using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.Application.Services;

public sealed class GdprCleanupService : IGdprCleanupService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly GdprSettings _settings;
    private readonly ILogger<GdprCleanupService> _logger;

    public GdprCleanupService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        IOptions<GdprSettings> settings,
        ILogger<GdprCleanupService> logger)
    {
        _contextFactory = contextFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<GdprCleanupStats> CleanupPersonalDataAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("Starting GDPR Cleanup (DryRun: {DryRun})", _settings.DryRun);

        var stats = new GdprCleanupStats
        {
            StartTime = startTime,
            DryRun = _settings.DryRun
        };

        try
        {
            stats.PageViewsDeleted = await CleanupPageViewsAsync();
            _logger.LogInformation("  PageViews: {Count} deleted", stats.PageViewsDeleted);

            stats.ApiRequestsDeleted = await CleanupApiRequestsAsync();
            _logger.LogInformation("  ApiRequests: {Count} deleted", stats.ApiRequestsDeleted);

            stats.SessionActivitiesDeleted = await CleanupSessionActivitiesAsync();
            _logger.LogInformation("  SessionActivities: {Count} deleted", stats.SessionActivitiesDeleted);

            stats.UserActivitiesDeleted = await CleanupUserActivitiesAsync();
            _logger.LogInformation("  UserActivities: {Count} deleted", stats.UserActivitiesDeleted);

            stats.AuditLogsDeleted = await CleanupAuditLogsAsync();
            _logger.LogInformation("  AuditLogs: {Count} deleted", stats.AuditLogsDeleted);

            stats.SecurityEventsDeleted = await CleanupSecurityEventsAsync();
            _logger.LogInformation("  SecurityEvents: {Count} deleted", stats.SecurityEventsDeleted);

            stats.PerformanceMetricsDeleted = await CleanupPerformanceMetricsAsync();
            _logger.LogInformation("  PerformanceMetrics: {Count} deleted", stats.PerformanceMetricsDeleted);

            stats.KeyBackupHistoryDeleted = await CleanupKeyBackupHistoryAsync();
            _logger.LogInformation("  KeyBackupHistory: {Count} deleted", stats.KeyBackupHistoryDeleted);

            stats.EndTime = DateTime.UtcNow;
            stats.Duration = stats.EndTime.Value - startTime;
            stats.Success = true;

            var totalDeleted = stats.TotalDeleted;
            _logger.LogInformation("GDPR Cleanup completed: {Total} records deleted in {Duration}",
                totalDeleted, stats.Duration.Value.ToString(@"hh\:mm\:ss"));

            // Save stats to database
            await SaveCleanupStatsAsync(stats);
        }
        catch (Exception ex)
        {
            stats.Success = false;
            stats.ErrorMessage = ex.Message;
            stats.EndTime = DateTime.UtcNow;
            stats.Duration = stats.EndTime.Value - startTime;

            _logger.LogError(ex, "GDPR Cleanup failed: {Message}", ex.Message);

            throw;
        }

        return stats;
    }

    public async Task<int> CleanupPageViewsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var cutoffDate = DateTime.UtcNow.AddDays(-_settings.PageViewsRetentionDays);
        var totalDeleted = 0;

        if (_settings.DryRun)
        {
            return await context.PageViews
                .Where(pv => pv.Timestamp < cutoffDate)
                .CountAsync(cancellationToken);
        }

        // Batch delete for performance
        while (true)
        {
            var batch = await context.PageViews
                .Where(pv => pv.Timestamp < cutoffDate)
                .OrderBy(pv => pv.Timestamp)
                .Take(_settings.BatchSize)
                .ToListAsync(cancellationToken);

            if (!batch.Any())
                break;

            context.PageViews.RemoveRange(batch);
            await context.SaveChangesAsync(cancellationToken);
            totalDeleted += batch.Count;

            _logger.LogDebug("  PageViews batch deleted: {Count} (Total: {Total})",
                batch.Count, totalDeleted);
        }

        return totalDeleted;
    }

    public async Task<int> CleanupApiRequestsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var cutoffDate = DateTime.UtcNow.AddDays(-_settings.ApiRequestsRetentionDays);
        var totalDeleted = 0;

        if (_settings.DryRun)
        {
            return await context.ApiRequests
                .Where(r => r.Timestamp < cutoffDate)
                .CountAsync(cancellationToken);
        }

        while (true)
        {
            var batch = await context.ApiRequests
                .Where(r => r.Timestamp < cutoffDate)
                .OrderBy(r => r.Timestamp)
                .Take(_settings.BatchSize)
                .ToListAsync(cancellationToken);

            if (!batch.Any())
                break;

            context.ApiRequests.RemoveRange(batch);
            await context.SaveChangesAsync(cancellationToken);
            totalDeleted += batch.Count;

            _logger.LogDebug("  ApiRequests batch deleted: {Count} (Total: {Total})",
                batch.Count, totalDeleted);
        }

        return totalDeleted;
    }

    public async Task<int> CleanupSessionActivitiesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var cutoffDate = DateTime.UtcNow.AddDays(-_settings.SessionActivitiesRetentionDays);
        var totalDeleted = 0;

        if (_settings.DryRun)
        {
            return await context.SessionActivities
                .Where(sa => sa.Timestamp < cutoffDate)
                .CountAsync(cancellationToken);
        }

        while (true)
        {
            var batch = await context.SessionActivities
                .Where(sa => sa.Timestamp < cutoffDate)
                .OrderBy(sa => sa.Timestamp)
                .Take(_settings.BatchSize)
                .ToListAsync(cancellationToken);

            if (!batch.Any())
                break;

            context.SessionActivities.RemoveRange(batch);
            await context.SaveChangesAsync(cancellationToken);
            totalDeleted += batch.Count;

            _logger.LogDebug("  SessionActivities batch deleted: {Count} (Total: {Total})",
                batch.Count, totalDeleted);
        }

        return totalDeleted;
    }

    public async Task<int> CleanupUserActivitiesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var cutoffDate = DateTime.UtcNow.AddDays(-_settings.UserActivitiesRetentionDays);
        var totalDeleted = 0;

        if (_settings.DryRun)
        {
            return await context.UserActivities
                .Where(ua => ua.Timestamp < cutoffDate)
                .CountAsync(cancellationToken);
        }

        while (true)
        {
            var batch = await context.UserActivities
                .Where(ua => ua.Timestamp < cutoffDate)
                .OrderBy(ua => ua.Timestamp)
                .Take(_settings.BatchSize)
                .ToListAsync(cancellationToken);

            if (!batch.Any())
                break;

            context.UserActivities.RemoveRange(batch);
            await context.SaveChangesAsync(cancellationToken);
            totalDeleted += batch.Count;

            _logger.LogDebug("  UserActivities batch deleted: {Count} (Total: {Total})",
                batch.Count, totalDeleted);
        }

        return totalDeleted;
    }

    public async Task<int> CleanupAuditLogsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var cutoffDate = DateTime.UtcNow.AddDays(-_settings.AuditLogsRetentionDays);
        var totalDeleted = 0;

        if (_settings.DryRun)
        {
            return await context.AuditLogs
                .Where(al => al.Timestamp < cutoffDate)
                .CountAsync(cancellationToken);
        }

        while (true)
        {
            var batch = await context.AuditLogs
                .Where(al => al.Timestamp < cutoffDate)
                .OrderBy(al => al.Timestamp)
                .Take(_settings.BatchSize)
                .ToListAsync(cancellationToken);

            if (!batch.Any())
                break;

            context.AuditLogs.RemoveRange(batch);
            await context.SaveChangesAsync(cancellationToken);
            totalDeleted += batch.Count;

            _logger.LogDebug("  AuditLogs batch deleted: {Count} (Total: {Total})",
                batch.Count, totalDeleted);
        }

        return totalDeleted;
    }

    public async Task<int> CleanupSecurityEventsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var cutoffDate = DateTime.UtcNow.AddDays(-_settings.SecurityEventsRetentionDays);
        var totalDeleted = 0;

        if (_settings.DryRun)
        {
            return await context.SecurityEvents
                .Where(se => se.Timestamp < cutoffDate)
                .CountAsync(cancellationToken);
        }

        while (true)
        {
            var batch = await context.SecurityEvents
                .Where(se => se.Timestamp < cutoffDate)
                .OrderBy(se => se.Timestamp)
                .Take(_settings.BatchSize)
                .ToListAsync(cancellationToken);

            if (!batch.Any())
                break;

            context.SecurityEvents.RemoveRange(batch);
            await context.SaveChangesAsync(cancellationToken);
            totalDeleted += batch.Count;

            _logger.LogDebug("  SecurityEvents batch deleted: {Count} (Total: {Total})",
                batch.Count, totalDeleted);
        }

        return totalDeleted;
    }

    public async Task<int> CleanupPerformanceMetricsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var cutoffDate = DateTime.UtcNow.AddDays(-_settings.PerformanceMetricsRetentionDays);
        var totalDeleted = 0;

        if (_settings.DryRun)
        {
            return await context.PerformanceMetrics
                .Where(pm => pm.Timestamp < cutoffDate)
                .CountAsync(cancellationToken);
        }

        while (true)
        {
            var batch = await context.PerformanceMetrics
                .Where(pm => pm.Timestamp < cutoffDate)
                .OrderBy(pm => pm.Timestamp)
                .Take(_settings.BatchSize)
                .ToListAsync(cancellationToken);

            if (!batch.Any())
                break;

            context.PerformanceMetrics.RemoveRange(batch);
            await context.SaveChangesAsync(cancellationToken);
            totalDeleted += batch.Count;

            _logger.LogDebug("  PerformanceMetrics batch deleted: {Count} (Total: {Total})",
                batch.Count, totalDeleted);
        }

        return totalDeleted;
    }

    public async Task<int> CleanupKeyBackupHistoryAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var cutoffDate = DateTime.UtcNow.AddDays(-_settings.KeyBackupHistoryRetentionDays);
        var totalDeleted = 0;

        if (_settings.DryRun)
        {
            return await context.KeyBackupHistory
                .Where(kbh => kbh.BackupDate < cutoffDate)
                .CountAsync(cancellationToken);
        }

        while (true)
        {
            var batch = await context.KeyBackupHistory
                .Where(kbh => kbh.BackupDate < cutoffDate)
                .OrderBy(kbh => kbh.BackupDate)
                .Take(_settings.BatchSize)
                .ToListAsync(cancellationToken);

            if (!batch.Any())
                break;

            context.KeyBackupHistory.RemoveRange(batch);
            await context.SaveChangesAsync(cancellationToken);
            totalDeleted += batch.Count;

            _logger.LogDebug("  KeyBackupHistory batch deleted: {Count} (Total: {Total})",
                batch.Count, totalDeleted);
        }

        return totalDeleted;
    }

    public async Task<GdprCleanupStats> GetCleanupPreviewAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var pageViewsCutoff = DateTime.UtcNow.AddDays(-_settings.PageViewsRetentionDays);
        var apiRequestsCutoff = DateTime.UtcNow.AddDays(-_settings.ApiRequestsRetentionDays);
        var sessionActivitiesCutoff = DateTime.UtcNow.AddDays(-_settings.SessionActivitiesRetentionDays);
        var userActivitiesCutoff = DateTime.UtcNow.AddDays(-_settings.UserActivitiesRetentionDays);
        var auditLogsCutoff = DateTime.UtcNow.AddDays(-_settings.AuditLogsRetentionDays);
        var securityEventsCutoff = DateTime.UtcNow.AddDays(-_settings.SecurityEventsRetentionDays);
        var performanceMetricsCutoff = DateTime.UtcNow.AddDays(-_settings.PerformanceMetricsRetentionDays);
        var keyBackupHistoryCutoff = DateTime.UtcNow.AddDays(-_settings.KeyBackupHistoryRetentionDays);

        return new GdprCleanupStats
        {
            PageViewsDeleted = await context.PageViews.CountAsync(pv => pv.Timestamp < pageViewsCutoff, cancellationToken),
            ApiRequestsDeleted = await context.ApiRequests.CountAsync(r => r.Timestamp < apiRequestsCutoff, cancellationToken),
            SessionActivitiesDeleted = await context.SessionActivities.CountAsync(sa => sa.Timestamp < sessionActivitiesCutoff, cancellationToken),
            UserActivitiesDeleted = await context.UserActivities.CountAsync(ua => ua.Timestamp < userActivitiesCutoff, cancellationToken),
            AuditLogsDeleted = await context.AuditLogs.CountAsync(al => al.Timestamp < auditLogsCutoff, cancellationToken),
            SecurityEventsDeleted = await context.SecurityEvents.CountAsync(se => se.Timestamp < securityEventsCutoff, cancellationToken),
            PerformanceMetricsDeleted = await context.PerformanceMetrics.CountAsync(pm => pm.Timestamp < performanceMetricsCutoff, cancellationToken),
            KeyBackupHistoryDeleted = await context.KeyBackupHistory.CountAsync(kbh => kbh.BackupDate < keyBackupHistoryCutoff, cancellationToken),
            DryRun = true
        };
    }

    public async Task<GdprCleanupStats?> GetLastCleanupStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var lastCleanup = await context.GdprCleanupHistory
            .OrderByDescending(gch => gch.StartTime)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastCleanup == null)
            return null;

        return new GdprCleanupStats
        {
            PageViewsDeleted = lastCleanup.PageViewsDeleted,
            ApiRequestsDeleted = lastCleanup.ApiRequestsDeleted,
            SessionActivitiesDeleted = lastCleanup.SessionActivitiesDeleted,
            UserActivitiesDeleted = lastCleanup.UserActivitiesDeleted,
            AuditLogsDeleted = lastCleanup.AuditLogsDeleted,
            SecurityEventsDeleted = lastCleanup.SecurityEventsDeleted,
            PerformanceMetricsDeleted = lastCleanup.PerformanceMetricsDeleted,
            KeyBackupHistoryDeleted = lastCleanup.KeyBackupHistoryDeleted,
            StartTime = lastCleanup.StartTime,
            EndTime = lastCleanup.EndTime,
            Duration = lastCleanup.Duration,
            Success = lastCleanup.Success,
            ErrorMessage = lastCleanup.ErrorMessage,
            DryRun = lastCleanup.DryRun
        };
    }

    private async Task SaveCleanupStatsAsync(GdprCleanupStats stats, CancellationToken cancellationToken = default)
    {
        if (_settings.DryRun)
            return;

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var history = new Domain.Models.GdprCleanupHistory
            {
                PageViewsDeleted = stats.PageViewsDeleted,
                ApiRequestsDeleted = stats.ApiRequestsDeleted,
                SessionActivitiesDeleted = stats.SessionActivitiesDeleted,
                UserActivitiesDeleted = stats.UserActivitiesDeleted,
                AuditLogsDeleted = stats.AuditLogsDeleted,
                SecurityEventsDeleted = stats.SecurityEventsDeleted,
                PerformanceMetricsDeleted = stats.PerformanceMetricsDeleted,
                KeyBackupHistoryDeleted = stats.KeyBackupHistoryDeleted,
                StartTime = stats.StartTime,
                EndTime = stats.EndTime ?? DateTime.UtcNow,
                Duration = stats.Duration ?? TimeSpan.Zero,
                Success = stats.Success,
                ErrorMessage = stats.ErrorMessage,
                DryRun = stats.DryRun
            };

            context.GdprCleanupHistory.Add(history);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save GDPR cleanup stats");
        }
    }
}

/// <summary>
/// GDPR Cleanup Statistics
/// </summary>
public sealed class GdprCleanupStats
{
    public int PageViewsDeleted { get; set; }
    public int ApiRequestsDeleted { get; set; }
    public int SessionActivitiesDeleted { get; set; }
    public int UserActivitiesDeleted { get; set; }
    public int AuditLogsDeleted { get; set; }
    public int SecurityEventsDeleted { get; set; }
    public int PerformanceMetricsDeleted { get; set; }
    public int KeyBackupHistoryDeleted { get; set; }

    public int TotalDeleted =>
        PageViewsDeleted +
        ApiRequestsDeleted +
        SessionActivitiesDeleted +
        UserActivitiesDeleted +
        AuditLogsDeleted +
        SecurityEventsDeleted +
        PerformanceMetricsDeleted +
        KeyBackupHistoryDeleted;

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public bool DryRun { get; set; }
}
