using Microsoft.Extensions.Logging;

namespace BlazorApp2.Services;

public class MotorManager
{
    private CiA402Motor? _motor;
    private UbuntuCANInterface? _can;
    public event Action<string>? OnCanFrameReceived;
    public event Action<CanLogEntry>? OnCanLog;

    public bool IsConnected { get; private set; }

    public MotorManager()
    {
        _can = new UbuntuCANInterface();
        _motor = new CiA402Motor(_can, 0x01);

        _can.CANFrameReceived += (s, frame) => LogReceive(frame);
        _can.CANFrameTransmitted += (s, frame) => LogSend(frame);
    }

    #region Kết nối & Ngắt kết nối

    public bool Connect()
    {
        if (_can == null || _motor == null)
        {
            LogMessage("❌ Connect: CAN interface hoặc motor null");
            return false;
        }

        try
        {
            IsConnected = _can.Connect("can0", 500000);
            if (!IsConnected)
            {
                LogMessage("❌ Không thể kết nối CAN interface");
                return false;
            }

            LogMessage("✅ Đã kết nối CAN interface");

            if (_motor.ConfigurePDO())
            {
                _motor.EnablePDOMode(true);
                LogMessage("✅ Đã kích hoạt chế độ PDO");
            }
            else
            {
                LogMessage("⚠️ Không thể cấu hình PDO cho motor");
            }

            return true;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Connect exception: {ex.Message}");
            return false;
        }
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
            LogMessage("✅ Đã ngắt kết nối CAN");
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Disconnect error: {ex.Message}");
        }
    }

    #endregion

    #region Điều khiển chính

    public bool StartHoming(byte method = 1)
    {
        if (_motor == null) return LogFail("StartHoming: motor null");
        return _motor.HomingMode(method);
    }

    public bool MoveToPosition(double targetRad, uint vel = 20, uint acc = 100, uint dec = 100)
    {
        if (_motor == null) return LogFail("MoveToPosition: motor null");
        return _motor.MoveToPositionRad(targetRad, vel, acc, dec);
    }

    public bool SetVelocity(double rpm)
    {
        if (_motor == null) return LogFail("SetVelocity: motor null");
        return _motor.SetVelocityRpm(rpm);
    }

    public bool SetTorque(short torque)
    {
        if (_motor == null) return LogFail("SetTorque: motor null");
        return _motor.SetTorque(torque);
    }

    public bool StopMotor()
    {
        if (_motor == null) return LogFail("StopMotor: motor null");
        try
        {
            bool ok = _motor.StopMotor();
            LogMessage(ok ? "✅ Motor đã dừng" : "❌ Không thể dừng motor");
            return ok;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ StopMotor error: {ex.Message}");
            return false;
        }
    }

    public bool QuickStop()
    {
        if (_motor == null) return LogFail("QuickStop: motor null");
        try
        {
            bool ok = _motor.QuickStop();
            LogMessage(ok ? "✅ QuickStop thành công" : "⚠️ QuickStop thất bại");
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
        if (_motor == null) return LogFail("DisableMotor: motor null");
        try
        {
            bool ok = _motor.Disable();
            LogMessage(ok ? "✅ Disable motor thành công" : "⚠️ Disable motor thất bại");
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
        if (_motor == null) return LogFail("ResetFault: motor null");
        try
        {
            bool ok = _motor.ResetFault();
            LogMessage(ok ? "✅ Reset lỗi thành công" : "⚠️ Reset lỗi thất bại");
            return ok;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ ResetFault error: {ex.Message}");
            return false;
        }
    }

    public bool ResetMotor()
    {
        if (_motor == null) return LogFail("ResetMotor: motor null");

        try
        {
            if (_motor.ResetMotor())
            {
                LogMessage("🔧 Đang khởi tạo lại motor sau reset...");
                Thread.Sleep(500);

                bool ok = _motor.Initialize();
                LogMessage(ok ? "✅ Motor đã được khởi tạo lại" : "❌ Khởi tạo lại motor thất bại");
                return ok;
            }

            LogMessage("❌ ResetMotor: ResetNode thất bại");
            return false;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ ResetMotor error: {ex.Message}");
            return false;
        }
    }

    public bool ResetFaultAndEnable(int waitAfterResetMs = 200)
    {
        if (_motor == null) return LogFail("ResetFaultAndEnable: motor null");

        try
        {
            LogMessage("🔁 Reset lỗi và bật lại motor...");
            if (!ResetFault())
            {
                LogMessage("⚠️ Reset lỗi thất bại, không thể bật motor");
                return false;
            }

            Thread.Sleep(waitAfterResetMs);
            bool enabled = _motor.EnableOperation();

            LogMessage(enabled ? "✅ Motor đã bật (Operation Enabled)" : "❌ Không bật được motor sau reset");
            return enabled;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ ResetFaultAndEnable error: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Đọc dữ liệu trạng thái

    public double GetPosition() => _motor?.GetActualPositionRad() ?? 0;
    public double GetVelocity() => _motor?.GetActualVelocityRpm() ?? 0;
    public short GetTorque() => _motor?.GetActualTorque() ?? 0;
    public string GetStatusDescription() => _motor?.GetStatusDescription() ?? "No data";

    #endregion

    #region Logging

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

    public void LogMessage(string msg)
    {
        OnCanLog?.Invoke(new CanLogEntry
        {
            Timestamp = DateTime.Now,
            Direction = "SYS",
            Msg = msg
        });
    }

    private bool LogFail(string msg)
    {
        LogMessage($"❌ {msg}");
        return false;
    }

    #endregion
}
