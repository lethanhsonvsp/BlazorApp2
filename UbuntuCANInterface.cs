using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BlazorApp2;

public class UbuntuCANInterface
{
    private string canInterface = "";
    private byte nodeId;
    private bool isConnected = false;
    private readonly object lockObject = new();
    private Process? monitorProcess;
    private readonly ConcurrentQueue<string> canFrames = new();
    private volatile bool isMonitoring = false;

    // RX từ candump
    public event EventHandler<string>? CANFrameReceived;
    // TX sau khi cansend
    public event EventHandler<string>? CANFrameTransmitted;

    private TPDO1Data latestTPDO1;
    private TPDO2Data latestTPDO2;
    private DateTime lastTPDO1Update = DateTime.MinValue;
    private DateTime lastTPDO2Update = DateTime.MinValue;

    public event EventHandler<TPDO1Data>? TPDO1Received;
    public event EventHandler<TPDO2Data>? TPDO2Received;

    public string GetInterfaceName() => canInterface;
    public TPDO1Data GetLatestTPDO1() => latestTPDO1;
    public TPDO2Data GetLatestTPDO2() => latestTPDO2;
    public DateTime GetLastTPDO1Time() => lastTPDO1Update;
    public DateTime GetLastTPDO2Time() => lastTPDO2Update;


    public bool Connect(string interfaceName, int baudrate, byte nodeId)
    {
        try
        {
            canInterface = interfaceName;
            this.nodeId = nodeId;

            Console.WriteLine($"Thiết lập CAN {interfaceName} baud {baudrate}");

            // Reset interface
            ExecuteCommand($"sudo ip link set {interfaceName} down");
            ExecuteCommand($"sudo ip link set {interfaceName} type can bitrate {baudrate}");
            var result = ExecuteCommand($"sudo ip link set {interfaceName} up");

            if (result.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Lỗi setup CAN: {result}");
                return false;
            }

            // Kiểm tra
            var status = ExecuteCommand($"ip -details link show {interfaceName}");
            if (status.Contains("UP") && status.Contains("can"))
            {
                isConnected = true;
                Console.WriteLine($"CAN interface {interfaceName} OK.");
                StartCANMonitoring();
                return true;
            }

            Console.WriteLine($"Trạng thái interface: {status}");

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi connect CAN: {ex.Message}");
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
                        canFrames.Enqueue(e.Data);
                        CANFrameReceived?.Invoke(this, e.Data); // RX
                    }
                };

                monitorProcess.Start();
                monitorProcess.BeginOutputReadLine();
                monitorProcess.WaitForExit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi monitor CAN: {ex.Message}");
            }
        });
    }
    private byte[] HexStringToByteArray(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();
        int length = hex.Length / 2;
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    public void ProcessPDOMessage(string candumpLine)
    {
        if (!TryParseCandumpLine(candumpLine, out uint cobId, out string dataHex))
            return;
        if (cobId == (0x180 + nodeId))
        {
            byte[] data = HexStringToByteArray(dataHex);
            latestTPDO1 = new TPDO1Data(data);
            lastTPDO1Update = DateTime.Now;
            TPDO1Received?.Invoke(this, latestTPDO1);
        }
        else if (cobId == (0x280 + nodeId))
        {
            byte[] data = HexStringToByteArray(dataHex);
            latestTPDO2 = new TPDO2Data(data);
            lastTPDO2Update = DateTime.Now;
            TPDO2Received?.Invoke(this, latestTPDO2);
        }
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
            Console.WriteLine($"CAN {canInterface} ngắt kết nối.");
        }
    }

    public bool WriteSDO(ushort index, byte subindex, uint data, byte dataSize)
    {
        if (!isConnected) return false;

        lock (lockObject)
        {
            try
            {
                while (canFrames.TryDequeue(out _)) { }

                byte command = dataSize switch
                {
                    1 => 0x2F,
                    2 => 0x2B,
                    4 => 0x23,
                    _ => throw new ArgumentException($"Size không hỗ trợ: {dataSize}")
                };

                string frameData =
                    $"{command:X2}{index & 0xFF:X2}{index >> 8 & 0xFF:X2}{subindex:X2}" +
                    $"{data & 0xFF:X2}{data >> 8 & 0xFF:X2}{data >> 16 & 0xFF:X2}{data >> 24 & 0xFF:X2}";

                uint cobId = (uint)(0x600 + nodeId);
                string cmd = $"cansend {canInterface} {cobId:X3}#{frameData}";
                ExecuteCommand(cmd);

                // TX event
                CANFrameTransmitted?.Invoke(this, $"{cobId:X3}#{frameData}");

                Console.WriteLine($"SDO Write: {cobId:X3}#{frameData}");

                return WaitForSDOResponse((uint)(0x580 + nodeId), true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi WriteSDO: {ex.Message}");
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
                while (canFrames.TryDequeue(out _)) { }

                string frameData = $"40{index & 0xFF:X2}{index >> 8 & 0xFF:X2}{subindex:X2}00000000";
                uint cobId = (uint)(0x600 + nodeId);

                string cmd = $"cansend {canInterface} {cobId:X3}#{frameData}";
                ExecuteCommand(cmd);

                // TX event
                CANFrameTransmitted?.Invoke(this, $"{cobId:X3}#{frameData}");

                Console.WriteLine($"SDO Read: {cobId:X3}#{frameData}");

                return WaitForSDOReadResponse((uint)(0x580 + nodeId));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi ReadSDO: {ex.Message}");
                return 0;
            }
        }
    }

    private static bool TryParseCandumpLine(string line, out uint cobId, out string dataHex)
    {
        cobId = 0;
        dataHex = "";

        // "can0  580   [8]  60 00 00 00 00 00 00 00"
        var match = Regex.Match(line, @"\b([0-9A-Fa-f]{3})\b\s+\[\d+\]\s+([0-9A-Fa-f ]+)");
        if (!match.Success) return false;

        if (!uint.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out cobId))
            return false;

        dataHex = match.Groups[2].Value.Replace(" ", "");
        return true;
    }

    private bool WaitForSDOResponse(uint expectedCOBID, bool isWrite, int timeoutMs = 2000)
    {
        var start = DateTime.Now;
        while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
        {
            if (canFrames.TryDequeue(out var frame) && !string.IsNullOrEmpty(frame))
            {
                if (TryParseCandumpLine(frame, out uint cobId, out string dataHex))
                {
                    if (cobId == expectedCOBID && !string.IsNullOrEmpty(dataHex))
                    {
                        try
                        {
                            byte cmd = Convert.ToByte(dataHex[..2], 16);

                            if (cmd == 0x80) { Console.WriteLine("SDO Abort"); return false; }
                            if (isWrite && cmd == 0x60) return true;
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
        var start = DateTime.Now;
        while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
        {
            if (canFrames.TryDequeue(out var frame) && !string.IsNullOrEmpty(frame))
            {
                if (TryParseCandumpLine(frame, out uint cobId, out string dataHex))
                {
                    if (cobId == expectedCOBID && !string.IsNullOrEmpty(dataHex))
                    {
                        try
                        {
                            byte cmd = Convert.ToByte(dataHex[..2], 16);

                            if (cmd == 0x80) { Console.WriteLine("SDO Abort"); return 0; }

                            return cmd switch
                            {
                                0x4F => Convert.ToByte(dataHex.Substring(8, 2), 16), // 1 byte
                                0x4B => (uint)(Convert.ToByte(dataHex.Substring(8, 2), 16) |
                                               Convert.ToByte(dataHex.Substring(10, 2), 16) << 8),
                                0x43 => (uint)(Convert.ToByte(dataHex.Substring(8, 2), 16) |
                                               Convert.ToByte(dataHex.Substring(10, 2), 16) << 8 |
                                               Convert.ToByte(dataHex.Substring(12, 2), 16) << 16 |
                                               Convert.ToByte(dataHex.Substring(14, 2), 16) << 24),
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
            var p = new Process
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

            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (!string.IsNullOrEmpty(error) && !error.Contains("File exists"))
                return error;

            return output;
        }
        catch (Exception ex)
        {
            return $"Lỗi: {ex.Message}";
        }
    }

    public bool SendRPDO1(ushort controlWord, int targetPosition)
    {
        if (!isConnected) return false;
        try
        {
            uint cobId = (uint)(0x200 + nodeId);
            byte[] data = new byte[6];
            data[0] = (byte)(controlWord & 0xFF);
            data[1] = (byte)((controlWord >> 8) & 0xFF);
            byte[] posBytes = BitConverter.GetBytes(targetPosition);
            Array.Copy(posBytes, 0, data, 2, 4);
            string frameData = BitConverter.ToString(data).Replace("-", "");
            ExecuteCommand($"cansend {canInterface} {cobId:X3}#{frameData}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi SendRPDO1: {ex.Message}");
            return false;
        }
    }

    public bool SendRPDO2(int targetVelocity, sbyte modesOfOperation)
    {
        if (!isConnected) return false;
        try
        {
            uint cobId = (uint)(0x300 + nodeId);
            byte[] data = new byte[5];
            byte[] velBytes = BitConverter.GetBytes(targetVelocity);
            Array.Copy(velBytes, 0, data, 0, 4);
            data[4] = (byte)modesOfOperation;
            string frameData = BitConverter.ToString(data).Replace("-", "");
            ExecuteCommand($"cansend {canInterface} {cobId:X3}#{frameData}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi SendRPDO2: {ex.Message}");
            return false;
        }
    }

    public bool SendRPDO3(short targetTorque)
    {
        if (!isConnected) return false;
        try
        {
            uint cobId = (uint)(0x400 + nodeId);
            byte[] data = BitConverter.GetBytes(targetTorque);
            string frameData = BitConverter.ToString(data).Replace("-", "");
            ExecuteCommand($"cansend {canInterface} {cobId:X3}#{frameData}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi SendRPDO3: {ex.Message}");
            return false;
        }
    }

}
