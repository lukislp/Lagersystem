namespace LagersystemLVHome.Application.Services;

public sealed class NotificationHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NotificationHostedService> _logger;
    private Timer? _hourlyTimer;
    private Timer? _dailyTimer;

    public NotificationHostedService(
        IServiceProvider serviceProvider,
        ILogger<NotificationHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification Background Service started");

        _hourlyTimer = new Timer(
            async _ => await ExecuteHourlyTasksAsync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromHours(1));

        _dailyTimer = new Timer(
            async _ => await ExecuteDailyTasksAsync(),
            null,
            GetTimeUntilNextRun(),
            TimeSpan.FromDays(1));

        await Task.CompletedTask;
    }

    private async Task ExecuteHourlyTasksAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var notificationService = scope.ServiceProvider
                .GetRequiredService<INotificationService>();
            var expiryService = scope.ServiceProvider
                .GetRequiredService<IExpiryService>();

            _logger.LogInformation("Starting low stock check...");
            await notificationService.CheckLowStockAndNotifyAsync();

            _logger.LogInformation("Starting expiry date check...");
            await expiryService.CheckExpiryAndNotifyAsync();

            await notificationService.DeleteOldNotificationsAsync(30);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in hourly notification tasks");
        }
    }

    private async Task ExecuteDailyTasksAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var notificationService = scope.ServiceProvider
                .GetRequiredService<INotificationService>();

            var now = DateTime.Now;
            if (now.Hour == 9)
            {
                _logger.LogInformation("Sending daily digest...");
                await notificationService.SendDailyDigestAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in daily notification tasks");
        }
    }

    private TimeSpan GetTimeUntilNextRun()
    {
        var now = DateTime.Now;
        var next9AM = DateTime.Today.AddHours(9);

        if (now >= next9AM)
        {
            next9AM = next9AM.AddDays(1);
        }

        return next9AM - now;
    }

    public override void Dispose()
    {
        _hourlyTimer?.Dispose();
        _dailyTimer?.Dispose();
        base.Dispose();
    }
}
