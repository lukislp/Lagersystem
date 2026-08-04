using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Application.Configuration;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Database health monitoring service (SuperAdmin only).
/// </summary>
public interface IDatabaseHealthService
{
    Task<DatabaseHealthReport> GetHealthReportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a connection test.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<List<TableStatistics>> GetTableStatisticsAsync(CancellationToken cancellationToken = default);

    Task<List<IndexStatistics>> GetIndexStatisticsAsync(CancellationToken cancellationToken = default);

    Task<List<SlowQueryInfo>> GetSlowQueriesAsync(int count = 10, CancellationToken cancellationToken = default);
}
