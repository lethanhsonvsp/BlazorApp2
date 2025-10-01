namespace BlazorApp2
{
    public class CiA402Motor(UbuntuCANInterface canInterface, byte nodeId)
    {
        private readonly UbuntuCANInterface canInterface = canInterface;
        private readonly byte nodeId = nodeId;
        private CiA402State currentState = CiA402State.NotReadyToSwitchOn;
        private OperationMode currentMode;

        // CiA 402 Object Dictionary Indices
        private const ushort CONTROL_WORD = 0x6040;
        private const ushort STATUS_WORD = 0x6041;
        private const ushort MODES_OF_OPERATION = 0x6060;
        private const ushort MODES_OF_OPERATION_DISPLAY = 0x6061;
        private const ushort TARGET_POSITION = 0x607A;
        private const ushort POSITION_ACTUAL = 0x6064;
        private const ushort TARGET_VELOCITY = 0x60FF;
        private const ushort VELOCITY_ACTUAL = 0x606C;
        private const ushort TARGET_TORQUE = 0x6071;
        private const ushort TORQUE_ACTUAL = 0x6077;
        private const ushort HOMING_METHOD = 0x6098;
        private const ushort PROFILE_VELOCITY = 0x6081;
        private const ushort PROFILE_ACCELERATION = 0x6083;
        private const ushort PROFILE_DECELERATION = 0x6084;

        public bool Initialize()
        {
            Console.WriteLine("Khoi tao motor...");

            // Đọc trạng thái hiện tại
            UpdateState();

            // Reset lỗi nếu có
            if (currentState == CiA402State.Fault)
            {
                Console.WriteLine("Phat hien loi, dang reset...");
                ResetFault();
                Thread.Sleep(500);
                UpdateState();
            }

            // Chuyển về trạng thái Operation Enabled
            return EnableOperation();
        }

        private void UpdateState()
        {
            uint statusWord = canInterface.ReadSDO(STATUS_WORD, 0);
            currentState = DecodeState(statusWord);
            Console.WriteLine($"Trang thai hien tai: {currentState} (Status Word: 0x{statusWord:X4})");
        }

        private static CiA402State DecodeState(uint statusWord)
        {
            // Decode state theo CiA 402 specification
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

        public bool ResetFault()
        {
            Console.WriteLine("Dang reset Loi...");
            return canInterface.WriteSDO(CONTROL_WORD, 0, 0x80, 2);
        }

        public bool EnableOperation()
        {
            Console.WriteLine("Bat motor...");
            int maxAttempts = 10;
            int attempt = 0;

            while (attempt < maxAttempts)
            {
                UpdateState();

                switch (currentState)
                {
                    case CiA402State.NotReadyToSwitchOn:
                        Console.WriteLine("Chờ ready to switch on...");
                        Thread.Sleep(200);
                        break;
                    case CiA402State.SwitchOnDisabled:
                        Console.WriteLine("Gửi lệnh shutdown...");
                        canInterface.WriteSDO(CONTROL_WORD, 0, 0x06, 2);
                        Thread.Sleep(200);
                        break;
                    case CiA402State.ReadyToSwitchOn:
                        Console.WriteLine("Gửi lệnh switch on...");
                        canInterface.WriteSDO(CONTROL_WORD, 0, 0x07, 2);
                        Thread.Sleep(200);
                        break;
                    case CiA402State.SwitchedOn:
                        Console.WriteLine("Gửi lệnh enable operation...");
                        canInterface.WriteSDO(CONTROL_WORD, 0, 0x0F, 2);
                        Thread.Sleep(200);
                        break;
                    case CiA402State.OperationEnabled:
                        Console.WriteLine("Motor đã sẵn sàng hoạt động!");
                        return true;
                    case CiA402State.Fault:
                        Console.WriteLine("Trạng thái lỗi, đang reset...");
                        ResetFault();
                        Thread.Sleep(500);
                        break;
                    default:
                        Console.WriteLine($"Trạng thái không mong đợi: {currentState}");
                        break;
                }

                attempt++;
            }

            Console.WriteLine("Không thể bật operation sau số lần thử tối đa");
            return false;
        }

        public bool SetOperationMode(OperationMode mode)
        {
            Console.WriteLine($"Đặt chế độ hoạt động: {mode}");
            currentMode = mode;
            bool result = canInterface.WriteSDO(MODES_OF_OPERATION, 0, (byte)(sbyte)mode, 1);

            if (result)
            {
                Thread.Sleep(100);
                uint displayMode = canInterface.ReadSDO(MODES_OF_OPERATION_DISPLAY, 0);
                Console.WriteLine($"Chế độ hiển thị: {(sbyte)(byte)displayMode}");
            }

            return result;
        }

        public bool StartHoming(byte homingMethod = 1)
        {
            Console.WriteLine($"Bắt đầu homing với phương pháp: {homingMethod}");

            if (!SetOperationMode(OperationMode.Homing))
                return false;

            if (!canInterface.WriteSDO(HOMING_METHOD, 0, homingMethod, 1))
                return false;

            return canInterface.WriteSDO(CONTROL_WORD, 0, 0x1F, 2);
        }

        public bool WaitForHomingComplete(int timeoutMs = 30000)
        {
            Console.WriteLine("Chờ homing hoàn thành...");

            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                uint statusWord = canInterface.ReadSDO(STATUS_WORD, 0);

                if ((statusWord & 0x1000) != 0)
                {
                    Console.WriteLine("Homing hoàn thành thành công!");
                    return true;
                }

                if ((statusWord & 0x2000) != 0)
                {
                    Console.WriteLine("Lỗi homing!");
                    return false;
                }

                if ((statusWord & 0x08) != 0)
                {
                    Console.WriteLine("Lỗi trong quá trình homing!");
                    return false;
                }

                Console.WriteLine($"Homing đang tiến hành... (Status: 0x{statusWord:X4})");
                Thread.Sleep(1000);
            }

            Console.WriteLine("Homing timeout!");
            return false;
        }

        public bool MoveToPosition(int targetPosition, uint profileVelocity = 10000,
                                    uint acceleration = 1000000, uint deceleration = 1000000)
        {
            Console.WriteLine($"Di chuyển tới vị trí: {targetPosition}");

            if (!SetOperationMode(OperationMode.ProfilePosition))
                return false;

            // Cấu hình profile
            canInterface.WriteSDO(PROFILE_VELOCITY, 0, profileVelocity, 4);
            canInterface.WriteSDO(PROFILE_ACCELERATION, 0, acceleration, 4);
            canInterface.WriteSDO(PROFILE_DECELERATION, 0, deceleration, 4);

            // Xử lý target âm
            uint positionData = targetPosition < 0
                ? (uint)(targetPosition + 0x100000000L)
                : (uint)targetPosition;

            // Ghi target position
            canInterface.WriteSDO(TARGET_POSITION, 0, positionData, 4);

            // ===== Toggle new set-point =====
            // Clear new set-point
            canInterface.WriteSDO(CONTROL_WORD, 0, 0x001F, 2);
            Thread.Sleep(10);

            // Set new set-point
            canInterface.WriteSDO(CONTROL_WORD, 0, 0x002F, 2);
            Thread.Sleep(10);

            // Immediate execute (nếu cần)
            return canInterface.WriteSDO(CONTROL_WORD, 0, 0x003F, 2);
        }


        public bool WaitForPositionReached(int targetPosition, int tolerance = 50, int timeoutMs = 30000)
        {
            Console.WriteLine($"Chờ vị trí {targetPosition} được đạt tới (dung sai: {tolerance})");

            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                uint statusWord = canInterface.ReadSDO(STATUS_WORD, 0);
                int currentPosition = GetActualPosition();

                if ((statusWord & 0x0400) != 0)
                {
                    Console.WriteLine($"Đạt mục tiêu! Vị trí hiện tại: {currentPosition}");
                    return true;
                }

                if (Math.Abs(currentPosition - targetPosition) <= tolerance)
                {
                    Console.WriteLine($"Vị trí trong dung sai! Hiện tại: {currentPosition}, Mục tiêu: {targetPosition}");
                    return true;
                }

                if ((statusWord & 0x08) != 0)
                {
                    Console.WriteLine("Lỗi trong quá trình di chuyển!");
                    return false;
                }

                Console.WriteLine($"Đang di chuyển... Hiện tại: {currentPosition}, Mục tiêu: {targetPosition}");
                Thread.Sleep(500);
            }

            Console.WriteLine("Timeout di chuyển vị trí!");
            return false;
        }

        public bool SetVelocity(int targetVelocity)
        {
            Console.WriteLine($"Đặt vận tốc: {targetVelocity}");

            if (!SetOperationMode(OperationMode.CyclicSynchronousVelocity))
                return false;

            // Xử lý vận tốc âm đúng cách
            uint velocityData;
            if (targetVelocity < 0)
            {
                velocityData = (uint)(targetVelocity + 0x100000000L);
            }
            else
            {
                velocityData = (uint)targetVelocity;
            }

            canInterface.WriteSDO(TARGET_VELOCITY, 0, velocityData, 4);
            return canInterface.WriteSDO(CONTROL_WORD, 0, 0x0F, 2);
        }

        public bool SetTorque(short targetTorque)
        {
            Console.WriteLine($"Đặt mô-men xoắn: {targetTorque}");

            if (!SetOperationMode(OperationMode.CyclicSynchronousTorque))
                return false;

            canInterface.WriteSDO(TARGET_TORQUE, 0, (ushort)targetTorque, 2);
            return canInterface.WriteSDO(CONTROL_WORD, 0, 0x0F, 2);
        }

        public int GetActualPosition()
        {
            uint rawValue = canInterface.ReadSDO(POSITION_ACTUAL, 0);
            // Chuyển đổi từ unsigned sang signed
            if (rawValue > 0x7FFFFFFF)
            {
                return (int)(rawValue - 0x100000000L);
            }
            return (int)rawValue;
        }

        public int GetActualVelocity()
        {
            uint rawValue = canInterface.ReadSDO(VELOCITY_ACTUAL, 0);
            // Chuyển đổi từ unsigned sang signed
            if (rawValue > 0x7FFFFFFF)
            {
                return (int)(rawValue - 0x100000000L);
            }
            return (int)rawValue;
        }

        public short GetActualTorque()
        {
            uint rawValue = canInterface.ReadSDO(TORQUE_ACTUAL, 0);
            return (short)(ushort)rawValue;
        }

        public uint GetStatusWord()
        {
            return canInterface.ReadSDO(STATUS_WORD, 0);
        }

        public bool Stop()
        {
            Console.WriteLine("Dừng motor...");
            return canInterface.WriteSDO(CONTROL_WORD, 0, 0x02, 2);
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

}
