using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BlazorApp2;

public class UbuntuCANInterface
{
    private string canInterface = "";
    private byte nodeId;
    private bool isConnected = false;
    private readonly object lockObject = new object();
    private Process? monitorProcess;
    private readonly ConcurrentQueue<string> canFrames = new ConcurrentQueue<string>();
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
            this.canInterface = interfaceName;
            this.nodeId = nodeId;
            Console.WriteLine($"Thiết lập giao diện CAN {interfaceName} với baudrate {baudrate}");

            ExecuteCommand($"sudo ip link set {interfaceName} down");
            ExecuteCommand($"sudo ip link set {interfaceName} type can bitrate {baudrate}");
            var result = ExecuteCommand($"sudo ip link set {interfaceName} up");

            if (result.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Lỗi thiết lập giao diện CAN: {result}");
                return false;
            }

            var status = ExecuteCommand($"ip -details link show {interfaceName}");
            if (status.Contains("UP") && status.Contains("can"))
            {
                isConnected = true;
                Console.WriteLine($"Kết nối thành công {interfaceName} với Node ID: {nodeId}");

                // Bắt đầu monitoring trước
                StartCANMonitoring();
                Thread.Sleep(500);

                // Gửi NMT Reset Node để đưa thiết bị về trạng thái Pre-Operational
                SendNMT(0x81, nodeId);
                Console.WriteLine("Đã gửi NMT Reset Node");
                Thread.Sleep(1000);

                // Gửi NMT Start để chuyển sang Operational (kích hoạt PDO)
                SendNMT(0x01, nodeId);
                Console.WriteLine("Đã gửi NMT Start Node - PDO đã được kích hoạt");
                Thread.Sleep(500);

                return true;
            }

            Console.WriteLine($"Trạng thái interface: {status}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi kết nối CAN: {ex.Message}");
            return false;
        }
    }


    public bool SendNMT(byte command, byte targetNodeId)
    {
        if (!isConnected) return false;
        try
        {
            uint cobId = 0x000;
            byte[] data = new byte[2] { command, targetNodeId };
            string frameData = BitConverter.ToString(data).Replace("-", "");
            ExecuteCommand($"cansend {canInterface} {cobId:X3}#{frameData}");
            Console.WriteLine($"NMT command sent: 0x{command:X2} to node {targetNodeId}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi SendNMT: {ex.Message}");
            return false;
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
                        CANFrameReceived?.Invoke(this, e.Data);
                        ProcessPDOMessage(e.Data);
                    }
                };
                monitorProcess.Start();
                monitorProcess.BeginOutputReadLine();
                monitorProcess.WaitForExit();
            }
            catch (Exception ex) { Console.WriteLine($"Lỗi monitor CAN: {ex.Message}"); }
        });
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

    private byte[] HexStringToByteArray(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();
        int length = hex.Length / 2;
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    public void Disconnect()
    {
        if (isConnected)
        {
            isMonitoring = false;
            try { monitorProcess?.Kill(); monitorProcess?.Dispose(); } catch { }
            ExecuteCommand($"sudo ip link set {canInterface} down");
            isConnected = false;
            Console.WriteLine("Ngắt kết nối CAN interface");
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
                    _ => throw new ArgumentException($"Kích thước dữ liệu không hỗ trợ: {dataSize}")
                };
                string frameData = $"{command:X2}{index & 0xFF:X2}{(index >> 8) & 0xFF:X2}{subindex:X2}";
                frameData += $"{data & 0xFF:X2}{(data >> 8) & 0xFF:X2}{(data >> 16) & 0xFF:X2}{(data >> 24) & 0xFF:X2}";
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
                string frameData = $"40{index & 0xFF:X2}{(index >> 8) & 0xFF:X2}{subindex:X2}00000000";
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
        cobId = 0; dataHex = "";
        var idMatch = Regex.Match(line, @"\b([0-9A-Fa-f]{3})\b");
        if (!idMatch.Success) return false;
        string idStr = idMatch.Groups[1].Value;
        if (!uint.TryParse(idStr, System.Globalization.NumberStyles.HexNumber, null, out cobId))
            return false;
        var dataMatches = Regex.Matches(line, @"\b([0-9A-Fa-f]{2})\b");
        if (dataMatches.Count == 0) return true;
        var sb = new System.Text.StringBuilder();
        foreach (Match m in dataMatches) sb.Append(m.Groups[1].Value);
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
                    if (cobId == expectedCOBID && !string.IsNullOrEmpty(dataHex))
                    {
                        try
                        {
                            byte responseCmd = Convert.ToByte(dataHex.Substring(0, 2), 16);
                            if (responseCmd == 0x80) return false;
                            if (isWrite && responseCmd == 0x60) return true;
                        }
                        catch { }
                    }
                }
            }
            Thread.Sleep(10);
        }
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
                            byte responseCmd = Convert.ToByte(dataHex.Substring(0, 2), 16);
                            if (responseCmd == 0x80) return 0;
                            return responseCmd switch
                            {
                                0x4F => Convert.ToByte(dataHex.Substring(8, 2), 16),
                                0x4B => (uint)(Convert.ToByte(dataHex.Substring(8, 2), 16) |
                                              (Convert.ToByte(dataHex.Substring(10, 2), 16) << 8)),
                                0x43 => (uint)(Convert.ToByte(dataHex.Substring(8, 2), 16) |
                                               (Convert.ToByte(dataHex.Substring(10, 2), 16) << 8) |
                                               (Convert.ToByte(dataHex.Substring(12, 2), 16) << 16) |
                                               (Convert.ToByte(dataHex.Substring(14, 2), 16) << 24)),
                                _ => 0u
                            };
                        }
                        catch { }
                    }
                }
            }
            Thread.Sleep(10);
        }
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
                return error;
            return output;
        }
        catch (Exception ex) { return $"Lỗi: {ex.Message}"; }
    }
}
