using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BlazorApp2
{
    public class UbuntuCANInterface
    {
        private string canInterface = "";
        private byte nodeId;
        private bool isConnected = false;
        private readonly object lockObject = new();
        private Process? monitorProcess;
        private readonly ConcurrentQueue<string> canFrames = new();
        private volatile bool isMonitoring = false;

        public event EventHandler<string>? CANFrameReceived;
        public bool IsConnected => isConnected;

        public bool Connect(string interfaceName, int baudrate, byte nodeId)
        {
            try
            {
                canInterface = interfaceName;
                this.nodeId = nodeId;

                Console.WriteLine($"Thiet lap giao dien CAN {interfaceName} voi baudrate {baudrate}");

                // Tắt interface trước
                ExecuteCommand($"sudo ip link set {interfaceName} down");

                // Cấu hình baudrate
                ExecuteCommand($"sudo ip link set {interfaceName} type can bitrate {baudrate}");

                // Bật interface
                var result = ExecuteCommand($"sudo ip link set {interfaceName} up");

                if (result.Contains("error") || result.Contains("Error"))
                {
                    Console.WriteLine($"Loi thiet lap giao dien CAN: {result}");
                    return false;
                }

                // Kiểm tra trạng thái interface
                var status = ExecuteCommand($"ip -details link show {interfaceName}");
                if (status.Contains("UP") && status.Contains("can"))
                {
                    isConnected = true;
                    Console.WriteLine($"CAN interface {interfaceName} da ket noi thanh cong.");
                    // Bắt đầu monitor CAN
                    StartCANMonitoring();
                    return true;
                }
                else
                {
                    Console.WriteLine($"Trang thai interface: {status}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Loi ket noi CAN: {ex.Message}");
                return false;
            }
        }

        private void StartCANMonitoring()
        {
            if (isMonitoring) return;
            isMonitoring = true;
            Task.Run(() =>
            {
                try
                {
                    monitorProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "candump",
                            Arguments = $"{canInterface}",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };

                    monitorProcess.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data) && isMonitoring)
                        {
                            // enqueue raw line from candump
                            canFrames.Enqueue(e.Data);
                            CANFrameReceived?.Invoke(this, e.Data);
                        }
                    };

                    monitorProcess.Start();
                    monitorProcess.BeginOutputReadLine();
                    monitorProcess.WaitForExit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Loi monitor CAN: {ex.Message}");
                }
            });
        }


        public void Disconnect()
        {
            if (isConnected)
            {
                isMonitoring = false;

                try
                {
                    monitorProcess?.Kill();
                    monitorProcess?.Dispose();
                }
                catch { }

                ExecuteCommand($"sudo ip link set {canInterface} down");
                isConnected = false;
                Console.WriteLine($"CAN interface {canInterface} da ngat ket noi.");
            }
        }

        public bool WriteSDO(ushort index, byte subindex, uint data, byte dataSize)
        {
            if (!isConnected) return false;

            lock (lockObject)
            {
                try
                {
                    // Xóa frame cũ
                    while (canFrames.TryDequeue(out _)) { }

                    // Tạo SDO write command
                    byte command = dataSize switch
                    {
                        1 => 0x2F, // Write 1 byte
                        2 => 0x2B, // Write 2 bytes
                        4 => 0x23, // Write 4 bytes
                        _ => throw new ArgumentException($"Kich thuoc du lieu khong ho tro: {dataSize}")
                    };

                    // Tạo CAN frame data (Little Endian)
                    string frameData = $"{command:X2}{index & 0xFF:X2}{index >> 8 & 0xFF:X2}{subindex:X2}";
                    frameData += $"{data & 0xFF:X2}{data >> 8 & 0xFF:X2}{data >> 16 & 0xFF:X2}{data >> 24 & 0xFF:X2}";

                    // Gửi SDO request
                    uint cobId = (uint)(0x600 + nodeId);
                    ExecuteCommand($"cansend {canInterface} {cobId:X3}#{frameData}");

                    Console.WriteLine($"SDO Write: Index=0x{index:X4}, Sub=0x{subindex:X2}, Data=0x{data:X8}, Size={dataSize}");

                    // Chờ response
                    return WaitForSDOResponse((uint)(0x580 + nodeId), true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Loi WriteSDO: {ex.Message}");
                    return false;
                }
            }
        }

        public uint ReadSDO(ushort index, byte subindex)
        {
            if (!isConnected) return 0;

            lock (lockObject)
            {
                try
                {
                    // Xóa frame cũ
                    while (canFrames.TryDequeue(out _)) { }

                    // Tạo SDO read command
                    string frameData = $"40{index & 0xFF:X2}{index >> 8 & 0xFF:X2}{subindex:X2}00000000";

                    // Gửi SDO read request
                    uint cobId = (uint)(0x600 + nodeId);
                    ExecuteCommand($"cansend {canInterface} {cobId:X3}#{frameData}");

                    Console.WriteLine($"SDO Read: Index=0x{index:X4}, Sub=0x{subindex:X2}");

                    // Chờ và đọc response
                    return WaitForSDOReadResponse((uint)(0x580 + nodeId));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Loi ReadSDO: {ex.Message}");
                    return 0;
                }
            }
        }
        private static bool TryParseCandumpLine(string line, out uint cobId, out string dataHex)
        {
            cobId = 0;
            dataHex = "";

            // Example candump line:
            // "  can0  580   [8]  60 00 00 00 00 00 00 00"
            var idMatch = Regex.Match(line, @"\b([0-9A-Fa-f]{3})\b");
            if (!idMatch.Success) return false;

            string idStr = idMatch.Groups[1].Value;
            if (!uint.TryParse(idStr, System.Globalization.NumberStyles.HexNumber, null, out cobId))
                return false;

            // Collect all 2-hex-byte sequences as data bytes
            var dataMatches = Regex.Matches(line, @"\b([0-9A-Fa-f]{2})\b");
            if (dataMatches.Count == 0) return true; // no data bytes but still return id

            var sb = new System.Text.StringBuilder();
            foreach (Match m in dataMatches)
            {
                sb.Append(m.Groups[1].Value);
            }
            dataHex = sb.ToString();
            return true;
        }
        private bool WaitForSDOResponse(uint expectedCOBID, bool isWrite, int timeoutMs = 2000)
        {
            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (canFrames.TryDequeue(out string? frame) && !string.IsNullOrEmpty(frame))
                {
                    if (TryParseCandumpLine(frame, out uint cobId, out string dataHex))
                    {
                        if (cobId == expectedCOBID)
                        {
                            if (string.IsNullOrEmpty(dataHex)) continue;
                            try
                            {
                                byte responseCmd = Convert.ToByte(dataHex[..2], 16);

                                if (responseCmd == 0x80)
                                {
                                    Console.WriteLine("SDO Abort nhan duoc");
                                    return false;
                                }

                                if (isWrite && responseCmd == 0x60)
                                {
                                    return true;
                                }
                            }
                            catch { }
                        }
                    }
                }
                Thread.Sleep(10);
            }

            Console.WriteLine("SDO response timeout");
            return false;
        }

        private uint WaitForSDOReadResponse(uint expectedCOBID, int timeoutMs = 2000)
        {
            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (canFrames.TryDequeue(out string? frame) && !string.IsNullOrEmpty(frame))
                {
                    if (TryParseCandumpLine(frame, out uint cobId, out string dataHex))
                    {
                        if (cobId == expectedCOBID && !string.IsNullOrEmpty(dataHex))
                        {
                            try
                            {
                                byte responseCmd = Convert.ToByte(dataHex[..2], 16);

                                if (responseCmd == 0x80)
                                {
                                    Console.WriteLine("SDO Abort nhan duoc");
                                    return 0;
                                }

                                // parse based on command (like trước)
                                return responseCmd switch
                                {
                                    0x4F => Convert.ToByte(dataHex.Substring(8, 2), 16), // 1 byte
                                    0x4B => (uint)(Convert.ToByte(dataHex.Substring(8, 2), 16) |
                                                  Convert.ToByte(dataHex.Substring(10, 2), 16) << 8), // 2 bytes
                                    0x43 => (uint)(Convert.ToByte(dataHex.Substring(8, 2), 16) |
                                                   Convert.ToByte(dataHex.Substring(10, 2), 16) << 8 |
                                                   Convert.ToByte(dataHex.Substring(12, 2), 16) << 16 |
                                                   Convert.ToByte(dataHex.Substring(14, 2), 16) << 24), // 4 bytes
                                    _ => 0u
                                };
                            }
                            catch { }
                        }
                    }
                }
                Thread.Sleep(10);
            }

            Console.WriteLine("SDO read response timeout");
            return 0;
        }

        private static string ExecuteCommand(string command)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"-c \"{command}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(error) && !error.Contains("RTNETLINK answers: File exists"))
                {
                    return error;
                }

                return output;
            }
            catch (Exception ex)
            {
                return $"Loi: {ex.Message}";
            }
        }
    }

}
