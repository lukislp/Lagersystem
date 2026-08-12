using LagersystemLVHome.Application.Services;

namespace LagersystemLVHome.Infrastructure.HostedServices;

/// <summary>
/// Automatic daily backup of all provider keys.
/// Loads settings from the database.
/// </summary>
public class KeyBackupHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KeyBackupHostedService> _logger;
    private DateTime? _lastBackupCheck;

    public KeyBackupHostedService(
        IServiceProvider serviceProvider,
        ILogger<KeyBackupHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Key Backup Service started");

        // Wait 30 seconds after app start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndPerformKeyBackupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Key Backup Service");
            }

            // Check every 15 minutes
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    private async Task CheckAndPerformKeyBackupAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var keyBackupService = scope.ServiceProvider.GetRequiredService<IKeyBackupService>();

        try
        {
            var settings = await keyBackupService.GetSettingsAsync();

            if (!settings.Enabled)
            {
                _logger.LogDebug("Key backup system is disabled");
                return;
            }

            var now = DateTime.Now;
            var targetTime = new DateTime(now.Year, now.Month, now.Day, settings.BackupHour, 0, 0);

            // If today's backup time has passed, target tomorrow
            if (now >= targetTime.AddMinutes(15))
            {
                targetTime = targetTime.AddDays(1);
            }

            var timeDifference = Math.Abs((now - targetTime).TotalMinutes);

            // Prevent multiple executions on the same day
            if (_lastBackupCheck.HasValue &&
                _lastBackupCheck.Value.Date == now.Date &&
                timeDifference <= 15)
            {
                _logger.LogDebug("Key backup already ran today");
                return;
            }

            if (timeDifference <= 15)
            {
                _logger.LogInformation("Key backup time reached - starting automatic backup...");
                _logger.LogInformation("Scheduled time: {TargetTime}, current time: {Now}",
                    targetTime.ToString("HH:mm"), now.ToString("HH:mm"));

                var result = await keyBackupService.CreateKeyBackupAsync();

                if (result.Success)
                {
                    _logger.LogInformation("Key backup successful: {FileName}", result.FileName);
                    _logger.LogInformation("Old key backups will be cleaned up automatically on next backup");
                    _lastBackupCheck = now;
                }
                else
                {
                    _logger.LogError("Key backup failed: {Error}", result.ErrorMessage);
                }
            }
            else
            {
                var timeUntilBackup = targetTime - now;

                // Only log every 4 hours (not every 15 minutes)
                if (!_lastBackupCheck.HasValue ||
                    (now - _lastBackupCheck.Value).TotalHours >= 4)
                {
                    _logger.LogDebug(
                        "Next key backup at {Time} (in {Hours}h {Minutes}m)",
                        targetTime.ToString("HH:mm"),
                        (int)timeUntilBackup.TotalHours,
                        timeUntilBackup.Minutes);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during key backup check");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Key Backup Service stopped");
        return base.StopAsync(cancellationToken);
    }
}
