using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Application.Services;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.Infrastructure.HostedServices;

/// <summary>
/// Background service for automatic GDPR-compliant data cleanup.
/// Runs daily at the configured time (default: 03:00).
/// </summary>
public class GdprCleanupHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly GdprSettings _settings;
    private readonly ILogger<GdprCleanupHostedService> _logger;

    public GdprCleanupHostedService(
        IServiceProvider serviceProvider,
        IOptions<GdprSettings> settings,
        ILogger<GdprCleanupHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.EnableAutoCleanup)
        {
            _logger.LogInformation("GDPR Auto-Cleanup is disabled");
            return;
        }

        _logger.LogInformation("GDPR Cleanup Service started - Schedule: {Schedule} (DryRun: {DryRun})",
            _settings.CleanupSchedule, _settings.DryRun);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = CalculateDelayUntilNextRun();
                _logger.LogInformation("Next GDPR Cleanup: {NextRun} (in {Delay})",
                    DateTime.Now.Add(delay).ToString("yyyy-MM-dd HH:mm:ss"),
                    FormatTimeSpan(delay));

                await Task.Delay(delay, stoppingToken);

                using var scope = _serviceProvider.CreateScope();
                var cleanupService = scope.ServiceProvider.GetRequiredService<IGdprCleanupService>();

                var stats = await cleanupService.CleanupPersonalDataAsync();

                if (stats.Success)
                {
                    _logger.LogInformation("GDPR Cleanup successful: {Total} records deleted in {Duration}",
                        stats.TotalDeleted, stats.Duration?.ToString(@"hh\:mm\:ss"));
                }
                else
                {
                    _logger.LogError("GDPR Cleanup failed: {Error}", stats.ErrorMessage);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("GDPR Cleanup Service stopping...");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GDPR Cleanup failed with exception");

                // Wait 1 hour before retrying on error
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    /// <summary>
    /// Calculates delay until the next scheduled run.
    /// </summary>
    private TimeSpan CalculateDelayUntilNextRun()
    {
        try
        {
            var scheduleParts = _settings.CleanupSchedule.Split(':');
            if (scheduleParts.Length != 2 ||
                !int.TryParse(scheduleParts[0], out var hour) ||
                !int.TryParse(scheduleParts[1], out var minute))
            {
                _logger.LogWarning("Invalid CleanupSchedule format: {Schedule}, using default 03:00",
                    _settings.CleanupSchedule);
                hour = 3;
                minute = 0;
            }

            var now = DateTime.Now;
            var scheduledTime = now.Date.AddHours(hour).AddMinutes(minute);

            if (scheduledTime < now)
            {
                scheduledTime = scheduledTime.AddDays(1);
            }

            return scheduledTime - now;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating next run time, using default 24 hours");
            return TimeSpan.FromHours(24);
        }
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
            return $"{(int)timeSpan.TotalDays}d {timeSpan.Hours}h {timeSpan.Minutes}m";
        if (timeSpan.TotalHours >= 1)
            return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
        if (timeSpan.TotalMinutes >= 1)
            return $"{(int)timeSpan.TotalMinutes}m {timeSpan.Seconds}s";
        return $"{(int)timeSpan.TotalSeconds}s";
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GDPR Cleanup Service stopped");
        await base.StopAsync(cancellationToken);
    }
}
