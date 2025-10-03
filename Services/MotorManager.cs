using Microsoft.Extensions.Logging;

namespace BlazorApp2.Services;

public class MotorManager
{
    private CiA402Motor? _motor;
    private UbuntuCANInterface? _can;
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

    public bool Connect()
    {
        if (_can == null)
        {
            LogMessage("❌ Connect: CAN interface null");
            return false;
        }
        if (_motor == null)
        {
            LogMessage("❌ Connect: motor null");
            return false;
        }
        try 
        { 
            bool canOk = IsConnected = _can!.Connect("can0", 500000, 1);
            if (!canOk)
            {
                LogMessage("❌ Cannot connect to CAN interface");
                return false;
            }
            LogMessage("✅ Connected to CAN interface");
            IsConnected = true;
            return true;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Connect exception: {ex.Message}");
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

    public bool QuickStop()
    {
        if (_motor == null)
        {
            LogMessage("❌ ResetFault: motor null");
            return false;
        }
        try
        {
            bool ok = _motor?.QuickStop() ?? false;
            LogMessage(ok ? "✅ QuickStop lỗi thành công" : "⚠️ QuickStop lỗi không thành công");
            return ok;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ QuickStop error: {ex.Message}");
            return false;
        }
    }

    public bool DisableMotor()
    {
        if (_motor == null)
        {
            LogMessage("❌ ResetFault: motor null");
            return false;
        }
        try
        {
            bool ok = _motor?.Disable() ?? false;
            LogMessage(ok ? "✅ Disable motor thành công" : "⚠️ Disable motor không thành công");
            return ok;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ DisableMotor error: {ex.Message}");
            return false;
        }

    }

    public bool ResetFault()
    {
        if (_motor == null)
        {
            LogMessage("❌ ResetFault: motor null");
            return false;
        }

        try
        {
            bool ok = _motor.ResetFault();
            LogMessage(ok ? "✅ Reset lỗi thành công" : "⚠️ Reset lỗi không thành công");
            return ok;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Exception ResetFault: {ex.Message}");
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
            bool ok = _motor.ResetNode();
            LogMessage(ok ? "✅ Reset lỗi thành công" : "⚠️ Reset lỗi không thành công");
            return ok;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Exception ResetFault: {ex.Message}");
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
            return false;
        }
    }

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

            Thread.Sleep(waitAfterResetMs);

            bool enabled = _motor.EnableOperation();
            LogMessage(enabled ? "✅ Motor đã được bật (Operation Enabled)" : "❌ Không bật được motor sau reset");
            return enabled;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Exception ResetFaultAndEnable: {ex.Message}");
            return false;
        }
    }

    public bool StopMotor()
    {
        if (_motor == null)
        {
            LogMessage("❌ StopMotor: motor null");
            return false;
        }
        try
        {
            bool ok = _motor.Stop();
            LogMessage(ok ? "✅ Motor đã dừng" : "❌ Không thể dừng motor");
            return ok;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Exception StopMotor: {ex.Message}");
            return false;
        }
    }

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

        OnCanFrameReceived?.Invoke(frame);
    }

    public void Disconnect()
    {
        try
        {
            _can?.Disconnect();
            _can = null;
            _motor = null;
            IsConnected = false;
            GC.SuppressFinalize(this);
            LogMessage("✅ Disconnected from CAN");
        }
        catch (Exception ex)
        {
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
