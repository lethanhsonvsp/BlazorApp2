using Microsoft.Extensions.Logging;

namespace BlazorApp2.Services
{
    public class MotorManager
    {
        private readonly ILogger<MotorManager> _logger;
        private CiA402Motor? _motor;
        private UbuntuCANInterface? _can;
        private byte _nodeId;
        public event Action<string>? OnCanFrameReceived;
        public event Action<CanLogEntry>? OnCanLog;

        public MotorManager()
        {
            _can = new UbuntuCANInterface();
            _motor = new CiA402Motor(_can, 0x01);

            // nối sự kiện
            _can.CANFrameReceived += (s, frame) =>
            {
                LogReceive(frame); // RX
            };

            _can.CANFrameTransmitted += (s, frame) =>
            {
                LogSend(frame); // TX
            };

        }
        public bool IsConnected { get; private set; }

        public bool Connect(string ifName, int baudrate, byte nodeId)
        {
            IsConnected = _can.Connect(ifName, baudrate, nodeId);
            return IsConnected;
        }

        /// <summary>
        /// Khởi tạo motor sau khi CAN kết nối
        /// </summary>
        public bool InitializeFromCan(UbuntuCANInterface canInterface, byte nodeId)
        {
            _can = canInterface;
            _nodeId = nodeId;

            try
            {
                _motor = new CiA402Motor(_can, _nodeId);
                return _motor.Initialize();
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public bool StartHoming(byte method = 1)
        {
            if (_motor == null) return false;
            return _motor.StartHoming(method);
        }

        public bool MoveToPosition(int pos, uint vel = 10000, uint acceleration = 1000000, uint deceleration = 1000000)
        {
            if (_motor == null) return false;
            return _motor.MoveToPosition(pos, vel, acceleration, deceleration);
        }

        public bool SetVelocity(int vel)
        {
            if (_motor == null) return false;
            return _motor.SetVelocity(vel);
        }

        public bool SetTorque(short t)
        {
            if (_motor == null) return false;
            return _motor.SetTorque(t);
        }

        public int GetPosition()
        {
            if (_motor == null) return 0;
            return _motor.GetActualPosition();
        }

        public int GetVelocity()
        {
            if (_motor == null) return 0;
            return _motor.GetActualVelocity();
        }

        public short GetTorque()
        {
            if (_motor == null) return 0;
            return _motor.GetActualTorque();
        }

        public uint GetStatus()
        {
            if (_motor == null) return 0;
            return _motor.GetStatusWord();
        }

        public void DisableMotor()
        {
            try
            {
                _motor?.Disable();
            }
            catch { }
        }
        public void LogSend(string frame)
        {
            OnCanLog?.Invoke(new CanLogEntry
            {
                Direction = "TX",
                Frame = frame
            });
        }

        public void LogReceive(string frame)
        {
            OnCanLog?.Invoke(new CanLogEntry
            {
                Direction = "RX",
                Frame = frame
            });
        }
        public void Disconnect()
        {
            try
            {
                // Đóng socket CAN
                _can?.Disconnect();
                IsConnected = false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error when disconnecting CAN");
            }
        }

    }
}
