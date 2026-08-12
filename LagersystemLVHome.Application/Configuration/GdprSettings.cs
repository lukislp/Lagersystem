namespace LagersystemLVHome.Application.Configuration;

/// <summary>
/// GDPR compliance settings for automatic data cleanup.
/// </summary>
public class GdprSettings
{
    /// <summary>
    /// Enable automatic cleanup.
    /// </summary>
    public bool EnableAutoCleanup { get; set; } = true;

    /// <summary>
    /// Time for daily cleanup (format: "HH:mm").
    /// </summary>
    public string CleanupSchedule { get; set; } = "03:00";

    /// <summary>
    /// Batch size for delete operations (performance tuning).
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Retention period for PageViews (days). Default: 30.
    /// </summary>
    public int PageViewsRetentionDays { get; set; } = 30;

    /// <summary>
    /// Retention period for ApiRequests (days). Default: 30.
    /// </summary>
    public int ApiRequestsRetentionDays { get; set; } = 30;

    /// <summary>
    /// Retention period for SessionActivities (days). Default: 30.
    /// </summary>
    public int SessionActivitiesRetentionDays { get; set; } = 30;

    /// <summary>
    /// Retention period for UserActivities (days). Default: 30.
    /// </summary>
    public int UserActivitiesRetentionDays { get; set; } = 30;

    /// <summary>
    /// Retention period for AuditLogs (days). Default: 90 (legal requirement).
    /// </summary>
    public int AuditLogsRetentionDays { get; set; } = 90;

    /// <summary>
    /// Retention period for SecurityEvents (days). Default: 90 (security relevant).
    /// </summary>
    public int SecurityEventsRetentionDays { get; set; } = 90;

    /// <summary>
    /// Retention period for PerformanceMetrics (days). Default: 7.
    /// </summary>
    public int PerformanceMetricsRetentionDays { get; set; } = 7;

    /// <summary>
    /// Retention period for NotificationLog (days). Default: 30.
    /// </summary>
    public int NotificationLogRetentionDays { get; set; } = 30;

    /// <summary>
    /// Retention period for KeyBackupHistory (days). Default: 90.
    /// </summary>
    public int KeyBackupHistoryRetentionDays { get; set; } = 90;

    /// <summary>
    /// Dry-run mode (log only, no actual deletions).
    /// </summary>
    public bool DryRun { get; set; } = false;
}
