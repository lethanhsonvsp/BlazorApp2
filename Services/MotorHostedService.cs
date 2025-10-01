namespace BlazorApp2.Services
{
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;


    public class MotorHostedService : BackgroundService
    {
        private readonly ILogger<MotorHostedService> _logger;
        private readonly UbuntuCANInterface _canInterface;
        private readonly MotorManager _manager;
        private bool _isReconnecting = false;
        private DateTime _lastReceived;


        public MotorHostedService(ILogger<MotorHostedService> logger, UbuntuCANInterface canInterface, MotorManager manager)
        {
            _logger = logger;
            _canInterface = canInterface;
            _manager = manager;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Nếu chưa connect, thử connect
                if (!_manager.IsConnected && !_isReconnecting)
                {
                    TryReconnect();
                }

                // Nếu đang connected → kiểm tra timeout
                if (_manager.IsConnected)
                {
                    var now = DateTime.UtcNow;
                    if ((now - _lastReceived).TotalSeconds > 2) // 2 giây không có data
                    {
                        _logger.LogWarning("CAN bus timeout! Disconnecting...");
                        _manager.Disconnect();
                        _isReconnecting = false; // cho phép thử reconnect vòng sau
                    }
                }

                await Task.Delay(500, stoppingToken);
            }
        }
        private void TryReconnect()
        {
            _isReconnecting = true;
            _logger.LogInformation("🔄 Trying to reconnect CAN...");
            try
            {
                if (_manager.Connect("can0", 500000, 1))
                {
                    _manager.InitializeFromCan(_canInterface, 1);
                    _lastReceived = DateTime.UtcNow;
                    _logger.LogInformation("✅ Reconnected to CAN");
                }
                else
                {
                    _logger.LogWarning("Reconnect failed, retry later...");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconnect error");
            }
            finally
            {
                _isReconnecting = false;
            }
        }
        public void NotifyDataReceived()
        {
            _lastReceived = DateTime.UtcNow;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping motor and disconnecting CAN");
            try
            {
                _manager?.DisableMotor();
                _canInterface?.Disconnect();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping motor");
            }
            return base.StopAsync(cancellationToken);
        }
    }
}
