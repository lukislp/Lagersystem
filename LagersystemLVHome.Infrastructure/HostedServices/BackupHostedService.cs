using LagersystemLVHome.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace LagersystemLVHome.Infrastructure.HostedServices;

/// <summary>
/// Hosted service for automatic daily backups.
/// Loads settings from the database and creates backups for all active providers.
/// </summary>
public class BackupHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackupHostedService> _logger;
    private DateTime? _lastBackupCheck;

    public BackupHostedService(
        IServiceProvider serviceProvider,
        ILogger<BackupHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Backup Hosted Service started");

        // Wait 30 seconds after start before first check
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndPerformBackupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Backup Hosted Service");
            }

            // Check every 15 minutes
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    private async Task CheckAndPerformBackupAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var backupService = scope.ServiceProvider.GetRequiredService<IBackupManagementService>();

        try
        {
            var settings = await backupService.GetSettingsAsync();

            if (!settings.Enabled)
            {
                _logger.LogDebug("Backup system is disabled");
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
                _logger.LogDebug("Backup already ran today");
                return;
            }

            if (timeDifference <= 15)
            {
                _logger.LogInformation("Backup time reached - starting automatic backup...");
                _logger.LogInformation("Scheduled time: {TargetTime}, current time: {Now}",
                    targetTime.ToString("HH:mm"), now.ToString("HH:mm"));

                var providers = await backupService.GetProvidersAsync();
                var activeProviders = providers.Where(p => p.Enabled).ToList();

                if (!activeProviders.Any())
                {
                    _logger.LogWarning("No active backup providers found");
                    return;
                }

                _logger.LogInformation("{Count} active providers found: {Providers}",
                    activeProviders.Count,
                    string.Join(", ", activeProviders.Select(p => p.Name)));

                var result = await backupService.CreateBackupAsync(ct);

                if (result.Success)
                {
                    _logger.LogInformation(
                        "Backup created successfully: {FileName} ({SizeMB} MB, {Duration}s)",
                        result.FileName,
                        result.FinalSizeBytes / 1024.0 / 1024.0,
                        result.Duration.TotalSeconds);

                    if (result.SuccessfulProviders.Any())
                    {
                        _logger.LogInformation("Upload successful to: {Providers}",
                            string.Join(", ", result.SuccessfulProviders));
                    }

                    if (result.FailedProviders.Any())
                    {
                        _logger.LogWarning("Upload failed to: {Providers}",
                            string.Join(", ", result.FailedProviders));
                    }

                    if (settings.VerifyBackups && result.ValidatedBackups > 0)
                    {
                        _logger.LogInformation("Validation: {Valid} successful, {Failed} failed",
                            result.ValidatedBackups, result.FailedValidations);
                    }

                    _logger.LogInformation("Starting backup cleanup (Retention: {Days} days)...",
                        settings.RetentionDays);

                    try
                    {
                        await backupService.CleanupOldBackupsAsync(settings.RetentionDays);
                        _logger.LogInformation("Cleanup completed");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Cleanup error");
                    }

                    _lastBackupCheck = now;
                }
                else
                {
                    _logger.LogError("Backup creation failed: {Error}", result.ErrorMessage);
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
                        "Next backup at {Time} (in {Hours}h {Minutes}m)",
                        targetTime.ToString("HH:mm"),
                        (int)timeUntilBackup.TotalHours,
                        timeUntilBackup.Minutes);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during backup check");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Backup Hosted Service stopped");
        return base.StopAsync(cancellationToken);
    }
}
