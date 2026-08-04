using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.Application.Services;

public interface IGdprCleanupService
{
    /// <summary>
    /// Performs a full GDPR cleanup of personal data.
    /// </summary>
    Task<GdprCleanupStats> CleanupPersonalDataAsync(CancellationToken cancellationToken = default);

    Task<int> CleanupPageViewsAsync(CancellationToken cancellationToken = default);

    Task<int> CleanupApiRequestsAsync(CancellationToken cancellationToken = default);

    Task<int> CleanupSessionActivitiesAsync(CancellationToken cancellationToken = default);

    Task<int> CleanupUserActivitiesAsync(CancellationToken cancellationToken = default);

    Task<int> CleanupAuditLogsAsync(CancellationToken cancellationToken = default);

    Task<int> CleanupSecurityEventsAsync(CancellationToken cancellationToken = default);

    Task<int> CleanupPerformanceMetricsAsync(CancellationToken cancellationToken = default);

    Task<int> CleanupKeyBackupHistoryAsync(CancellationToken cancellationToken = default);

    Task<GdprCleanupStats> GetCleanupPreviewAsync(CancellationToken cancellationToken = default);

    Task<GdprCleanupStats?> GetLastCleanupStatsAsync(CancellationToken cancellationToken = default);
}
