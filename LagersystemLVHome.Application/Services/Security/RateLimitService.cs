using LagersystemLVHome.Application.Configuration;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace LagersystemLVHome.Application.Services;

public sealed class BurstAttackDetection
{
    public bool IsBurstAttack { get; set; }
    public int RequestsInBurst { get; set; }
    public TimeSpan BurstDuration { get; set; }
    public double RequestsPerSecond { get; set; }
    public string Identifier { get; set; } = "";
}

public sealed class BruteForceDetection
{
    public bool IsBruteForce { get; set; }
    public int FailedAttempts { get; set; }
    public List<string> TargetedEndpoints { get; set; } = new();
    public TimeSpan AttackDuration { get; set; }
    public string Identifier { get; set; } = "";
}

public sealed class DDoSDetection
{
    public bool IsDDoSPattern { get; set; }
    public int UniqueIPsInvolved { get; set; }
    public int TotalRequests { get; set; }
    public double AverageRequestsPerIP { get; set; }
    public List<string> SuspiciousIPs { get; set; } = new();
}

public sealed class SlowRateAttackDetection
{
    public bool IsSlowRateAttack { get; set; }
    public int SuspiciousPatternCount { get; set; }
    public List<string> ConsistentOffenders { get; set; } = new();
}

public sealed class RateLimitService : IRateLimitService, IDisposable
{
    private readonly RateLimitSettings _settings;
    private readonly ILogger<RateLimitService> _logger;
    private readonly ConcurrentDictionary<string, RateLimitBucket> _buckets = new();
    private readonly IGeoLocationService? _geoLocationService;
    private readonly RateLimitConnectionPool _connectionPool;

    // In-memory request log (last 2000 requests)
    private readonly ConcurrentQueue<RequestLog> _requestLog = new();
    private const int MaxLogSize = 2000;

    // Global statistics
    private int _totalRequests = 0;
    private int _blockedRequests = 0;

    // Bucket cleanup timer
    private readonly System.Threading.Timer? _cleanupTimer;
    private const int CleanupIntervalMinutes = 5;
    private const int BucketInactivityMinutes = 10;

    public RateLimitService(
        IOptions<RateLimitSettings> settings,
        ILogger<RateLimitService> logger,
        IGeoLocationService? geoLocationService = null)
    {
        _settings = settings.Value;
        _logger = logger;
        _geoLocationService = geoLocationService;

        // Initialize connection pool (max 50 concurrent requests)
        _connectionPool = new RateLimitConnectionPool(50,
            logger as ILogger<RateLimitConnectionPool> ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RateLimitConnectionPool>.Instance);

        // Start cleanup timer (every 5 minutes)
        _cleanupTimer = new System.Threading.Timer(
            CleanupInactiveBuckets,
            null,
            TimeSpan.FromMinutes(CleanupIntervalMinutes),
            TimeSpan.FromMinutes(CleanupIntervalMinutes)
        );

        _logger.LogInformation("RateLimitService initialized with connection pool (max 50 concurrent)");
    }

    public async Task<RateLimitResult> CheckRateLimitAsync(
        string identifier, string endpoint, string? role = null, bool isWebRequest = false, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return RateLimitResult.CreateSuccess();
        }

        // Check whitelist
        if (_settings.WhitelistedIPs.Contains(identifier))
        {
            return RateLimitResult.CreateSuccess();
        }

        // Check blacklist
        if (_settings.BlacklistedIPs.Contains(identifier))
        {
            _logger.LogWarning("Blacklisted IP attempted access: {IP} -> {Endpoint}", identifier, endpoint);
            return RateLimitResult.CreateBlocked("IP is blacklisted");
        }

        // Connection pool with priority for web requests
        try
        {
            using var lease = await _connectionPool.AcquireAsync(TimeSpan.FromSeconds(2), isWebRequest);

            var policy = GetPolicyForRequest(endpoint, role);
            var key = $"{identifier}:{endpoint}";

            var bucket = _buckets.GetOrAdd(key, _ => new RateLimitBucket(policy));

            var result = await bucket.TryConsumeAsync();

            LogRequest(identifier, endpoint, result.IsSuccess, policy.PermitLimit - result.RemainingRequests);

            if (!result.IsSuccess && _settings.LogViolations)
            {
                _logger.LogWarning(
                    "Rate Limit exceeded: {Identifier} -> {Endpoint} | Role: {Role} | Limit: {Limit}/{Window}",
                    identifier, endpoint, role ?? "Anonymous", policy.PermitLimit, policy.Window);
            }

            return result;
        }
        catch (TimeoutException)
        {
            _logger.LogError("Connection pool exhausted! Active: {Active}/{Max} | IsWebRequest: {IsWeb}",
                _connectionPool.ActiveConnections, 50, isWebRequest);

            // Web requests must always get through (even under load)
            if (isWebRequest)
            {
                _logger.LogWarning("Allowing web request despite pool exhaustion (UX priority)");
                return RateLimitResult.CreateSuccess();
            }

            // API requests are blocked
            return RateLimitResult.CreateBlocked("Server overloaded - try again later");
        }
    }

    public Task ResetLimitAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var keysToRemove = _buckets.Keys.Where(k => k.StartsWith($"{identifier}:")).ToList();
        foreach (var key in keysToRemove)
        {
            _buckets.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    public Task<RateLimitStats> GetStatsAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var stats = new RateLimitStats
        {
            Identifier = identifier,
            Buckets = new List<BucketStats>()
        };

        foreach (var kvp in _buckets.Where(kvp => kvp.Key.StartsWith($"{identifier}:")))
        {
            var bucket = kvp.Value;
            stats.Buckets.Add(new BucketStats
            {
                Endpoint = kvp.Key.Replace($"{identifier}:", ""),
                RequestsRemaining = bucket.GetRemainingRequests(),
                WindowResetsIn = bucket.GetResetTime()
            });
        }

        return Task.FromResult(stats);
    }

    public int GetActiveBucketsCount()
    {
        return _buckets.Count;
    }

    private void CleanupInactiveBuckets(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var inactivityThreshold = TimeSpan.FromMinutes(BucketInactivityMinutes);
            var removed = 0;

            foreach (var kvp in _buckets.ToArray())
            {
                var bucket = kvp.Value;
                var lastActivity = bucket.GetLastActivity();

                // Remove buckets inactive for 10 minutes
                if (now - lastActivity > inactivityThreshold)
                {
                    if (_buckets.TryRemove(kvp.Key, out _))
                    {
                        removed++;
                    }
                }
            }

            if (removed > 0)
            {
                _logger.LogInformation("Cleaned up {Count} inactive rate limit buckets", removed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during rate limit bucket cleanup");
        }
    }

    public List<(string Identifier, string Endpoint, int Remaining, TimeSpan ResetIn)> GetAllBuckets()
    {
        var result = new List<(string, string, int, TimeSpan)>();
        var now = DateTime.UtcNow;
        var activeThreshold = TimeSpan.FromMinutes(2);

        foreach (var kvp in _buckets)
        {
            var parts = kvp.Key.Split(':', 2);
            if (parts.Length != 2) continue;

            var identifier = parts[0];
            var endpoint = parts[1];
            var bucket = kvp.Value;

            // Only return buckets active within the last 2 minutes
            if (now - bucket.GetLastActivity() <= activeThreshold)
            {
                result.Add((identifier, endpoint, bucket.GetRemainingRequests(), bucket.GetResetTime()));
            }
        }

        return result;
    }

    // Request tracking
    private void LogRequest(string identifier, string endpoint, bool isSuccess, int requestCount)
    {
        // Geo-location lookup
        string? country = null;
        string? countryCode = null;
        string? city = null;
        double? latitude = null;
        double? longitude = null;

        if (_geoLocationService != null && identifier.StartsWith("ip:"))
        {
            try
            {
                var ip = identifier.Substring(3);
                Task.Run(async () =>
                {
                    try
                    {
                        var location = await _geoLocationService.GetLocationFromIpAsync(ip);

                        if (location != null && location.IsSuccess)
                        {
                            country = location.Country;
                            countryCode = location.IsoCode;
                            city = location.City;
                            latitude = location.Latitude;
                            longitude = location.Longitude;
                        }
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogWarning(innerEx, "Geo-location lookup failed for IP: {IP}", ip);
                    }
                }).Wait(TimeSpan.FromMilliseconds(100));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get geo-location for IP: {Identifier}", identifier);
            }
        }

        var log = new RequestLog
        {
            Timestamp = DateTime.UtcNow,
            Identifier = identifier,
            Endpoint = endpoint,
            IsSuccess = isSuccess,
            RequestCount = requestCount,
            Country = country,
            CountryCode = countryCode,
            City = city,
            Latitude = latitude,
            Longitude = longitude
        };

        _requestLog.Enqueue(log);

        Interlocked.Increment(ref _totalRequests);
        if (!isSuccess)
        {
            Interlocked.Increment(ref _blockedRequests);
        }

        while (_requestLog.Count > MaxLogSize)
        {
            _requestLog.TryDequeue(out _);
        }
    }

    public List<RequestLog> GetRecentRequests(int count = 500)
    {
        var actualCount = Math.Min(count, _requestLog.Count);

        _logger.LogDebug("GetRecentRequests called with count={RequestedCount}, returning {ActualCount} requests",
            count, actualCount);

        return _requestLog
            .OrderByDescending(r => r.Timestamp)
            .Take(actualCount)
            .ToList();
    }

    public RateLimitStatistics GetGlobalStatistics()
    {
        return new RateLimitStatistics
        {
            TotalRequests = _totalRequests,
            BlockedRequests = _blockedRequests,
            SuccessRequests = _totalRequests - _blockedRequests,
            ActiveBuckets = _buckets.Count,
            BlockRate = _totalRequests > 0 ? (double)_blockedRequests / _totalRequests * 100 : 0
        };
    }

    private RateLimitPolicy GetPolicyForRequest(string endpoint, string? role)
    {
        // 1. Check endpoint-specific overrides
        foreach (var kvp in _settings.EndpointOverrides)
        {
            if (IsEndpointMatch(endpoint, kvp.Key))
            {
                return kvp.Value;
            }
        }

        // 2. Check role-based policy
        return role?.ToLower() switch
        {
            "superadmin" => _settings.SuperAdmin,
            "admin" or "manager" => _settings.Admin,
            "user" => _settings.Authenticated,
            _ => _settings.Anonymous
        };
    }

    private bool IsEndpointMatch(string endpoint, string pattern)
    {
        if (pattern.EndsWith("/*"))
        {
            var prefix = pattern[..^2];
            return endpoint.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return endpoint.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    public BurstAttackDetection DetectBurstAttack(string identifier)
    {
        var recentRequests = _requestLog
            .Where(r => r.Identifier == identifier &&
                (DateTime.UtcNow - r.Timestamp).TotalSeconds <= 10)
            .OrderBy(r => r.Timestamp)
            .ToList();

        if (!recentRequests.Any())
            return new BurstAttackDetection { IsBurstAttack = false, Identifier = identifier };

        var burst = new BurstAttackDetection
        {
            Identifier = identifier,
            RequestsInBurst = recentRequests.Count,
            BurstDuration = recentRequests.Last().Timestamp - recentRequests.First().Timestamp,
        };

        burst.RequestsPerSecond = burst.RequestsInBurst /
            Math.Max(burst.BurstDuration.TotalSeconds, 0.1);

        // Burst attack: 50+ requests in 10 seconds
        burst.IsBurstAttack = burst.RequestsInBurst > 50 &&
            burst.BurstDuration.TotalSeconds <= 10;

        _logger.LogInformation(
            "Burst detection ({Identifier}): Requests={Requests}, Duration={Duration}s, Pattern={Pattern}",
            identifier, burst.RequestsInBurst, burst.BurstDuration.TotalSeconds, burst.IsBurstAttack);

        return burst;
    }

    public BruteForceDetection DetectBruteForce(string identifier)
    {
        var authEndpoints = new[] { "/api/auth/login", "/login", "/api/users/authenticate", "/api/auth/apikey" };

        // Search all failed auth attempts (not just from a single IP)
        var authRequests = _requestLog
            .Where(r => !r.IsSuccess &&
                authEndpoints.Any(ep => r.Endpoint.Contains(ep, StringComparison.OrdinalIgnoreCase)) &&
                (DateTime.UtcNow - r.Timestamp).TotalMinutes <= 15)
            .ToList();

        var detection = new BruteForceDetection
        {
            Identifier = identifier,
            FailedAttempts = authRequests.Count,
            TargetedEndpoints = authRequests.Select(r => r.Endpoint).Distinct().ToList(),
            AttackDuration = authRequests.Any()
                ? authRequests.Max(r => r.Timestamp) - authRequests.Min(r => r.Timestamp)
                : TimeSpan.Zero
        };

        // Brute-force: 10+ failed attempts in 15 minutes
        detection.IsBruteForce = detection.FailedAttempts >= 10;

        if (detection.IsBruteForce)
        {
            _logger.LogWarning("BruteForce Attack Detected: {Attempts} failed attempts in {Duration} minutes",
                detection.FailedAttempts, detection.AttackDuration.TotalMinutes);
        }

        return detection;
    }

    public DDoSDetection DetectDDoS(TimeSpan timeWindow)
    {
        var cutoff = DateTime.UtcNow - timeWindow;
        var recentRequests = _requestLog
            .Where(r => r.Timestamp >= cutoff)
            .ToList();

        var ipGroups = recentRequests
            .GroupBy(r => r.Identifier)
            .Select(g => new { IP = g.Key, Count = g.Count() })
            .ToList();

        var detection = new DDoSDetection
        {
            UniqueIPsInvolved = ipGroups.Count,
            TotalRequests = recentRequests.Count,
            AverageRequestsPerIP = ipGroups.Any() ? ipGroups.Average(g => g.Count) : 0,
            SuspiciousIPs = ipGroups
                .Where(g => g.Count > 40)
                .Select(g => g.IP)
                .ToList()
        };

        // DDoS pattern: 10+ IPs, 300+ requests, 20+ req/IP
        detection.IsDDoSPattern =
            detection.UniqueIPsInvolved > 10 &&
            detection.TotalRequests > 300 &&
            detection.AverageRequestsPerIP > 20;

        return detection;
    }

    public SlowRateAttackDetection DetectSlowRateAttack()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        // IPs consistently near the limit (but not over)
        var consistentOffenders = _requestLog
            .Where(r => r.Timestamp >= cutoff)
            .GroupBy(r => r.Identifier)
            .Where(g =>
            {
                var hourlyGroups = g.GroupBy(r => r.Timestamp.Hour);
                // Suspicious if active in at least 8 distinct hours
                return hourlyGroups.Count() >= 8;
            })
            .Select(g => g.Key)
            .ToList();

        var detection = new SlowRateAttackDetection
        {
            ConsistentOffenders = consistentOffenders,
            SuspiciousPatternCount = consistentOffenders.Count,
            IsSlowRateAttack = consistentOffenders.Count >= 3
        };

        if (detection.IsSlowRateAttack)
        {
            _logger.LogWarning("Slow-rate attack detected: {Count} consistent offenders over 24 hours",
                detection.SuspiciousPatternCount);
        }

        return detection;
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _connectionPool?.Dispose();
        _logger.LogInformation("RateLimitService disposed (connection pool + cleanup timer)");
    }

    /// <summary>
    /// Logs failed auth attempts for brute-force detection.
    /// Called directly from the AuthenticationHandler.
    /// </summary>
    public void LogFailedAuthAttempt(string identifier, string endpoint)
    {
        try
        {
            var log = new RequestLog
            {
                Timestamp = DateTime.UtcNow,
                Identifier = identifier,
                Endpoint = endpoint,
                IsSuccess = false,
                RequestCount = 0
            };

            _requestLog.Enqueue(log);

            Interlocked.Increment(ref _totalRequests);
            Interlocked.Increment(ref _blockedRequests);

            while (_requestLog.Count > MaxLogSize)
            {
                _requestLog.TryDequeue(out _);
            }

            _logger.LogDebug("Logged failed auth attempt: {Identifier} -> {Endpoint}", identifier, endpoint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log auth attempt for brute-force detection");
        }
    }
}

/// <summary>
/// Rate limit bucket using token bucket algorithm.
/// </summary>
internal class RateLimitBucket
{
    private readonly RateLimitPolicy _policy;
    private readonly object _lock = new();
    private int _tokens;
    private DateTime _lastRefill;
    private DateTime _lastActivity;

    public RateLimitBucket(RateLimitPolicy policy)
    {
        _policy = policy;
        _tokens = policy.PermitLimit;
        _lastRefill = DateTime.UtcNow;
        _lastActivity = DateTime.UtcNow;
    }

    public async Task<RateLimitResult> TryConsumeAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            RefillTokens();
            _lastActivity = DateTime.UtcNow;

            if (_tokens > 0)
            {
                _tokens--;
                return RateLimitResult.CreateSuccess(_tokens);
            }

            var retryAfter = _policy.Window - (DateTime.UtcNow - _lastRefill);
            return RateLimitResult.CreateExceeded(retryAfter, _tokens);
        }
    }

    public int GetRemainingRequests()
    {
        lock (_lock)
        {
            RefillTokens();
            return _tokens;
        }
    }

    public TimeSpan GetResetTime()
    {
        lock (_lock)
        {
            var elapsed = DateTime.UtcNow - _lastRefill;
            return _policy.Window - elapsed;
        }
    }

    public DateTime GetLastActivity()
    {
        lock (_lock)
        {
            return _lastActivity;
        }
    }

    private void RefillTokens()
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _lastRefill;

        if (elapsed >= _policy.Window)
        {
            _tokens = _policy.PermitLimit;
            _lastRefill = now;
        }
    }
}

/// <summary>
/// Rate limit check result.
/// </summary>
public sealed class RateLimitResult
{
    public bool IsSuccess { get; set; }
    public string? Message { get; set; }
    public int RemainingRequests { get; set; }
    public TimeSpan? RetryAfter { get; set; }

    public static RateLimitResult CreateSuccess(int remaining = int.MaxValue) =>
        new() { IsSuccess = true, RemainingRequests = remaining };

    public static RateLimitResult CreateExceeded(TimeSpan retryAfter, int remaining = 0) =>
        new()
        {
            IsSuccess = false,
            Message = "Rate limit exceeded",
            RetryAfter = retryAfter,
            RemainingRequests = remaining
        };

    public static RateLimitResult CreateBlocked(string reason) =>
        new() { IsSuccess = false, Message = reason, RemainingRequests = 0 };
}

/// <summary>
/// Rate limit statistics per identifier.
/// </summary>
public sealed class RateLimitStats
{
    public string Identifier { get; set; } = string.Empty;
    public List<BucketStats> Buckets { get; set; } = new();
}

public sealed class BucketStats
{
    public string Endpoint { get; set; } = string.Empty;
    public int RequestsRemaining { get; set; }
    public TimeSpan WindowResetsIn { get; set; }
}

/// <summary>
/// Request log model.
/// </summary>
public sealed class RequestLog
{
    public DateTime Timestamp { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public int RequestCount { get; set; }

    // Geo-location properties
    public string? Country { get; set; }
    public string? CountryCode { get; set; }
    public string? City { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

/// <summary>
/// Global statistics model.
/// </summary>
public sealed class RateLimitStatistics
{
    public int TotalRequests { get; set; }
    public int BlockedRequests { get; set; }
    public int SuccessRequests { get; set; }
    public int ActiveBuckets { get; set; }
    public double BlockRate { get; set; }
}
