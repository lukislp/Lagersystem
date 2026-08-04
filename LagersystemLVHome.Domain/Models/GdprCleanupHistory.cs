using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// History of GDPR cleanup executions.
/// </summary>
public class GdprCleanupHistory
{
    [Key]
    public int Id { get; set; }

    public DateTime StartTime { get; set; }

    /// <summary>
    /// End time of the cleanup.
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Duration of the cleanup.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Whether the cleanup was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message (if failed).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Dry-run mode (no actual deletions).
    /// </summary>
    public bool DryRun { get; set; }

    // Deleted records per type
    public int PageViewsDeleted { get; set; }
    public int ApiRequestsDeleted { get; set; }
    public int SessionActivitiesDeleted { get; set; }
    public int UserActivitiesDeleted { get; set; }
    public int AuditLogsDeleted { get; set; }
    public int SecurityEventsDeleted { get; set; }
    public int PerformanceMetricsDeleted { get; set; }
    public int KeyBackupHistoryDeleted { get; set; }

    /// <summary>
    /// Total number of deleted records.
    /// </summary>
    public int TotalDeleted =>
        PageViewsDeleted +
        ApiRequestsDeleted +
        SessionActivitiesDeleted +
        UserActivitiesDeleted +
        AuditLogsDeleted +
        SecurityEventsDeleted +
        PerformanceMetricsDeleted +
        KeyBackupHistoryDeleted;
}
