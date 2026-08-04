using Microsoft.Extensions.Options;
using LagersystemLVHome.Application.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Cloudflare API service for Free Plan integration.
/// </summary>
public interface ICloudflareService
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    // Analytics
    Task<CloudflareAnalytics?> GetAnalyticsAsync(int days = 1, CancellationToken cancellationToken = default);
    Task<CloudflareDashboardData?> GetDashboardDataAsync(CancellationToken cancellationToken = default);

    // Security
    Task<bool> UpdateSecurityLevelAsync(CloudflareSecurityLevel level, CancellationToken cancellationToken = default);
    Task<SecurityLevelInfo?> GetCurrentSecurityLevelAsync(CancellationToken cancellationToken = default);

    // Performance
    Task<bool> PurgeCacheAsync(List<string>? urls = null, CancellationToken cancellationToken = default);
    Task<bool> SetDevelopmentModeAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<CacheStats?> GetCacheStatsAsync(CancellationToken cancellationToken = default);

    // Auto-Escalation
    Task<bool> EscalateToUnderAttackAsync(CancellationToken cancellationToken = default);
    Task<bool> DeEscalateFromUnderAttackAsync(CancellationToken cancellationToken = default);
    Task<EscalationStatus> GetEscalationStatusAsync(CancellationToken cancellationToken = default);

    // SSL/TLS
    Task<SslTlsInfo?> GetSslTlsInfoAsync(CancellationToken cancellationToken = default);
    Task<bool> UpdateSslModeAsync(string mode, CancellationToken cancellationToken = default);

    // Headers
    Task<Dictionary<string, string>> GetRequestHeadersAsync(HttpContext context, CancellationToken cancellationToken = default);

    // Zone Info
    Task<ZoneInfo?> GetZoneInfoAsync(CancellationToken cancellationToken = default);
}
