using Microsoft.Extensions.Logging;

namespace BlazorApp2.Services
{
    public class MotorManager
    {
        private readonly ILogger<MotorManager>? _logger;
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
                LogMessage($"❌ InitializeFromCan error: {ex.Message}");
                _logger?.LogError(ex, "InitializeFromCan");
                return false;
            }
        }

        public bool StartHoming(byte method = 1)
        {
            if (_motor == null)
            {
                LogMessage("❌ StartHoming: motor null");
                return false;
            }
            return _motor.StartHoming(method);
        }

        public bool MoveToPosition(int pos, uint vel = 10000, uint acceleration = 1000000, uint deceleration = 1000000)
        {
            if (_motor == null)
            {
                LogMessage("❌ MoveToPosition: motor null");
                return false;
            }
            return _motor.MoveToPosition(pos, vel, acceleration, deceleration);
        }

        public bool SetVelocity(int vel)
        {
            if (_motor == null)
            {
                LogMessage("❌ SetVelocity: motor null");
                return false;
            }
            return _motor.SetVelocity(vel);
        }

        public bool SetTorque(short t)
        {
            if (_motor == null)
            {
                LogMessage("❌ SetTorque: motor null");
                return false;
            }
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

        public string GetStatusDescription()
        {
            return _motor?.GetStatusDescription() ?? "No data";
        }


        public void DisableMotor()
        {
            try
            {
                _motor?.Disable();
            }
            catch (Exception ex)
            {
                LogMessage($"❌ DisableMotor error: {ex.Message}");
                _logger?.LogError(ex, "DisableMotor");
            }
        }

        // ---------------- New: Reset fault methods ----------------

        /// <summary>
        /// Gửi lệnh reset fault tới motor (trả về true nếu WriteSDO báo OK)
        /// </summary>
        public bool ResetFault()
        {
            if (_motor == null)
            {
                LogMessage("❌ ResetFault: motor null");
                return false;
            }

            try
            {
                LogMessage("🔧 Gửi lệnh reset lỗi...");
                bool ok = _motor.ResetFault();
                LogMessage(ok ? "✅ Reset lỗi thành công" : "⚠️ Reset lỗi không thành công");
                return ok;
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Exception ResetFault: {ex.Message}");
                _logger?.LogError(ex, "ResetFault");
                return false;
            }
        }

        public bool ResetNode()
        {
            if (_motor == null)
            {
                LogMessage("❌ ResetMotor: motor null");
                return false;
            }
            try
            {
                LogMessage("🔧 Gửi lệnh reset động cơ...");
                bool ok = _motor.ResetNode();
                LogMessage(ok ? "✅ Reset lỗi thành công" : "⚠️ Reset lỗi không thành công");
                return ok;
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Exception ResetFault: {ex.Message}");
                _logger?.LogError(ex, "ResetFault");
                return false;
            }
        }
        public bool ResetMotor()
        {
            if (_motor == null)
            {
                LogMessage("❌ ResetMotor: motor null");
                return false;
            }
            try
            {
                if (_motor.ResetNode())
                {
                    LogMessage("🔧 Đang khởi tạo lại motor sau reset...");
                    Thread.Sleep(500);
                    bool ok = _motor.Initialize();
                    LogMessage(ok ? "✅ Motor đã được khởi tạo lại sau reset" : "❌ Không thể khởi tạo lại motor sau reset");
                    return ok;
                }
                else
                {
                    LogMessage("❌ ResetNode failed in ResetMotor");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Exception ResetMotor: {ex.Message}");
                _logger?.LogError(ex, "ResetMotor");
                return false;
            }
        }
        /// <summary>
        /// Reset lỗi rồi cố gắng enable operation (ResetFault -> EnableOperation).
        /// Useful khi muốn tự phục hồi và bật lại motor.
        /// </summary>
        public bool ResetFaultAndEnable(int waitAfterResetMs = 200)
        {
            if (_motor == null)
            {
                LogMessage("❌ ResetFaultAndEnable: motor null");
                return false;
            }

            try
            {
                LogMessage("🔁 Reset lỗi và bật lại motor...");
                if (!ResetFault())
                {
                    LogMessage("⚠️ Reset lỗi thất bại, không thể bật motor.");
                    return false;
                }

                // đợi một chút để node xử lý
                Thread.Sleep(waitAfterResetMs);

                bool enabled = _motor.EnableOperation();
                LogMessage(enabled ? "✅ Motor đã được bật (Operation Enabled)" : "❌ Không bật được motor sau reset");
                return enabled;
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Exception ResetFaultAndEnable: {ex.Message}");
                _logger?.LogError(ex, "ResetFaultAndEnable");
                return false;
            }
        }

        // ---------------- End new methods ----------------

        public void LogSend(string frame)
        {
            OnCanLog?.Invoke(new CanLogEntry
            {
                Timestamp = DateTime.Now,
                Direction = "TX",
                Frame = frame
            });
        }

        public void LogReceive(string frame)
        {
            OnCanLog?.Invoke(new CanLogEntry
            {
                Timestamp = DateTime.Now,
                Direction = "RX",
                Frame = frame
            });

            // forward legacy frame event if somebody listens to it
            OnCanFrameReceived?.Invoke(frame);
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
                LogMessage($"❌ Disconnect error: {ex.Message}");
            }
        }

        public void LogMessage(string msg)
        {
            OnCanLog?.Invoke(new CanLogEntry
            {
                Timestamp = DateTime.Now,
                Direction = "SYS",
                Msg = msg
            });
        }
    }
}
