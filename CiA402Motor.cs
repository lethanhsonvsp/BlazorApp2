using BlazorApp2;
using System.Diagnostics;
namespace BlazorApp2;

public class CiA402Motor
{
    private UbuntuCANInterface canInterface;
    private byte nodeId;
    private CiA402State currentState;
    private OperationMode currentMode;
    private bool usePDO = false;

    private const ushort CONTROL_WORD = 0x6040;
    private const ushort STATUS_WORD = 0x6041;
    //private const ushort MODES_OF_OPERATION = 0x6060;
    private const ushort TARGET_POSITION = 0x607A;
    private const ushort POSITION_ACTUAL = 0x6064;
    private const ushort TARGET_VELOCITY = 0x60FF;
    private const ushort VELOCITY_ACTUAL = 0x606C;
    private const ushort TARGET_TORQUE = 0x6071;
    private const ushort TORQUE_ACTUAL = 0x6077;
    private const ushort PROFILE_VELOCITY = 0x6081;
    private const ushort PROFILE_ACCELERATION = 0x6083;
    private const ushort PROFILE_DECELERATION = 0x6084;

    private const double ENCODER_RES = 1048576.0;
    private const double GEAR_RATIO = 10.0;
    private const double COUNTS_PER_REV_OUTPUT = ENCODER_RES * GEAR_RATIO;


    public CiA402Motor(UbuntuCANInterface canInterface, byte nodeId)
    {
        this.canInterface = canInterface;
        this.nodeId = nodeId;
        this.currentState = CiA402State.NotReadyToSwitchOn;
    }

    public double GetActualPositionRad()
    {
        int counts = GetActualPosition(); // đọc counts gốc
        return (counts / COUNTS_PER_REV_OUTPUT) * (2.0 * Math.PI);
    }
    public bool MoveToPositionRad(double targetRad, uint profileVelocityRpm = 20,uint accelerationRpmPerSec =100, uint decelerationRpmPerSec = 100)
    {
        // targetRad là góc trục ra
        int counts = (int)((targetRad / (2.0 * Math.PI)) * ENCODER_RES * GEAR_RATIO);

        // profileVelocityRpm là RPM động cơ → counts/s
        int targetVelCounts = (int)((profileVelocityRpm * ENCODER_RES) / 60.0);

        // accelerationRpmPerSec là RPM/s động cơ → counts/s²
        uint accelCounts = (uint)((accelerationRpmPerSec * ENCODER_RES) / 60.0);
        uint decelCounts = (uint)((decelerationRpmPerSec * ENCODER_RES) / 60.0);

        return MoveToPosition(counts, (uint)targetVelCounts, accelCounts, decelCounts);
    }
    // public double GetActualVelocityRpm()
    // {
    //     int countsPerSec = GetActualVelocity(); 
    //     return (countsPerSec * 60.0) / (ENCODER_RES * GEAR_RATIO);
    // }

    // public bool SetVelocityRpm(double rpm)
    // {
    //     int countsPerSec = (int)((rpm * ENCODER_RES * GEAR_RATIO) / 60.0);
    //     return SetVelocity(countsPerSec);
    // }

    public bool SetVelocityRpm(double rpm)
    {
        // rpm là RPM của động cơ
        // Encoder ở trục động cơ nên KHÔNG nhân GEAR_RATIO
        int countsPerSec = (int)((rpm * ENCODER_RES) / 60.0);
        return SetVelocity(countsPerSec);
    }

    public double GetActualVelocityRpm()
    {
        int countsPerSec = GetActualVelocity();
        // Chuyển counts/s sang RPM động cơ
        return (countsPerSec * 60.0) / ENCODER_RES;
    }
    public bool ConfigurePDO()
    {
        Console.WriteLine("\n=== Cấu hình PDO Mapping ===");

        // ----- RPDO1: ControlWord (0x6040,16bit) + TargetPosition (0x607A,32bit)
        canInterface.WriteSDO(0x1400, 1, (uint)(0x80000200 + nodeId), 4); // disable
        canInterface.WriteSDO(0x1600, 0, 0, 1); // clear
        canInterface.WriteSDO(0x1600, 1, 0x60400010, 4); // ControlWord
        canInterface.WriteSDO(0x1600, 2, 0x607A0020, 4); // TargetPosition
        canInterface.WriteSDO(0x1600, 0, 2, 1);
        canInterface.WriteSDO(0x1400, 1, (uint)(0x00000200 + nodeId), 4); // enable

        // ----- RPDO2: TargetVelocity (0x60FF,32bit) + ModeOfOperation (0x6060,8bit)
        canInterface.WriteSDO(0x1401, 1, (uint)(0x80000300 + nodeId), 4);
        canInterface.WriteSDO(0x1601, 0, 0, 1);
        canInterface.WriteSDO(0x1601, 1, 0x60FF0020, 4); // TargetVelocity
        canInterface.WriteSDO(0x1601, 2, 0x60600008, 4); // ModeOfOperation
        canInterface.WriteSDO(0x1601, 0, 2, 1);
        canInterface.WriteSDO(0x1401, 1, (uint)(0x00000300 + nodeId), 4);

        // ----- RPDO3: TargetTorque (0x6071,16bit)
        canInterface.WriteSDO(0x1402, 1, (uint)(0x80000400 + nodeId), 4);
        canInterface.WriteSDO(0x1602, 0, 0, 1);
        canInterface.WriteSDO(0x1602, 1, 0x60710010, 4); // TargetTorque
        canInterface.WriteSDO(0x1602, 0, 1, 1);
        canInterface.WriteSDO(0x1402, 1, (uint)(0x00000400 + nodeId), 4);

        // ----- TPDO1: StatusWord + PositionActual
        canInterface.WriteSDO(0x1800, 1, (uint)(0x80000180 + nodeId), 4);
        canInterface.WriteSDO(0x1A00, 0, 0, 1);
        canInterface.WriteSDO(0x1A00, 1, 0x60410010, 4);
        canInterface.WriteSDO(0x1A00, 2, 0x60640020, 4);
        canInterface.WriteSDO(0x1A00, 0, 2, 1);
        canInterface.WriteSDO(0x1800, 1, (uint)(0x00000180 + nodeId), 4);

        // ----- TPDO2: VelocityActual + ModesOfOperationDisplay
        canInterface.WriteSDO(0x1801, 1, (uint)(0x80000280 + nodeId), 4);
        canInterface.WriteSDO(0x1A01, 0, 0, 1);
        canInterface.WriteSDO(0x1A01, 1, 0x606C0020, 4);
        canInterface.WriteSDO(0x1A01, 2, 0x60610008, 4);
        canInterface.WriteSDO(0x1A01, 0, 2, 1);
        canInterface.WriteSDO(0x1801, 1, (uint)(0x00000280 + nodeId), 4);

        Console.WriteLine("PDO được cấu hình thành công!");
        Thread.Sleep(1000);
        usePDO = true;
        return true;
    }


    public void EnablePDOMode(bool enable)
    {
        usePDO = enable;
        Console.WriteLine($"PDO Mode: {(enable ? "Enabled" : "Disabled")}");
    }

    // public bool Initialize()
    // {
    //     Console.WriteLine("Khởi tạo motor...");
    //     UpdateState();
    //     if (currentState == CiA402State.Fault)
    //     {
    //         Console.WriteLine("Phát hiện lỗi, đang reset...");
    //         ResetFault();
    //         Thread.Sleep(500);
    //         UpdateState();
    //     }
    //     return EnableOperation();
    // }

    public bool Initialize()
    {
        Console.WriteLine("Khởi tạo motor...");
        UpdateState();
        if (currentState == CiA402State.Fault)
        {
            Console.WriteLine("Phát hiện lỗi, đang reset...");
            ResetFault();
            Thread.Sleep(500);
            UpdateState();
        }

        if (!EnableOperation())
            return false;

        Console.WriteLine("\n=== Cấu hình mặc định cho Profile Position ===");

        // 1. Đặt các tham số Profile Position mặc định (dùng RPM động cơ)
        double defaultVelRpm = 20;      // 100 RPM động cơ
        double defaultAccelRpm = 100;    // 500 RPM/s gia tốc
        double defaultDecelRpm = 100;    // 500 RPM/s giảm tốc

        uint defaultVelocity = (uint)((defaultVelRpm * ENCODER_RES) / 60.0);
        uint defaultAccel = (uint)((defaultAccelRpm * ENCODER_RES) / 60.0);
        uint defaultDecel = (uint)((defaultDecelRpm * ENCODER_RES) / 60.0);

        Console.WriteLine($"Profile Velocity: {defaultVelRpm} RPM → {defaultVelocity} counts/s");
        canInterface.WriteSDO(PROFILE_VELOCITY, 0, defaultVelocity, 4);
        Thread.Sleep(50);

        Console.WriteLine($"Profile Acceleration: {defaultAccelRpm} RPM/s → {defaultAccel} counts/s²");
        canInterface.WriteSDO(PROFILE_ACCELERATION, 0, defaultAccel, 4);
        Thread.Sleep(50);

        Console.WriteLine($"Profile Deceleration: {defaultDecelRpm} RPM/s → {defaultDecel} counts/s²");
        canInterface.WriteSDO(PROFILE_DECELERATION, 0, defaultDecel, 4);
        Thread.Sleep(50);

        // 2. Đảm bảo Position mode là Absolute (không phải Relative)
        uint posMode = canInterface.ReadSDO(0x607D, 0);
        Console.WriteLine($"Position Mode Config: 0x{posMode:X} (bit6=0:Absolute, bit6=1:Relative)");

        if ((posMode & 0x40) != 0)
        {
            Console.WriteLine("→ Chuyển sang Absolute Position Mode");
            canInterface.WriteSDO(0x607D, 0, posMode & ~0x40u, 2);
            Thread.Sleep(50);
        }
        else
        {
            Console.WriteLine("→ Đã ở Absolute Position Mode");
        }

        // 3. Tăng Max Velocity lên 3000 RPM động cơ
        uint currentMaxVel = canInterface.ReadSDO(0x607F, 0);
        double currentMaxRpm = (currentMaxVel * 60.0) / ENCODER_RES;
        Console.WriteLine($"Max Profile Velocity hiện tại: {currentMaxRpm:F2} RPM động cơ");

        if (currentMaxRpm < 1000) // Nếu nhỏ hơn 1000 RPM
        {
            double newMaxRpm = 3000;
            uint newMaxVel = (uint)((newMaxRpm * ENCODER_RES) / 60.0);
            Console.WriteLine($"→ Tăng Max Velocity lên: {newMaxRpm} RPM → {newMaxVel} counts/s");
            canInterface.WriteSDO(0x607F, 0, newMaxVel, 4);
            Thread.Sleep(50);
        }

        // 4. Kích hoạt Profile Position mode
        Console.WriteLine("Kích hoạt Profile Position mode...");
        canInterface.WriteSDO(0x6060, 0, (byte)OperationMode.ProfilePosition, 1);
        Thread.Sleep(100);

        // Đọc lại để xác nhận
        uint modeDisplay = canInterface.ReadSDO(0x6061, 0);
        Console.WriteLine($"Mode hiển thị: {modeDisplay} (1=Profile Position OK)");

        // 5. Gửi ControlWord để sẵn sàng
        canInterface.WriteSDO(CONTROL_WORD, 0, 0x0F, 2);
        Thread.Sleep(100);

        Console.WriteLine("✓ Khởi tạo hoàn tất! Motor sẵn sàng cho Position và Velocity mode.\n");

        return true;
    }

    private void UpdateState()
    {
        uint statusWord = usePDO ? canInterface.GetLatestTPDO1().StatusWord : canInterface.ReadSDO(STATUS_WORD, 0);
        currentState = DecodeState(statusWord);
        Console.WriteLine($"Trạng thái: {currentState} (0x{statusWord:X4})");
    }

    private CiA402State DecodeState(uint statusWord)
    {
        return (statusWord & 0x4F) switch
        {
            0x00 => CiA402State.NotReadyToSwitchOn,
            0x40 => CiA402State.SwitchOnDisabled,
            0x08 => CiA402State.Fault,
            0x0F => CiA402State.FaultReactionActive,
            _ => (statusWord & 0x6F) switch
            {
                0x21 => CiA402State.ReadyToSwitchOn,
                0x23 => CiA402State.SwitchedOn,
                0x27 => CiA402State.OperationEnabled,
                0x07 => CiA402State.QuickStopActive,
                _ => CiA402State.NotReadyToSwitchOn
            }
        };
    }

    /// <summary>
    /// Thực hiện Homing mode với method (1-35)
    /// </summary>
    public bool HomingMode(int method)
    {
        if (method < 1 || method > 35)
        {
            Console.WriteLine("Homing method phải nằm trong khoảng 1-35");
            return false;
        }

        Console.WriteLine($"=== Bắt đầu Homing (method {method}) ===");

        // Đặt mode về Homing
        if (!SetOperationMode(OperationMode.Homing)) return false;

        // Ghi Homing Method (0x6098)
        if (!canInterface.WriteSDO(0x6098, 0, (uint)(sbyte)method, 1))
        {
            Console.WriteLine("Không ghi được homing method");
            return false;
        }

        // Bật lệnh homing (bit 4 của controlword)
        canInterface.WriteSDO(CONTROL_WORD, 0, 0x001F, 2);
        Thread.Sleep(50);
        canInterface.WriteSDO(CONTROL_WORD, 0, 0x003F, 2);

        Console.WriteLine("Đang homing...");

        // Chờ homing complete (statusword bit 12)
        var start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < 30)
        {
            uint status = GetStatusWord();
            if ((status & 0x1000) != 0)
            {
                Console.WriteLine("Homing hoàn tất!");
                return true;
            }
            if ((status & 0x2000) != 0)
            {
                Console.WriteLine("Homing lỗi!");
                return false;
            }
            Thread.Sleep(200);
        }

        Console.WriteLine("Timeout homing!");
        return false;
    }

    /// <summary>
    /// Reset lỗi motor (Fault Reset)
    /// </summary>
    public bool ResetFault()
    {
        Console.WriteLine("Reset lỗi motor...");
        return canInterface.WriteSDO(CONTROL_WORD, 0, 0x80, 2);
    }

    /// <summary>
    /// Reset toàn bộ motor (fault reset + disable + enable lại)
    /// </summary>
    public bool ResetMotor()
    {
        Console.WriteLine("=== Reset Motor ===");
        ResetFault();
        Thread.Sleep(500);
        Disable();
        Thread.Sleep(200);
        return EnableOperation();
    }

    /// <summary>
    /// Stop motor: byVelocity = true thì stop theo velocity mode, false thì stop theo position mode
    /// </summary>

    public bool StopMotor()
    {
        SetVelocity(0);
        return canInterface.WriteSDO(CONTROL_WORD, 0, 0x010F, 2);

    }
    public bool EnableOperation()
    {
        Console.WriteLine("Bật hoạt động motor...");
        for (int i = 0; i < 10; i++)
        {
            UpdateState();
            switch (currentState)
            {
                case CiA402State.SwitchOnDisabled:
                    canInterface.WriteSDO(CONTROL_WORD, 0, 0x06, 2);
                    Thread.Sleep(200);
                    break;
                case CiA402State.ReadyToSwitchOn:
                    canInterface.WriteSDO(CONTROL_WORD, 0, 0x07, 2);
                    Thread.Sleep(200);
                    break;
                case CiA402State.SwitchedOn:
                    canInterface.WriteSDO(CONTROL_WORD, 0, 0x0F, 2);
                    Thread.Sleep(200);
                    break;
                case CiA402State.OperationEnabled:
                    Console.WriteLine("Motor sẵn sàng!");
                    return true;
                case CiA402State.Fault:
                    ResetFault();
                    Thread.Sleep(500);
                    break;
                default:
                    Thread.Sleep(200);
                    break;
            }
        }
        return false;
    }

    public bool SetOperationMode(OperationMode mode)
    {
        Console.WriteLine($"Đặt chế độ: {mode}");
        currentMode = mode;
        return canInterface.WriteSDO(0x6060, 0, (uint)(byte)(sbyte)mode, 1);
    }
    public bool MoveToPosition(int targetPosition, uint profileVelocity = 10000,
                                uint acceleration = 100000, uint deceleration = 100000)
    {
        Console.WriteLine($"Di chuyển tới: {targetPosition} (PDO: {usePDO})");

        // Set mode về Profile Position
        if (!SetOperationMode(OperationMode.ProfilePosition)) return false;

        // Dù chạy PDO thì Acc/Dec vẫn phải set qua SDO
        canInterface.WriteSDO(PROFILE_VELOCITY, 0, profileVelocity, 4);
        canInterface.WriteSDO(PROFILE_ACCELERATION, 0, acceleration, 4);
        canInterface.WriteSDO(PROFILE_DECELERATION, 0, deceleration, 4);

        if (usePDO)
        {
            // RPDO1 chứa ControlWord + TargetPosition
            canInterface.SendRPDO1(0x001F, targetPosition); // setpoint
            Thread.Sleep(10);
            canInterface.SendRPDO1(0x002F, targetPosition); // new setpoint
            Thread.Sleep(10);
            return canInterface.SendRPDO1(0x003F, targetPosition); // start move
        }
        else
        {
            // Nếu dùng SDO
            uint positionData = targetPosition < 0 ? (uint)((long)targetPosition + 0x100000000L) : (uint)targetPosition;
            canInterface.WriteSDO(TARGET_POSITION, 0, positionData, 4);
            canInterface.WriteSDO(CONTROL_WORD, 0, 0x001F, 2);
            Thread.Sleep(10);
            canInterface.WriteSDO(CONTROL_WORD, 0, 0x002F, 2);
            Thread.Sleep(10);
            return canInterface.WriteSDO(CONTROL_WORD, 0, 0x003F, 2);
        }
    }

    public bool WaitForPositionReached(double targetRad, double toleranceRad = 0.01, int timeoutMs = 30000)
    {
        Console.WriteLine($"Chờ vị trí {targetRad:F4} rad (tolerance: {toleranceRad} rad)");
        var startTime = DateTime.Now;

        while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
        {
            double currentRad = GetActualPositionRad();
            uint statusWord = GetStatusWord();

            // Kiểm tra bit "target reached" (bit 10 của StatusWord)
            if ((statusWord & 0x0400) != 0)
            {
                Console.WriteLine($"Đạt mục tiêu! Vị trí: {currentRad:F4} rad");
                return true;
            }

            // Kiểm tra dung sai rad
            if (Math.Abs(currentRad - targetRad) <= toleranceRad)
            {
                Console.WriteLine($"Trong dung sai! Hiện tại: {currentRad:F4} rad");
                return true;
            }

            // Nếu có lỗi
            if ((statusWord & 0x08) != 0)
            {
                Console.WriteLine("Lỗi trong quá trình di chuyển!");
                return false;
            }

            Console.WriteLine($"Đang di chuyển... {currentRad:F4} rad -> {targetRad:F4} rad");
            Thread.Sleep(200);
        }

        Console.WriteLine("Timeout khi chờ vị trí!");
        return false;
    }

    // public bool WaitForPositionReached(int targetPosition, int tolerance = 50, int timeoutMs = 30000)
    // {
    //     Console.WriteLine($"Chờ vị trí {targetPosition} (tolerance: {tolerance})");
    //     var startTime = DateTime.Now;
    //     while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
    //     {
    //         double currentPosition = GetActualPositionRad();
    //         uint statusWord = GetStatusWord();
    //         if ((statusWord & 0x0400) != 0)
    //         {
    //             Console.WriteLine($"Đạt mục tiêu! Vị trí: {currentPosition}");
    //             return true;
    //         }
    //         if (Math.Abs(currentPosition - targetPosition) <= tolerance)
    //         {
    //             Console.WriteLine($"Trong dung sai! Hiện tại: {currentPosition}");
    //             return true;
    //         }
    //         if ((statusWord & 0x08) != 0)
    //         {
    //             Console.WriteLine("Lỗi trong quá trình di chuyển!");
    //             return false;
    //         }
    //         Console.WriteLine($"Đang di chuyển... {currentPosition} -> {targetPosition}");
    //         Thread.Sleep(500);
    //     }
    //     Console.WriteLine("Timeout!");
    //     return false;
    // }

    public bool SetVelocity(int targetVelocity)
    {
        Console.WriteLine($"Đặt vận tốc: {targetVelocity} (PDO: {usePDO})");
        if (!SetOperationMode(OperationMode.CyclicSynchronousVelocity)) return false;
        if (usePDO)
            return canInterface.SendRPDO2(targetVelocity, (sbyte)OperationMode.CyclicSynchronousVelocity);
        else
        {
            uint velocityData = targetVelocity < 0 ? (uint)((long)targetVelocity + 0x100000000L) : (uint)targetVelocity;
            canInterface.WriteSDO(TARGET_VELOCITY, 0, velocityData, 4);
            return canInterface.WriteSDO(CONTROL_WORD, 0, 0x0F, 2);
        }
    }

    public bool SetTorque(short targetTorque)
    {
        Console.WriteLine($"Đặt torque: {targetTorque} (PDO: {usePDO})");
        if (!SetOperationMode(OperationMode.CyclicSynchronousTorque)) return false;
        if (usePDO)
            return canInterface.SendRPDO3(targetTorque);
        else
        {
            canInterface.WriteSDO(TARGET_TORQUE, 0, (uint)(ushort)targetTorque, 2);
            return canInterface.WriteSDO(CONTROL_WORD, 0, 0x0F, 2);
        }
    }

    public int GetActualPosition()
    {
        if (usePDO)
            return canInterface.GetLatestTPDO1().ActualPosition;
        else
        {
            uint rawValue = canInterface.ReadSDO(POSITION_ACTUAL, 0);
            return rawValue > 0x7FFFFFFF ? (int)(rawValue - 0x100000000L) : (int)rawValue;
        }
    }

    public int GetActualVelocity()
    {
        if (usePDO)
            return canInterface.GetLatestTPDO2().ActualVelocity;
        else
        {
            uint rawValue = canInterface.ReadSDO(VELOCITY_ACTUAL, 0);
            return rawValue > 0x7FFFFFFF ? (int)(rawValue - 0x100000000L) : (int)rawValue;
        }
    }
    public void ReadMotorLimits()
    {
        Console.WriteLine("\n=== Thông Số Giới Hạn Motor ===");

        // Max Motor Speed (0x6080)
        uint maxMotorSpeed = canInterface.ReadSDO(0x6080, 0);
        Console.WriteLine($"Max Motor Speed: {maxMotorSpeed} counts/s");

        // Max Profile Velocity (0x607F)
        uint maxProfileVel = canInterface.ReadSDO(0x607F, 0);
        Console.WriteLine($"Max Profile Velocity: {maxProfileVel} counts/s");

        // Chuyển sang RPM
        if (maxMotorSpeed > 0)
        {
            double maxRpm = (maxMotorSpeed * 60.0) / ENCODER_RES;
            Console.WriteLine($"  → Max Motor RPM: {maxRpm:F2} RPM");
            Console.WriteLine($"  → Max Output RPM: {(maxRpm / GEAR_RATIO):F2} RPM");
        }

        // Nominal Current (0x6075)
        uint nominalCurrent = canInterface.ReadSDO(0x6075, 0);
        Console.WriteLine($"Nominal Current: {nominalCurrent} mA");

        // Max Current (0x6073)
        uint maxCurrent = canInterface.ReadSDO(0x6073, 0);
        Console.WriteLine($"Max Current: {maxCurrent} mA");

        // Rated Torque (0x6076)
        uint ratedTorque = canInterface.ReadSDO(0x6076, 0);
        Console.WriteLine($"Rated Torque: {ratedTorque} mNm");
    }

    public short GetActualTorque()
    {
        if (usePDO)
            return canInterface.GetLatestTPDO1().ActualTorque;
        else
        {
            uint rawValue = canInterface.ReadSDO(TORQUE_ACTUAL, 0);
            return (short)(ushort)rawValue;
        }
    }

    public uint GetStatusWord()
    {
        return usePDO ? canInterface.GetLatestTPDO1().StatusWord : canInterface.ReadSDO(STATUS_WORD, 0);
    }

    public string GetStatusDescription()
    {
        uint sw = GetStatusWord();
        if ((sw & 0x08) != 0) return "Fault";
        if ((sw & 0x2000) != 0) return "Homing error";
        if ((sw & 0x1000) != 0) return "Homing attained";
        if ((sw & 0x04) != 0) return "Operation enabled";
        if ((sw & 0x02) != 0) return "Switched on";
        if ((sw & 0x01) != 0) return "Ready to switch on";
        if ((sw & 0x40) != 0) return "Switch on disabled";
        return "Unknown";
    }

    public uint ReadEncoderResolution()
    {
        Console.WriteLine("\n=== Đọc Encoder Resolution ===");
        uint resolution = canInterface.ReadSDO(0x2A0B, 0);
        Console.WriteLine($"Encoder Resolution: {resolution} counts/rev");
        return resolution;
    }

    public bool QuickStop()
    {
        Console.WriteLine("Dừng khẩn cấp motor...");
        return canInterface.WriteSDO(CONTROL_WORD, 0, 0x02, 2);
    }

    public bool Disable()
    {
        Console.WriteLine("Tắt motor...");
        return canInterface.WriteSDO(CONTROL_WORD, 0, 0x07, 2);
    }

}
