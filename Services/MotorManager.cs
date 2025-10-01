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

        public MotorManager()
        {
            _can = new UbuntuCANInterface();
            _motor = new CiA402Motor(_can, 0x01);

            // nối sự kiện
            _can.CANFrameReceived += (s, frame) =>
            {
                OnCanFrameReceived?.Invoke(frame);
            };
        }
        public bool IsConnected => _can.IsConnected;

        public bool Connect(string ifName, int baudrate, byte nodeId)
        {
            return _can.Connect(ifName, baudrate, nodeId);
        }

        public void Disconnect() => _can.Disconnect();

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

        public bool MoveToPosition(int pos, uint vel = 10000)
        {
            if (_motor == null) return false;
            return _motor.MoveToPosition(pos, vel);
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
    }
}
