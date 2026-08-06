using LagersystemLVHome.Application.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Reflection;

namespace LagersystemLVHome.UnitTests.Services.Security;

public class RateLimitServiceTests
{
    private static RateLimitService Build(RateLimitSettings settings)
        => new(Options.Create(settings), NullLogger<RateLimitService>.Instance);

    [Fact]
    public async Task CheckRateLimitAsync_Disabled_AlwaysSucceeds()
    {
        using var sut = Build(new RateLimitSettings { Enabled = false });

        var result = await sut.CheckRateLimitAsync("1.2.3.4", "/api/x");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRateLimitAsync_Whitelisted_AlwaysSucceeds()
    {
        using var sut = Build(new RateLimitSettings { WhitelistedIPs = { "9.9.9.9" } });

        var result = await sut.CheckRateLimitAsync("9.9.9.9", "/api/x");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRateLimitAsync_Blacklisted_IsBlocked()
    {
        using var sut = Build(new RateLimitSettings { BlacklistedIPs = { "6.6.6.6" } });

        var result = await sut.CheckRateLimitAsync("6.6.6.6", "/api/x");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("blacklist", because: "blacklisted message should mention reason")
            .And.NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckRateLimitAsync_AllowsBelowLimit()
    {
        using var sut = Build(new RateLimitSettings
        {
            Anonymous = new RateLimitPolicy { PermitLimit = 3, Window = TimeSpan.FromMinutes(1) }
        });

        var first = await sut.CheckRateLimitAsync("1.1.1.1", "/api/x");
        var second = await sut.CheckRateLimitAsync("1.1.1.1", "/api/x");

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ResetLimitAsync_RemovesBucketsForIdentifier()
    {
        using var sut = Build(new RateLimitSettings());
        await sut.CheckRateLimitAsync("2.2.2.2", "/api/a");
        await sut.CheckRateLimitAsync("2.2.2.2", "/api/b");

        sut.GetActiveBucketsCount().Should().BeGreaterOrEqualTo(2);

        await sut.ResetLimitAsync("2.2.2.2");

        sut.GetActiveBucketsCount().Should().Be(0);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsBucketsForIdentifier()
    {
        using var sut = Build(new RateLimitSettings());
        await sut.CheckRateLimitAsync("3.3.3.3", "/api/foo");

        var stats = await sut.GetStatsAsync("3.3.3.3");

        stats.Identifier.Should().Be("3.3.3.3");
        stats.Buckets.Should().ContainSingle().Which.Endpoint.Should().Be("/api/foo");
    }

    [Fact]
    public async Task GetAllBuckets_IncludesActiveBuckets()
    {
        using var sut = Build(new RateLimitSettings());
        await sut.CheckRateLimitAsync("4.4.4.4", "/api/bar");

        var all = sut.GetAllBuckets();

        all.Should().Contain(b => b.Identifier == "4.4.4.4" && b.Endpoint == "/api/bar");
    }

    [Fact]
    public void RateLimitResult_FactoryMethods_ProduceExpectedShape()
    {
        RateLimitResult.CreateSuccess(5).IsSuccess.Should().BeTrue();
        RateLimitResult.CreateExceeded(TimeSpan.FromSeconds(1)).IsSuccess.Should().BeFalse();
        RateLimitResult.CreateBlocked("nope").Message.Should().Be("nope");
    }

    // ---------- Reflection helpers for internals not exposed on IRateLimitService ----------
    // RateLimitBucket is `internal`; RateLimitConnectionPool/RequestLog are public but the
    // fields holding them on RateLimitService are private. Following the pattern already used
    // for AuthHelpers (LagersystemLVHome.UnitTests/Services/Auth/AuthHelpersTests.cs), we reach
    // them via reflection instead of adding InternalsVisibleTo or widening production surface.

    private static ConcurrentQueue<RequestLog> GetRequestLog(RateLimitService sut)
        => (ConcurrentQueue<RequestLog>)typeof(RateLimitService)
            .GetField("_requestLog", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(sut)!;

    private static RateLimitConnectionPool GetConnectionPool(RateLimitService sut)
        => (RateLimitConnectionPool)typeof(RateLimitService)
            .GetField("_connectionPool", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(sut)!;

    private static System.Collections.IDictionary GetBuckets(RateLimitService sut)
        => (System.Collections.IDictionary)typeof(RateLimitService)
            .GetField("_buckets", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(sut)!;

    private static void InvokeCleanupInactiveBuckets(RateLimitService sut)
        => typeof(RateLimitService)
            .GetMethod("CleanupInactiveBuckets", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(sut, new object?[] { null });

    private static void SetBucketLastActivity(object bucket, DateTime value)
        => bucket.GetType()
            .GetField("_lastActivity", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(bucket, value);

    // ---------- CheckRateLimitAsync: exceeding the limit ----------

    [Fact]
    public async Task CheckRateLimitAsync_ExceedsLimit_ReturnsExceededWithRetryAfter()
    {
        using var sut = Build(new RateLimitSettings
        {
            Anonymous = new RateLimitPolicy { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) },
            LogViolations = true
        });

        var first = await sut.CheckRateLimitAsync("5.5.5.5", "/api/limited");
        var second = await sut.CheckRateLimitAsync("5.5.5.5", "/api/limited");

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeFalse();
        second.Message.Should().Be("Rate limit exceeded");
        second.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckRateLimitAsync_AfterWindowElapses_TokensRefill()
    {
        using var sut = Build(new RateLimitSettings
        {
            Anonymous = new RateLimitPolicy { PermitLimit = 1, Window = TimeSpan.FromMilliseconds(50) }
        });

        (await sut.CheckRateLimitAsync("refill-ip", "/api/x")).IsSuccess.Should().BeTrue();
        (await sut.CheckRateLimitAsync("refill-ip", "/api/x")).IsSuccess.Should().BeFalse();

        await Task.Delay(80);

        (await sut.CheckRateLimitAsync("refill-ip", "/api/x")).IsSuccess.Should().BeTrue();
    }

    // ---------- GetPolicyForRequest: role-based policy selection ----------

    [Theory]
    [InlineData("superadmin", 10000)]
    [InlineData("SuperAdmin", 10000)]
    [InlineData("admin", 500)]
    [InlineData("manager", 500)]
    [InlineData("user", 100)]
    [InlineData(null, 10)]
    [InlineData("unknown-role", 10)]
    public async Task CheckRateLimitAsync_AppliesRoleBasedPolicy(string? role, int expectedLimit)
    {
        using var sut = Build(new RateLimitSettings());
        var identifier = $"role-{role ?? "anon"}";

        await sut.CheckRateLimitAsync(identifier, "/api/generic", role);

        var stats = await sut.GetStatsAsync(identifier);

        stats.Buckets.Should().ContainSingle().Which.RequestsRemaining.Should().Be(expectedLimit - 1);
    }

    // ---------- GetPolicyForRequest / IsEndpointMatch: endpoint overrides ----------

    [Fact]
    public async Task CheckRateLimitAsync_ExactEndpointOverride_TakesPrecedenceOverRolePolicy()
    {
        using var sut = Build(new RateLimitSettings());

        // "admin" role would normally give 500/min, but the exact "/api/auth/login" override (5/5min) wins.
        await sut.CheckRateLimitAsync("override-ip", "/api/auth/login", role: "admin");

        var stats = await sut.GetStatsAsync("override-ip");

        stats.Buckets.Should().ContainSingle().Which.RequestsRemaining.Should().Be(4);
    }

    [Fact]
    public async Task CheckRateLimitAsync_WildcardEndpointOverride_MatchesPrefix()
    {
        using var sut = Build(new RateLimitSettings());

        await sut.CheckRateLimitAsync("wildcard-ip", "/api/sensors/123/readings");

        var stats = await sut.GetStatsAsync("wildcard-ip");

        // "/api/sensors/*" override grants 60/min.
        stats.Buckets.Should().ContainSingle().Which.RequestsRemaining.Should().Be(59);
    }

    // ---------- Connection pool exhaustion (TimeoutException branch) ----------

    [Fact]
    public async Task CheckRateLimitAsync_PriorityPoolExhausted_WebRequestStillSucceeds()
    {
        using var sut = Build(new RateLimitSettings());
        var pool = GetConnectionPool(sut);

        var leases = new List<IDisposable>();
        for (var i = 0; i < 10; i++)
        {
            leases.Add(await pool.AcquireAsync(TimeSpan.FromSeconds(5), isWebRequest: true));
        }

        try
        {
            var result = await sut.CheckRateLimitAsync("pool-web-ip", "/api/x", isWebRequest: true);

            result.IsSuccess.Should().BeTrue("web requests must get through even when the priority pool is exhausted");
        }
        finally
        {
            foreach (var lease in leases) lease.Dispose();
        }
    }

    [Fact]
    public async Task CheckRateLimitAsync_NormalPoolExhausted_ApiRequestIsBlocked()
    {
        using var sut = Build(new RateLimitSettings());
        var pool = GetConnectionPool(sut);

        var leases = new List<IDisposable>();
        for (var i = 0; i < 50; i++)
        {
            leases.Add(await pool.AcquireAsync(TimeSpan.FromSeconds(5), isWebRequest: false));
        }

        try
        {
            var result = await sut.CheckRateLimitAsync("pool-api-ip", "/api/x", isWebRequest: false);

            result.IsSuccess.Should().BeFalse();
            result.Message.Should().Be("Server overloaded - try again later");
        }
        finally
        {
            foreach (var lease in leases) lease.Dispose();
        }
    }

    // ---------- LogRequest: geo-location enrichment ----------

    [Fact]
    public async Task CheckRateLimitAsync_WithGeoLocationService_DoesNotThrowAndLogsRequest()
    {
        var geo = Substitute.For<IGeoLocationService>();
        geo.GetLocationFromIpAsync("9.10.11.12", Arg.Any<CancellationToken>())
            .Returns(new GeoLocationResult
            {
                IsSuccess = true,
                Country = "Testland",
                IsoCode = "TL",
                City = "Testville",
                Latitude = 1.23,
                Longitude = 4.56
            });

        using var sut = new RateLimitService(Options.Create(new RateLimitSettings()), NullLogger<RateLimitService>.Instance, geo);

        await sut.CheckRateLimitAsync("ip:9.10.11.12", "/api/geo");
        await Task.Delay(150); // LogRequest awaits the geo lookup for up to 100ms internally.

        sut.GetRecentRequests().Should().ContainSingle(r => r.Identifier == "ip:9.10.11.12");
    }

    [Fact]
    public async Task CheckRateLimitAsync_GeoLocationServiceThrows_RequestStillSucceeds()
    {
        var geo = Substitute.For<IGeoLocationService>();
        geo.GetLocationFromIpAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<GeoLocationResult>(new InvalidOperationException("geo unavailable")));

        using var sut = new RateLimitService(Options.Create(new RateLimitSettings()), NullLogger<RateLimitService>.Instance, geo);

        var result = await sut.CheckRateLimitAsync("ip:1.2.3.4", "/api/geo");

        result.IsSuccess.Should().BeTrue();
    }

    // ---------- GetRecentRequests / GetGlobalStatistics ----------

    [Fact]
    public async Task GetRecentRequests_ReturnsMostRecentFirst_LimitedByCount()
    {
        using var sut = Build(new RateLimitSettings());
        await sut.CheckRateLimitAsync("recent-ip", "/api/a");
        await sut.CheckRateLimitAsync("recent-ip", "/api/b");
        await sut.CheckRateLimitAsync("recent-ip", "/api/c");

        var recent = sut.GetRecentRequests(2);

        recent.Should().HaveCount(2);
        recent.Should().BeInDescendingOrder(r => r.Timestamp);
    }

    [Fact]
    public async Task GetGlobalStatistics_ReflectsSuccessAndBlockedCounts()
    {
        using var sut = Build(new RateLimitSettings
        {
            Anonymous = new RateLimitPolicy { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) }
        });

        await sut.CheckRateLimitAsync("stats-ip", "/api/x");
        await sut.CheckRateLimitAsync("stats-ip", "/api/x"); // blocked - over the limit

        var stats = sut.GetGlobalStatistics();

        stats.TotalRequests.Should().Be(2);
        stats.BlockedRequests.Should().Be(1);
        stats.SuccessRequests.Should().Be(1);
        stats.BlockRate.Should().BeApproximately(50.0, 0.01);
    }

    // ---------- DetectBurstAttack ----------

    [Fact]
    public void DetectBurstAttack_NoRecentRequests_ReturnsNotBurst()
    {
        using var sut = Build(new RateLimitSettings());

        var result = sut.DetectBurstAttack("no-such-ip");

        result.IsBurstAttack.Should().BeFalse();
        result.RequestsInBurst.Should().Be(0);
    }

    [Fact]
    public void DetectBurstAttack_51RequestsWithin10Seconds_DetectsBurst()
    {
        using var sut = Build(new RateLimitSettings());
        var queue = GetRequestLog(sut);
        var now = DateTime.UtcNow;

        for (var i = 0; i < 51; i++)
        {
            queue.Enqueue(new RequestLog
            {
                Identifier = "burst-ip",
                Endpoint = "/api/x",
                Timestamp = now.AddSeconds(-5).AddMilliseconds(i * 10),
                IsSuccess = true
            });
        }

        var result = sut.DetectBurstAttack("burst-ip");

        result.IsBurstAttack.Should().BeTrue();
        result.RequestsInBurst.Should().Be(51);
        result.RequestsPerSecond.Should().BeGreaterThan(0);
    }

    // ---------- DetectBruteForce ----------

    [Fact]
    public void DetectBruteForce_NoFailedAuthAttempts_ReturnsNotBruteForce()
    {
        using var sut = Build(new RateLimitSettings());

        sut.DetectBruteForce("no-such-ip").IsBruteForce.Should().BeFalse();
    }

    [Fact]
    public void DetectBruteForce_10FailedLoginAttemptsWithin15Minutes_DetectsBruteForce()
    {
        using var sut = Build(new RateLimitSettings());

        for (var i = 0; i < 10; i++)
        {
            sut.LogFailedAuthAttempt("brute-ip", "/api/auth/login");
        }

        var result = sut.DetectBruteForce("brute-ip");

        result.IsBruteForce.Should().BeTrue();
        result.FailedAttempts.Should().Be(10);
        result.TargetedEndpoints.Should().Contain("/api/auth/login");
    }

    // ---------- DetectDDoS ----------

    [Fact]
    public void DetectDDoS_TrafficBelowThreshold_ReturnsNoPattern()
    {
        using var sut = Build(new RateLimitSettings());

        sut.DetectDDoS(TimeSpan.FromMinutes(1)).IsDDoSPattern.Should().BeFalse();
    }

    [Fact]
    public void DetectDDoS_ManyIPsWithHighVolume_DetectsPatternAndSuspiciousIPs()
    {
        using var sut = Build(new RateLimitSettings());
        var queue = GetRequestLog(sut);
        var now = DateTime.UtcNow;

        for (var ip = 0; ip < 10; ip++)
        {
            for (var i = 0; i < 30; i++)
            {
                queue.Enqueue(new RequestLog { Identifier = $"ddos-{ip}", Endpoint = "/api/x", Timestamp = now, IsSuccess = true });
            }
        }

        // One especially aggressive IP also exercises the SuspiciousIPs filter (> 40 requests).
        for (var i = 0; i < 45; i++)
        {
            queue.Enqueue(new RequestLog { Identifier = "ddos-heavy", Endpoint = "/api/x", Timestamp = now, IsSuccess = true });
        }

        var result = sut.DetectDDoS(TimeSpan.FromMinutes(1));

        result.IsDDoSPattern.Should().BeTrue();
        result.UniqueIPsInvolved.Should().Be(11);
        result.SuspiciousIPs.Should().Contain("ddos-heavy");
    }

    // ---------- DetectSlowRateAttack ----------

    [Fact]
    public void DetectSlowRateAttack_NoConsistentOffenders_ReturnsNoPattern()
    {
        using var sut = Build(new RateLimitSettings());

        sut.DetectSlowRateAttack().IsSlowRateAttack.Should().BeFalse();
    }

    [Fact]
    public void DetectSlowRateAttack_ThreeIdentifiersActiveAcross8Hours_DetectsPattern()
    {
        using var sut = Build(new RateLimitSettings());
        var queue = GetRequestLog(sut);
        var baseTime = DateTime.UtcNow.AddHours(-20);

        for (var id = 0; id < 3; id++)
        {
            for (var hour = 0; hour < 8; hour++)
            {
                queue.Enqueue(new RequestLog { Identifier = $"slow-{id}", Endpoint = "/api/x", Timestamp = baseTime.AddHours(hour), IsSuccess = true });
            }
        }

        var result = sut.DetectSlowRateAttack();

        result.IsSlowRateAttack.Should().BeTrue();
        result.SuspiciousPatternCount.Should().Be(3);
        result.ConsistentOffenders.Should().Contain(new[] { "slow-0", "slow-1", "slow-2" });
    }

    // ---------- CleanupInactiveBuckets (private timer callback) ----------

    [Fact]
    public async Task CleanupInactiveBuckets_RemovesBucketsInactiveForOver10Minutes()
    {
        using var sut = Build(new RateLimitSettings());
        await sut.CheckRateLimitAsync("stale-ip", "/api/x");
        sut.GetActiveBucketsCount().Should().Be(1);

        var buckets = GetBuckets(sut);
        SetBucketLastActivity(buckets["stale-ip:/api/x"]!, DateTime.UtcNow.AddMinutes(-15));

        InvokeCleanupInactiveBuckets(sut);

        sut.GetActiveBucketsCount().Should().Be(0);
    }

    [Fact]
    public async Task CleanupInactiveBuckets_LeavesRecentlyActiveBucketsAlone()
    {
        using var sut = Build(new RateLimitSettings());
        await sut.CheckRateLimitAsync("fresh-ip", "/api/x");

        InvokeCleanupInactiveBuckets(sut);

        sut.GetActiveBucketsCount().Should().Be(1);
    }

    // ---------- GetAllBuckets: activity window + defensive key parsing ----------

    [Fact]
    public async Task GetAllBuckets_ExcludesBucketsInactiveForOver2Minutes()
    {
        using var sut = Build(new RateLimitSettings());
        await sut.CheckRateLimitAsync("stale-view-ip", "/api/x");

        var buckets = GetBuckets(sut);
        SetBucketLastActivity(buckets["stale-view-ip:/api/x"]!, DateTime.UtcNow.AddMinutes(-5));

        sut.GetAllBuckets().Should().NotContain(b => b.Identifier == "stale-view-ip");
    }

    [Fact]
    public async Task GetAllBuckets_SkipsMalformedKeysWithoutColon()
    {
        using var sut = Build(new RateLimitSettings());
        await sut.CheckRateLimitAsync("normal-ip", "/api/x");

        var buckets = GetBuckets(sut);
        // Defensive guard in GetAllBuckets (parts.Length != 2) is otherwise unreachable, since
        // production code always builds keys as "{identifier}:{endpoint}".
        buckets.Add("malformed-key-no-colon", buckets["normal-ip:/api/x"]!);

        var act = () => sut.GetAllBuckets();

        act.Should().NotThrow();
        sut.GetAllBuckets().Should().NotContain(b => b.Identifier == "malformed-key-no-colon");
    }
}
