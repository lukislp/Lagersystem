using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Read-only queries for the admin audit log page. Separates querying from
/// logging (<see cref="IAuditService"/>) in a CQRS-style split.
/// </summary>
public interface IAuditQueryService
{
    /// <summary>Active non-deleted users suitable for the user-filter dropdown.</summary>
    Task<IReadOnlyList<User>> GetActiveUsersForFilterAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent audit log entries matching the filter.</summary>
    Task<IReadOnlyList<AuditLog>> GetAuditLogsAsync(AuditLogFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Aggregated counts used by the statistics KPIs.</summary>
    Task<AuditLogStats> GetAuditLogStatsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Filter parameters for <see cref="IAuditQueryService.GetAuditLogsAsync"/>.
/// </summary>
/// <param name="UserId">Only return entries for this user id, or <c>null</c> to include all users.</param>
/// <param name="Action">Only return entries with this action name, or <c>null</c> to include all actions.</param>
/// <param name="Severity">Only return entries with this severity, or <c>null</c> to include all severities.</param>
/// <param name="TakeCount">Maximum number of entries to return (hard page size).</param>
public sealed record AuditLogFilter(
    int? UserId,
    string? Action,
    AuditSeverity? Severity,
    int TakeCount);

/// <summary>Aggregated KPIs used at the top of the audit log page.</summary>
public sealed record AuditLogStats(
    int Total,
    int InfoCount,
    int WarningCount,
    int ErrorAndCriticalCount);
