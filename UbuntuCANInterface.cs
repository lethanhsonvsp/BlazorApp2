using System;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Collections.Concurrent;
using SocketCANSharp;
using SocketCANSharp.Network;

namespace BlazorApp2
{

    public class UbuntuCANInterface
    {
        private string canInterface = "";
        private byte nodeId;
        private bool isConnected = false;
        private readonly object sdoLock = new();
        private readonly object nodeLock = new();
        private CanNetworkInterface? canInterfaceHandle;
        private RawCanSocket? canSocket;
        private readonly ConcurrentQueue<CanFrame> canFrames = new();
        private volatile bool isMonitoring = false;
        private const int MAX_QUEUE_SIZE = 500;
        private readonly ConcurrentDictionary<byte, TPDO1Data> latestTPDO1 = new();
        private readonly ConcurrentDictionary<byte, TPDO2Data> latestTPDO2 = new();
        private readonly ConcurrentDictionary<byte, DateTime> lastTPDO1Update = new();
        private readonly ConcurrentDictionary<byte, DateTime> lastTPDO2Update = new();

        // RX từ CAN
        public event EventHandler<string>? CANFrameReceived;
        // TX sau khi gửi frame
        public event EventHandler<string>? CANFrameTransmitted;
        public event EventHandler<TPDO1Data>? TPDO1Received;
        public event EventHandler<TPDO2Data>? TPDO2Received;

        public string GetInterfaceName() => canInterface;
        public TPDO1Data GetLatestTPDO1() => latestTPDO1.TryGetValue(nodeId, out var data) ? data : new TPDO1Data();
        public TPDO2Data GetLatestTPDO2() => latestTPDO2.TryGetValue(nodeId, out var data) ? data : new TPDO2Data();
        public DateTime GetLastTPDO1Time() => lastTPDO1Update.TryGetValue(nodeId, out var time) ? time : DateTime.MinValue;
        public DateTime GetLastTPDO2Time() => lastTPDO2Update.TryGetValue(nodeId, out var time) ? time : DateTime.MinValue;

        public bool Connect(string interfaceName, int baudrate)
        {
            try
            {
                this.canInterface = interfaceName;
                Console.WriteLine($"Thiết lập giao diện CAN {interfaceName} với baudrate {baudrate}");
                ExecuteCommand($"sudo ip link set {interfaceName} down");
                ExecuteCommand($"sudo ip link set {interfaceName} type can bitrate {baudrate}");
                string result = ExecuteCommand($"sudo ip link set {interfaceName} up");
                if (result.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Lỗi thiết lập giao diện CAN: {result}");
                    return false;
                }
                canSocket = new RawCanSocket();
                canInterfaceHandle = CanNetworkInterface.GetAllInterfaces(true).FirstOrDefault(i => i.Name == interfaceName);
                if (canInterfaceHandle == null)
                {
                    Console.WriteLine($"CAN: Interface {interfaceName} not found");
                    return false;
                }
                canSocket.Bind(canInterfaceHandle);
                isConnected = true;

                StartCANMonitoring();
                Thread.Sleep(500);
                SendNMT(0x81, 1);
                SendNMT(0x81, 2);
                Console.WriteLine("Đã gửi NMT Reset Node");
                Thread.Sleep(1000);
                SendNMT(0x01, 1);
                SendNMT(0x01, 2);
                Console.WriteLine("Đã gửi NMT Start Node - PDO đã được kích hoạt");
                Thread.Sleep(500);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi kết nối CAN: {ex.Message}");
                return false;
            }
        }

        public bool SendNMT(byte command, byte targetNodeId)
        {
            if (!isConnected || canSocket == null) return false;
            try
            {
                uint cobId = 0x000;
                byte[] data = [command, targetNodeId];
                var frame = new CanFrame
                {
                    CanId = cobId,
                    Data = data,
                    Length = (byte)data.Length
                };
                canSocket.Write(frame);
                string frameData = BitConverter.ToString(data, 0, frame.Length).Replace("-", "");
                CANFrameTransmitted?.Invoke(this, $"{cobId:X3}#{frameData}");
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
            if (!isConnected || canSocket == null) return false;
            try
            {
                uint cobId = (uint)(0x200 + nodeId);
                byte[] data = new byte[6];
                data[0] = (byte)(controlWord & 0xFF);
                data[1] = (byte)((controlWord >> 8) & 0xFF);
                byte[] posBytes = BitConverter.GetBytes(targetPosition);
                Array.Copy(posBytes, 0, data, 2, 4);
                var frame = new CanFrame
                {
                    CanId = cobId,
                    Data = data,
                    Length = (byte)data.Length
                };
                canSocket.Write(frame);
                string frameData = BitConverter.ToString(data, 0, frame.Length).Replace("-", "");
                CANFrameTransmitted?.Invoke(this, $"{cobId:X3}#{frameData}");
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
            if (!isConnected || canSocket == null) return false;
            try
            {
                uint cobId = (uint)(0x300 + nodeId);
                byte[] data = new byte[5];
                byte[] velBytes = BitConverter.GetBytes(targetVelocity);
                Array.Copy(velBytes, 0, data, 0, 4);
                data[4] = (byte)modesOfOperation;
                var frame = new CanFrame
                {
                    CanId = cobId,
                    Data = data,
                    Length = (byte)data.Length
                };
                canSocket.Write(frame);
                string frameData = BitConverter.ToString(data, 0, frame.Length).Replace("-", "");
                CANFrameTransmitted?.Invoke(this, $"{cobId:X3}#{frameData}");
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
            if (!isConnected || canSocket == null) return false;
            try
            {
                uint cobId = (uint)(0x400 + nodeId);
                byte[] data = BitConverter.GetBytes(targetTorque);
                var frame = new CanFrame
                {
                    CanId = cobId,
                    Data = data,
                    Length = (byte)data.Length
                };
                canSocket.Write(frame);
                string frameData = BitConverter.ToString(data, 0, frame.Length).Replace("-", "");
                CANFrameTransmitted?.Invoke(this, $"{cobId:X3}#{frameData}");
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
            if (!isConnected || canSocket == null) return false;
            lock (nodeLock)
            {
                lock (sdoLock)
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
                        byte[] frameData = new byte[8];
                        frameData[0] = command;
                        frameData[1] = (byte)(index & 0xFF);
                        frameData[2] = (byte)(index >> 8);
                        frameData[3] = subindex;
                        byte[] dataBytes = BitConverter.GetBytes(data);
                        Array.Copy(dataBytes, 0, frameData, 4, Math.Min((int)dataSize, 4));
                        uint cobId = (uint)(0x600 + nodeId);
                        var frame = new CanFrame
                        {
                            CanId = cobId,
                            Data = frameData,
                            Length = (byte)frameData.Length
                        };
                        canSocket.Write(frame);
                        string frameDataStr = BitConverter.ToString(frameData, 0, frame.Length).Replace("-", "");
                        CANFrameTransmitted?.Invoke(this, $"{cobId:X3}#{frameDataStr}");
                        Console.WriteLine($"SDO Write: {cobId:X3}#{frameDataStr}");
                        return WaitForSDOResponse((uint)(0x580 + nodeId), true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Lỗi WriteSDO: {ex.Message}");
                        return false;
                    }
                }
            }
        }

        public uint ReadSDO(ushort index, byte subindex)
        {
            if (!isConnected || canSocket == null) return 0;
            lock (nodeLock)
            {
                lock (sdoLock)
                {
                    try
                    {
                        while (canFrames.TryDequeue(out _)) { }
                        byte[] frameData = new byte[8];
                        frameData[0] = 0x40;
                        frameData[1] = (byte)(index & 0xFF);
                        frameData[2] = (byte)(index >> 8);
                        frameData[3] = subindex;
                        uint cobId = (uint)(0x600 + nodeId);
                        var frame = new CanFrame
                        {
                            CanId = cobId,
                            Data = frameData,
                            Length = (byte)frameData.Length
                        };
                        canSocket.Write(frame);
                        string frameDataStr = BitConverter.ToString(frameData, 0, frame.Length).Replace("-", "");
                        CANFrameTransmitted?.Invoke(this, $"{cobId:X3}#{frameDataStr}");
                        Console.WriteLine($"SDO Read: {cobId:X3}#{frameDataStr}");
                        return WaitForSDOReadResponse((uint)(0x580 + nodeId));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Lỗi ReadSDO: {ex.Message}");
                        return 0;
                    }
                }
            }
        }

        private void StartCANMonitoring()
        {
            if (isMonitoring || canSocket == null) return;
            isMonitoring = true;
            Task.Run(() =>
            {
                try
                {
                    while (isMonitoring)
                    {
                        CanFrame frame;
                        canSocket.Read(out frame);
                        while (canFrames.Count > MAX_QUEUE_SIZE)
                            canFrames.TryDequeue(out _);
                        canFrames.Enqueue(frame);
                        string frameStr = FrameToString(frame);
                        CANFrameReceived?.Invoke(this, frameStr);
                        ProcessPDOMessage(frame);
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lỗi monitor CAN: {ex.Message}");
                    isMonitoring = false;
                }
            });
        }

        private void ProcessPDOMessage(CanFrame frame)
        {
            uint cobId = frame.CanId;
            if (cobId == (0x180 + nodeId))
            {
                var tpdo1 = new TPDO1Data(frame.Data);
                latestTPDO1[nodeId] = tpdo1;
                lastTPDO1Update[nodeId] = DateTime.Now;
                TPDO1Received?.Invoke(this, tpdo1);
            }
            else if (cobId == (0x280 + nodeId))
            {
                var tpdo2 = new TPDO2Data(frame.Data);
                latestTPDO2[nodeId] = tpdo2;
                lastTPDO2Update[nodeId] = DateTime.Now;
                TPDO2Received?.Invoke(this, tpdo2);
            }
        }

        public void Disconnect()
        {
            if (isConnected)
            {
                isMonitoring = false;
                try
                {
                    canSocket?.Close();
                    canSocket?.Dispose();
                    ExecuteCommand($"sudo ip link set {canInterface} down");
                    Console.WriteLine($"Ngắt kết nối CAN interface {canInterface}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lỗi ngắt kết nối CAN: {ex.Message}");
                }
                isConnected = false;
            }
        }

        private bool WaitForSDOResponse(uint expectedCOBID, bool isWrite, int timeoutMs = 2000)
        {
            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (canFrames.TryDequeue(out CanFrame frame))
                {
                    if (frame.CanId == expectedCOBID && frame.Length > 0)
                    {
                        try
                        {
                            byte responseCmd = frame.Data[0];
                            if (responseCmd == 0x80) return false;
                            if (isWrite && responseCmd == 0x60) return true;
                        }
                        catch { }
                    }
                }
                Thread.Sleep(1);
            }
            return false;
        }

        private uint WaitForSDOReadResponse(uint expectedCOBID, int timeoutMs = 2000)
        {
            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (canFrames.TryDequeue(out CanFrame frame))
                {
                    if (frame.CanId == expectedCOBID && frame.Length > 0)
                    {
                        try
                        {
                            byte responseCmd = frame.Data[0];
                            if (responseCmd == 0x80) return 0;
                            return responseCmd switch
                            {
                                0x4F => frame.Data[4],
                                0x4B => (uint)(frame.Data[4] | (frame.Data[5] << 8)),
                                0x43 => (uint)(frame.Data[4] | (frame.Data[5] << 8) | (frame.Data[6] << 16) | (frame.Data[7] << 24)),
                                _ => 0u
                            };
                        }
                        catch { }
                    }
                }
                Thread.Sleep(1);
            }
            return 0;
        }

        private static string FrameToString(CanFrame frame)
        {
            var dataHex = BitConverter.ToString(frame.Data, 0, frame.Length).Replace("-", "");
            return $"{frame.CanId:X3}#{dataHex}";
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
            catch (Exception ex)
            {
                return $"Lỗi: {ex.Message}";
            }
        }
    }
}