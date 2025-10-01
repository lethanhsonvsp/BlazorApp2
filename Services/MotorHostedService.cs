namespace BlazorApp2.Services
{
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using System.Diagnostics;

    public class MotorHostedService : BackgroundService
    {
        private readonly UbuntuCANInterface _canInterface;
        private readonly MotorManager _manager;
        private DateTime _lastReceived;

        public MotorHostedService(UbuntuCANInterface canInterface, MotorManager manager)
        {
            _canInterface = canInterface;
            _manager = manager;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_manager.IsConnected)
                {
                    if (IsCanUp("can0"))
                    {
                        try
                        {
                            if (_manager.Connect("can0", 500000, 1))
                            {
                                _manager.InitializeFromCan(_canInterface, 1);
                                _lastReceived = DateTime.UtcNow;
                                _manager.LogMessage("✅ Connected to CAN0");
                            }
                        }
                        catch (Exception ex)
                        {
                            _manager.LogMessage($"❌ Error when connecting CAN0: {ex.Message}");
                        }
                    }
                    else
                    {
                        _manager.LogMessage("⚠️ CAN0 interface is DOWN. Waiting...");
                    }
                }
                else
                {
                    var now = DateTime.UtcNow;
                    if ((now - _lastReceived).TotalSeconds > 2)
                    {
                        _manager.LogMessage("⚠️ CAN bus timeout! Disconnecting...");
                        _manager.Disconnect();
                    }
                }

                await Task.Delay(1000, stoppingToken);
            }
        }

        private bool IsCanUp(string ifName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = $"-c \"ip link show {ifName}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                return output.Contains("UP");
            }
            catch (Exception ex)
            {
                _manager.LogMessage($"❌ Error checking {ifName}: {ex.Message}");
                return false;
            }
        }

        public void NotifyDataReceived()
        {
            _lastReceived = DateTime.UtcNow;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _manager.LogMessage("🛑 Stopping motor and disconnecting CAN");
            try
            {
                _manager?.DisableMotor();
                _canInterface?.Disconnect();
            }
            catch (Exception ex)
            {
                _manager.LogMessage($"❌ Error while stopping motor: {ex.Message}");
            }
            return base.StopAsync(cancellationToken);
        }
    }
}
