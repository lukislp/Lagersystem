using LagersystemLVHome.Application.Configuration;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace LagersystemLVHome.Application.Services;

public interface IRateLimitService
{
    Task<RateLimitResult> CheckRateLimitAsync(string identifier, string endpoint, string? role = null, bool isWebRequest = false, CancellationToken cancellationToken = default);
    Task ResetLimitAsync(string identifier, CancellationToken cancellationToken = default);
    Task<RateLimitStats> GetStatsAsync(string identifier, CancellationToken cancellationToken = default);

    // Dashboard support
    int GetActiveBucketsCount();
    List<(string Identifier, string Endpoint, int Remaining, TimeSpan ResetIn)> GetAllBuckets();

    // Request tracking
    List<RequestLog> GetRecentRequests(int count = 100);
    RateLimitStatistics GetGlobalStatistics();

    // Security detection methods
    BurstAttackDetection DetectBurstAttack(string identifier);
    BruteForceDetection DetectBruteForce(string identifier);
    DDoSDetection DetectDDoS(TimeSpan timeWindow);
    SlowRateAttackDetection DetectSlowRateAttack();

    // Failed auth attempt logging (for brute-force detection)
    void LogFailedAuthAttempt(string identifier, string endpoint);
}
