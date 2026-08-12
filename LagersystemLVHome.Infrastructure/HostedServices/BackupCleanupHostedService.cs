using LagersystemLVHome.Application.Services;

namespace LagersystemLVHome.Infrastructure.HostedServices;

/// <summary>
/// Automatic cleanup service for old backups.
/// Runs every 15 minutes and deletes old backups based on provider settings.
/// </summary>
public class BackupCleanupHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackupCleanupHostedService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(15);

    public BackupCleanupHostedService(
        IServiceProvider serviceProvider,
        ILogger<BackupCleanupHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Backup Cleanup Service started. Interval: {Minutes} minutes", _interval.TotalMinutes);

        // Wait 1 minute after start before first run
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Starting automatic backup cleanup...");

                using var scope = _serviceProvider.CreateScope();
                var backupService = scope.ServiceProvider.GetRequiredService<IBackupManagementService>();

                // Cleanup by MaxBackupsCount per provider (overrides retention)
                await CleanupByMaxBackupsCountAsync(backupService);

                _logger.LogInformation("Automatic backup cleanup completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automatic backup cleanup failed");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("Backup Cleanup Service stopped");
    }

    /// <summary>
    /// Cleanup based on MaxBackupsCount per provider.
    /// </summary>
    private async Task CleanupByMaxBackupsCountAsync(IBackupManagementService backupService, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking MaxBackupsCount for each provider...");

        var providers = await backupService.GetProvidersAsync();

        _logger.LogDebug("Found {Count} total providers", providers.Count);

        int totalDeleted = 0;

        foreach (var provider in providers.Where(p => p.Enabled))
        {
            try
            {
                _logger.LogDebug("Processing Provider: {Name}", provider.Name);

                var allBackups = await backupService.GetHistoryAsync(providerId: provider.Id, limit: 1000);

                int maxBackups = GetMaxBackupsCountFromProvider(provider);

                var successfulBackups = allBackups
                    .Where(b => b.Status == LagersystemLVHome.Domain.Models.BackupStatus.Success)
                    .OrderByDescending(b => b.BackupDate)
                    .ToList();

                var currentCount = successfulBackups.Count;
                var toDelete = currentCount - maxBackups;

                if (toDelete > 0)
                {
                    _logger.LogInformation("Provider {Provider}: {Current} backups, max {Max} -> Deleting {ToDelete} old backups",
                        provider.Name, currentCount, maxBackups, toDelete);

                    var oldBackups = successfulBackups
                        .Skip(maxBackups)
                        .Take(toDelete)
                        .ToList();

                    foreach (var backup in oldBackups)
                    {
                        await backupService.DeleteBackupAsync(backup.Id);
                        totalDeleted++;
                        _logger.LogInformation("Deleted: {FileName} from {Date}",
                            backup.FileName, backup.BackupDate.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
                    }
                }
                else
                {
                    _logger.LogInformation("Provider {Provider}: {Current} backups (max {Max}) - no cleanup needed",
                        provider.Name, currentCount, maxBackups);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up backups for provider {Provider}", provider.Name);
            }
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation("MaxBackupsCount cleanup complete: {Count} backups deleted", totalDeleted);
        }
        else
        {
            _logger.LogInformation("MaxBackupsCount cleanup complete: No backups needed deletion");
        }
    }

    /// <summary>
    /// Extracts MaxBackupsCount from the provider configuration JSON.
    /// </summary>
    private int GetMaxBackupsCountFromProvider(LagersystemLVHome.Domain.Models.BackupProvider provider)
    {
        const int DEFAULT_MAX_BACKUPS = 30;

        if (string.IsNullOrEmpty(provider.Configuration))
            return DEFAULT_MAX_BACKUPS;

        try
        {
            var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                provider.Configuration,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (config == null)
            {
                _logger.LogWarning("Configuration is null for provider {Provider}", provider.Name);
                return DEFAULT_MAX_BACKUPS;
            }

            var possibleKeys = new[]
            {
                "MaxBackupsCount", "maxBackupsCount", "maxbackupscount",
                "MaxBackups", "maxBackups", "maxbackups"
            };

            foreach (var key in possibleKeys)
            {
                if (config.TryGetValue(key, out var maxBackupsElement))
                {
                    if (maxBackupsElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        if (maxBackupsElement.TryGetInt32(out int maxBackups) && maxBackups > 0)
                        {
                            _logger.LogInformation("Found MaxBackups={Max} for provider {Provider} (key: {Key})",
                                maxBackups, provider.Name, key);
                            return maxBackups;
                        }
                    }
                    else if (maxBackupsElement.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        if (int.TryParse(maxBackupsElement.GetString(), out int maxBackups) && maxBackups > 0)
                        {
                            _logger.LogInformation("Found MaxBackups={Max} for provider {Provider} (key: {Key}, as string)",
                                maxBackups, provider.Name, key);
                            return maxBackups;
                        }
                    }
                }
            }

            _logger.LogWarning("MaxBackups/MaxBackupsCount not found for provider {Provider}. Available keys: {Keys}",
                provider.Name, string.Join(", ", config.Keys));

            return DEFAULT_MAX_BACKUPS;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing configuration for provider {Provider}", provider.Name);
            return DEFAULT_MAX_BACKUPS;
        }
    }
}
