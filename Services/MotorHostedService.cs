namespace BlazorApp2.Services
{
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;


    public class MotorHostedService : BackgroundService
    {
        private readonly ILogger<MotorHostedService> _logger;
        private readonly UbuntuCANInterface _canInterface;
        private readonly MotorManager _manager;


        public MotorHostedService(ILogger<MotorHostedService> logger, UbuntuCANInterface canInterface, MotorManager manager)
        {
            _logger = logger;
            _canInterface = canInterface;
            _manager = manager;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MotorHostedService starting");


            // Configure CAN - adjust values as needed
            var connected = _canInterface.Connect("can0", 500000, 1);
            if (!connected)
            {
                _logger.LogError("Cannot connect CAN interface");
                return;
            }


            // Initialize motor in manager
            var initOk = _manager.InitializeFromCan(_canInterface, 1);
            if (!initOk)
            {
                _logger.LogError("Motor initialization failed");
            }
            else
            {
                _logger.LogInformation("Motor initialized and ready");
            }


            // Keep running until stopped
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }


            _logger.LogInformation("MotorHostedService stopping");
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
