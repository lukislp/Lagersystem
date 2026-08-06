using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Reporting;

public class ApplicationInsightsServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static ApplicationInsightsService Build(IDbContextFactory<InventoryDbContext> factory)
        => new(factory, NullLogger<ApplicationInsightsService>.Instance);

    // ---- Entity builders ----------------------------------------------------

    private static User MakeUser(int id, int warehouseId = 1) => new()
    {
        Id = id,
        Username = $"u{id}",
        Email = $"u{id}@x.local",
        PasswordHash = "x",
        WarehouseId = warehouseId
    };

    private static PageView MakePageView(
        int id,
        int userId,
        string pageUrl,
        DateTime timestamp,
        string sessionId = "s1",
        string? username = null,
        string? referrer = null,
        string? country = null,
        string? city = null,
        string? deviceType = null,
        string? browser = null,
        string? os = null,
        int? loadTimeMs = null,
        string ipAddress = "127.0.0.1",
        string? warehouseName = null,
        string userRole = "User") => new()
        {
            Id = id,
            UserId = userId,
            Username = username ?? $"u{userId}",
            UserRole = userRole,
            PageUrl = pageUrl,
            SessionId = sessionId,
            IpAddress = ipAddress,
            UserAgent = "test-agent",
            Timestamp = timestamp,
            Referrer = referrer,
            Country = country,
            City = city,
            DeviceType = deviceType,
            Browser = browser,
            OperatingSystem = os,
            LoadTimeMs = loadTimeMs,
            WarehouseName = warehouseName
        };

    private static ApiRequest MakeApiRequest(
        string endpoint,
        int statusCode,
        DateTime timestamp,
        int durationMs = 100,
        bool isError = false) => new()
        {
            Endpoint = endpoint,
            Method = "GET",
            StatusCode = statusCode,
            Timestamp = timestamp,
            DurationMs = durationMs,
            IsError = isError,
            IpAddress = "127.0.0.1",
            UserAgent = "test-agent"
        };

    private static PerformanceMetric MakeMetric(DateTime timestamp, double cpu = 1, long mem = 10) => new()
    {
        Timestamp = timestamp,
        CpuUsagePercent = cpu,
        MemoryUsedMB = mem
    };

    private static UserActivity MakeActivity(int userId, string activityType, DateTime timestamp, string? entityType = null) => new()
    {
        UserId = userId,
        ActivityType = activityType,
        EntityType = entityType ?? "Product",
        Timestamp = timestamp,
        SessionId = "s1"
    };

    // ---- TrackPageViewAsync ---------------------------------------------------

    [Fact]
    public async Task TrackPageViewAsync_AddsEntityToStore()
    {
        var factory = CreateFactory(nameof(TrackPageViewAsync_AddsEntityToStore));
        var sut = Build(factory);

        await sut.TrackPageViewAsync(MakePageView(1, 1, "/home", DateTime.UtcNow));

        await using var db = factory.CreateDbContext();
        (await db.PageViews.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TrackPageViewAsync_DuplicateId_SwallowsErrorAndDoesNotThrow()
    {
        var factory = CreateFactory(nameof(TrackPageViewAsync_DuplicateId_SwallowsErrorAndDoesNotThrow));
        await using (var db = factory.CreateDbContext())
        {
            db.PageViews.Add(MakePageView(1, 1, "/home", DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var act = () => sut.TrackPageViewAsync(MakePageView(1, 1, "/other", DateTime.UtcNow));

        await act.Should().NotThrowAsync("SaveChanges failures are caught and logged, not propagated");
    }

    // ---- GetRecentPageViewsAsync (Include User) --------------------------------

    [Fact]
    public async Task GetRecentPageViewsAsync_OrdersDescendingAndRespectsCount()
    {
        var factory = CreateFactory(nameof(GetRecentPageViewsAsync_OrdersDescendingAndRespectsCount));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            db.PageViews.AddRange(
                MakePageView(1, 1, "/a", now.AddMinutes(-3)),
                MakePageView(2, 1, "/b", now.AddMinutes(-1)),
                MakePageView(3, 1, "/c", now.AddMinutes(-2)));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var result = await sut.GetRecentPageViewsAsync(count: 2);

        result.Should().HaveCount(2);
        result[0].PageUrl.Should().Be("/b");
        result[1].PageUrl.Should().Be("/c");
        result.All(pv => pv.User is not null).Should().BeTrue("GetRecentPageViewsAsync includes the User navigation");
    }

    // ---- GetTopPagesAsync ------------------------------------------------------

    [Fact]
    public async Task GetTopPagesAsync_FiltersByDateRangeAndOrdersDescending()
    {
        var factory = CreateFactory(nameof(GetTopPagesAsync_FiltersByDateRangeAndOrdersDescending));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.PageViews.AddRange(
                MakePageView(1, 1, "/a", now.AddDays(-1)),
                MakePageView(2, 1, "/a", now.AddDays(-1)),
                MakePageView(3, 1, "/b", now.AddDays(-1)),
                MakePageView(4, 1, "/a", now.AddDays(-10))); // outside range
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var result = await sut.GetTopPagesAsync(now.AddDays(-2), now);

        result.Should().ContainKey("/a").WhoseValue.Should().Be(2);
        result.Should().ContainKey("/b").WhoseValue.Should().Be(1);
        result.Keys.First().Should().Be("/a", "results are ordered descending by count");
    }

    // ---- GetPageViewsByUserAsync ------------------------------------------------

    [Fact]
    public async Task GetPageViewsByUserAsync_GroupsByUsername()
    {
        var factory = CreateFactory(nameof(GetPageViewsByUserAsync_GroupsByUsername));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.PageViews.AddRange(
                MakePageView(1, 1, "/a", now, username: "alice"),
                MakePageView(2, 1, "/b", now, username: "alice"),
                MakePageView(3, 2, "/a", now, username: "bob"));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var result = await sut.GetPageViewsByUserAsync();

        result["alice"].Should().Be(2);
        result["bob"].Should().Be(1);
    }

    // ---- TrackApiRequestAsync ---------------------------------------------------

    [Fact]
    public async Task TrackApiRequestAsync_AddsEntityToStore()
    {
        var factory = CreateFactory(nameof(TrackApiRequestAsync_AddsEntityToStore));
        var sut = Build(factory);

        await sut.TrackApiRequestAsync(MakeApiRequest("/api/x", 200, DateTime.UtcNow));

        await using var db = factory.CreateDbContext();
        (await db.ApiRequests.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TrackApiRequestAsync_DuplicateId_SwallowsErrorAndDoesNotThrow()
    {
        var factory = CreateFactory(nameof(TrackApiRequestAsync_DuplicateId_SwallowsErrorAndDoesNotThrow));
        await using (var db = factory.CreateDbContext())
        {
            var req = MakeApiRequest("/api/x", 200, DateTime.UtcNow);
            req.Id = 1;
            db.ApiRequests.Add(req);
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var dup = MakeApiRequest("/api/y", 200, DateTime.UtcNow);
        dup.Id = 1;

        var act = () => sut.TrackApiRequestAsync(dup);

        await act.Should().NotThrowAsync();
    }

    // ---- GetRecentApiRequestsAsync ----------------------------------------------

    [Fact]
    public async Task GetRecentApiRequestsAsync_OrdersDescendingAndRespectsCount()
    {
        var factory = CreateFactory(nameof(GetRecentApiRequestsAsync_OrdersDescendingAndRespectsCount));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.ApiRequests.AddRange(
                MakeApiRequest("/a", 200, now.AddMinutes(-2)),
                MakeApiRequest("/b", 200, now.AddMinutes(-1)));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var result = await sut.GetRecentApiRequestsAsync(count: 1);

        result.Should().ContainSingle().Which.Endpoint.Should().Be("/b");
    }

    // ---- GetApiEndpointStatsAsync -----------------------------------------------

    [Fact]
    public async Task GetApiEndpointStatsAsync_GroupsByEndpointWithinRange()
    {
        var factory = CreateFactory(nameof(GetApiEndpointStatsAsync_GroupsByEndpointWithinRange));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.ApiRequests.AddRange(
                MakeApiRequest("/a", 200, now),
                MakeApiRequest("/a", 200, now),
                MakeApiRequest("/b", 200, now.AddDays(-10))); // outside range
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var result = await sut.GetApiEndpointStatsAsync(now.AddDays(-1), now.AddDays(1));

        result.Should().ContainKey("/a").WhoseValue.Should().Be(2);
        result.Should().NotContainKey("/b");
    }

    // ---- GetApiSuccessRateAsync --------------------------------------------------

    [Fact]
    public async Task GetApiSuccessRateAsync_NoRequests_Returns100()
    {
        var factory = CreateFactory(nameof(GetApiSuccessRateAsync_NoRequests_Returns100));
        var sut = Build(factory);

        (await sut.GetApiSuccessRateAsync()).Should().Be(100);
    }

    [Fact]
    public async Task GetApiSuccessRateAsync_ComputesPercentageOf2xxResponses()
    {
        var factory = CreateFactory(nameof(GetApiSuccessRateAsync_ComputesPercentageOf2xxResponses));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.ApiRequests.AddRange(
                MakeApiRequest("/a", 200, now),
                MakeApiRequest("/a", 201, now),
                MakeApiRequest("/a", 404, now),
                MakeApiRequest("/a", 500, now));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        (await sut.GetApiSuccessRateAsync()).Should().Be(50);
    }

    // ---- TrackPerformanceMetricAsync (+ 7-day cleanup) ---------------------------

    [Fact]
    public async Task TrackPerformanceMetricAsync_AddsMetricAndPrunesOlderThanSevenDays()
    {
        var factory = CreateFactory(nameof(TrackPerformanceMetricAsync_AddsMetricAndPrunesOlderThanSevenDays));
        await using (var db = factory.CreateDbContext())
        {
            db.PerformanceMetrics.Add(MakeMetric(DateTime.UtcNow.AddDays(-8))); // stale, should be pruned
            db.PerformanceMetrics.Add(MakeMetric(DateTime.UtcNow.AddDays(-1))); // recent, should remain
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        await sut.TrackPerformanceMetricAsync(MakeMetric(DateTime.UtcNow));

        await using var verify = factory.CreateDbContext();
        var remaining = await verify.PerformanceMetrics.ToListAsync();
        remaining.Should().HaveCount(2, "the newly tracked metric plus the recent one; the 8-day-old metric is pruned");
        remaining.Should().OnlyContain(m => m.Timestamp >= DateTime.UtcNow.AddDays(-7));
    }

    [Fact]
    public async Task TrackPerformanceMetricAsync_DuplicateId_SwallowsErrorAndDoesNotThrow()
    {
        var factory = CreateFactory(nameof(TrackPerformanceMetricAsync_DuplicateId_SwallowsErrorAndDoesNotThrow));
        await using (var db = factory.CreateDbContext())
        {
            var metric = MakeMetric(DateTime.UtcNow);
            metric.Id = 1;
            db.PerformanceMetrics.Add(metric);
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var dup = MakeMetric(DateTime.UtcNow);
        dup.Id = 1;

        var act = () => sut.TrackPerformanceMetricAsync(dup);

        await act.Should().NotThrowAsync();
    }

    // ---- GetPerformanceHistoryAsync ----------------------------------------------

    [Fact]
    public async Task GetPerformanceHistoryAsync_FiltersByHoursWindowAndOrdersAscending()
    {
        var factory = CreateFactory(nameof(GetPerformanceHistoryAsync_FiltersByHoursWindowAndOrdersAscending));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.PerformanceMetrics.AddRange(
                MakeMetric(now.AddHours(-30)), // outside default 24h window
                MakeMetric(now.AddHours(-2)),
                MakeMetric(now.AddHours(-1)));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var result = await sut.GetPerformanceHistoryAsync(hours: 24);

        result.Should().HaveCount(2);
        result.Should().BeInAscendingOrder(m => m.Timestamp);
    }

    // ---- GetCurrentPerformanceAsync ----------------------------------------------

    [Fact]
    public async Task GetCurrentPerformanceAsync_ComputesLiveSnapshot()
    {
        var factory = CreateFactory(nameof(GetCurrentPerformanceAsync_ComputesLiveSnapshot));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.ApiRequests.AddRange(
                MakeApiRequest("/a", 200, now.AddMinutes(-30), durationMs: 100),
                MakeApiRequest("/a", 200, now.AddMinutes(-10), durationMs: 300),
                MakeApiRequest("/a", 200, now.AddHours(-2), durationMs: 999)); // outside 1h window
            db.PageViews.Add(MakePageView(1, 1, "/a", now.AddMinutes(-1))); // within 5 min -> active user
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var metric = await sut.GetCurrentPerformanceAsync();

        metric.TotalRequests.Should().Be(2, "only requests within the last hour are counted");
        metric.AvgResponseTimeMs.Should().Be(200);
        metric.ActiveUsers.Should().Be(1);
        metric.CpuUsagePercent.Should().BeGreaterThanOrEqualTo(0);
        metric.MemoryUsedMB.Should().BeGreaterThanOrEqualTo(0);
        // Suspected bug: MemoryTotalMB is hardcoded to long.MinValue instead of an actual total-memory reading.
        metric.MemoryTotalMB.Should().Be(long.MinValue);
    }

    [Fact]
    public async Task GetCurrentPerformanceAsync_NoRecentRequests_AvgResponseTimeIsZero()
    {
        var factory = CreateFactory(nameof(GetCurrentPerformanceAsync_NoRecentRequests_AvgResponseTimeIsZero));
        var sut = Build(factory);

        var metric = await sut.GetCurrentPerformanceAsync();

        metric.TotalRequests.Should().Be(0);
        metric.AvgResponseTimeMs.Should().Be(0);
        metric.ActiveUsers.Should().Be(0);
    }

    // ---- TrackUserActivityAsync ---------------------------------------------------

    [Fact]
    public async Task TrackUserActivityAsync_AddsEntityToStore()
    {
        var factory = CreateFactory(nameof(TrackUserActivityAsync_AddsEntityToStore));
        var sut = Build(factory);

        await sut.TrackUserActivityAsync(MakeActivity(1, "Login", DateTime.UtcNow));

        await using var db = factory.CreateDbContext();
        (await db.UserActivities.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TrackUserActivityAsync_DuplicateId_SwallowsErrorAndDoesNotThrow()
    {
        var factory = CreateFactory(nameof(TrackUserActivityAsync_DuplicateId_SwallowsErrorAndDoesNotThrow));
        await using (var db = factory.CreateDbContext())
        {
            var activity = MakeActivity(1, "Login", DateTime.UtcNow);
            activity.Id = 1;
            db.UserActivities.Add(activity);
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var dup = MakeActivity(1, "Logout", DateTime.UtcNow);
        dup.Id = 1;

        var act = () => sut.TrackUserActivityAsync(dup);

        await act.Should().NotThrowAsync();
    }

    // ---- GetUserActivityAsync / GetRecentActivityAsync (Include User) -------------

    [Fact]
    public async Task GetUserActivityAsync_FiltersByUserIdAndOrdersDescending()
    {
        var factory = CreateFactory(nameof(GetUserActivityAsync_FiltersByUserIdAndOrdersDescending));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(MakeUser(1), MakeUser(2));
            db.UserActivities.AddRange(
                MakeActivity(1, "Login", now.AddMinutes(-2)),
                MakeActivity(1, "Logout", now.AddMinutes(-1)),
                MakeActivity(2, "Login", now));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var result = await sut.GetUserActivityAsync(userId: 1);

        result.Should().HaveCount(2);
        result[0].ActivityType.Should().Be("Logout");
        result.All(a => a.User is not null).Should().BeTrue();
    }

    [Fact]
    public async Task GetRecentActivityAsync_ReturnsAcrossAllUsersOrderedDescending()
    {
        var factory = CreateFactory(nameof(GetRecentActivityAsync_ReturnsAcrossAllUsersOrderedDescending));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(MakeUser(1), MakeUser(2));
            db.UserActivities.AddRange(
                MakeActivity(1, "Login", now.AddMinutes(-2)),
                MakeActivity(2, "Login", now.AddMinutes(-1)));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var result = await sut.GetRecentActivityAsync(count: 50);

        result.Should().HaveCount(2);
        result[0].UserId.Should().Be(2);
    }

    // ---- GetActivityTypeStatsAsync -------------------------------------------------

    [Fact]
    public async Task GetActivityTypeStatsAsync_GroupsByTypeWithinRange()
    {
        var factory = CreateFactory(nameof(GetActivityTypeStatsAsync_GroupsByTypeWithinRange));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.UserActivities.AddRange(
                MakeActivity(1, "Login", now),
                MakeActivity(1, "Login", now),
                MakeActivity(1, "Export", now.AddDays(-10))); // outside range
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var result = await sut.GetActivityTypeStatsAsync(now.AddDays(-1), now.AddDays(1));

        result.Should().ContainKey("Login").WhoseValue.Should().Be(2);
        result.Should().NotContainKey("Export");
    }

    // ---- GetActiveSessionsAsync ----------------------------------------------------

    [Fact]
    public async Task GetActiveSessionsAsync_GroupsBySessionWithinLastFiveMinutes()
    {
        var factory = CreateFactory(nameof(GetActiveSessionsAsync_GroupsBySessionWithinLastFiveMinutes));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.PageViews.AddRange(
                MakePageView(1, 1, "/a", now.AddMinutes(-3), sessionId: "s1", username: "alice", deviceType: "Mobile"),
                MakePageView(2, 1, "/b", now.AddMinutes(-1), sessionId: "s1", username: "alice", deviceType: "Mobile"),
                MakePageView(3, 2, "/c", now.AddMinutes(-20), sessionId: "s2", username: "bob")); // outside window
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var sessions = await sut.GetActiveSessionsAsync();

        sessions.Should().ContainSingle();
        var session = sessions[0];
        session.SessionId.Should().Be("s1");
        session.Username.Should().Be("alice");
        session.PageViews.Should().Be(2);
        session.CurrentPage.Should().Be("/b", "the most recent page view in the session");
        session.DeviceType.Should().Be("Mobile");
    }

    [Fact]
    public async Task GetActiveSessionsAsync_NullDeviceType_DefaultsToUnknown()
    {
        var factory = CreateFactory(nameof(GetActiveSessionsAsync_NullDeviceType_DefaultsToUnknown));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.PageViews.Add(MakePageView(1, 1, "/a", now.AddMinutes(-1), sessionId: "s1", deviceType: null));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var sessions = await sut.GetActiveSessionsAsync();

        sessions.Should().ContainSingle().Which.DeviceType.Should().Be("Unknown");
    }

    // ---- GetDeviceTypeStatsAsync / GetBrowserStatsAsync -----------------------------

    [Fact]
    public async Task GetDeviceTypeStatsAsync_GroupsByDeviceTypeIgnoringNulls()
    {
        var factory = CreateFactory(nameof(GetDeviceTypeStatsAsync_GroupsByDeviceTypeIgnoringNulls));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.PageViews.AddRange(
                MakePageView(1, 1, "/a", now, deviceType: "Mobile"),
                MakePageView(2, 1, "/a", now, deviceType: "Mobile"),
                MakePageView(3, 1, "/a", now, deviceType: null));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var result = await sut.GetDeviceTypeStatsAsync();

        result.Should().ContainKey("Mobile").WhoseValue.Should().Be(2);
        result.Values.Sum().Should().Be(2, "page views with a null DeviceType are excluded entirely");
    }

    [Fact]
    public async Task GetBrowserStatsAsync_TopFiveOrderedDescending()
    {
        var factory = CreateFactory(nameof(GetBrowserStatsAsync_TopFiveOrderedDescending));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            var id = 1;
            for (var i = 0; i < 3; i++)
                db.PageViews.Add(MakePageView(id++, 1, "/a", now, browser: "Chrome"));
            db.PageViews.Add(MakePageView(id++, 1, "/a", now, browser: "Firefox"));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var result = await sut.GetBrowserStatsAsync();

        result.Keys.First().Should().Be("Chrome");
        result["Chrome"].Should().Be(3);
        result["Firefox"].Should().Be(1);
    }

    // ---- GetDashboardStatsAsync (aggregates all private helper metrics) -------------

    [Fact]
    public async Task GetDashboardStatsAsync_EmptyDatabase_ReturnsZeroedDefaults()
    {
        var factory = CreateFactory(nameof(GetDashboardStatsAsync_EmptyDatabase_ReturnsZeroedDefaults));
        var sut = Build(factory);

        var stats = await sut.GetDashboardStatsAsync();

        stats.TotalPageViews.Should().Be(0);
        stats.TotalApiRequests.Should().Be(0);
        stats.TotalUsers.Should().Be(0);
        stats.ApiSuccessRate.Should().Be(100);
        stats.BounceRate.Should().Be(0);
        stats.ErrorRate.Should().Be(0);
        stats.UserRetention.Should().Be(0);
        stats.NewVsReturningUsers["New Users"].Should().Be(0);
        stats.TopPages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDashboardStatsAsync_AggregatesAcrossAllMetrics()
    {
        var factory = CreateFactory(nameof(GetDashboardStatsAsync_AggregatesAcrossAllMetrics));
        var now = DateTime.UtcNow;
        var from = now.AddDays(-2);
        var to = now;

        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(MakeUser(1), MakeUser(2));

            // Session A (user 1): two page views -> not a bounce, 1h apart.
            db.PageViews.AddRange(
                MakePageView(1, 1, "/products", from.AddHours(1), sessionId: "A",
                    referrer: "https://google.com", country: "DE", city: "Berlin",
                    deviceType: "Desktop", browser: "Chrome", os: "Windows",
                    loadTimeMs: 100, ipAddress: "10.0.0.1", warehouseName: "WH1", userRole: "Admin"),
                MakePageView(2, 1, "/scanner", from.AddHours(2), sessionId: "A",
                    loadTimeMs: 300, ipAddress: "10.0.0.2", warehouseName: "WH1", userRole: "Admin"),
                // Session B (user 2): single page view -> bounce.
                MakePageView(3, 2, "/products", from.AddHours(3), sessionId: "B",
                    loadTimeMs: 50, ipAddress: "10.0.0.3", warehouseName: "WH2", userRole: "User"));

            db.ApiRequests.AddRange(
                MakeApiRequest("/api/products", 200, from.AddHours(1), durationMs: 100, isError: false),
                MakeApiRequest("/api/products", 500, from.AddHours(2), durationMs: 200, isError: true));

            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var stats = await sut.GetDashboardStatsAsync(from, to);

        stats.TotalPageViews.Should().Be(3);
        stats.TotalApiRequests.Should().Be(2);
        stats.TotalUsers.Should().Be(2);
        stats.TotalSessions.Should().Be(2);
        stats.AvgPageLoadTimeMs.Should().Be((100 + 300 + 50) / 3.0);
        stats.AvgApiResponseTimeMs.Should().Be((100 + 200) / 2.0);
        stats.ApiSuccessRate.Should().Be(50);

        stats.TopPages.Should().Contain(kv => kv.Key == "/products" && kv.Value == 2);
        stats.TopUsers.Should().Contain(kv => kv.Key == "u1" && kv.Value == 2);
        stats.TopApiEndpoints.Should().Contain(kv => kv.Key == "/api/products" && kv.Value == 2);

        stats.DeviceTypes.Should().ContainKey("Desktop").WhoseValue.Should().Be(1);
        stats.Browsers.Should().ContainKey("Chrome").WhoseValue.Should().Be(1);
        stats.OperatingSystems.Should().ContainKey("Windows").WhoseValue.Should().Be(1);

        stats.UniqueVisitors.Should().Be(3, "each page view used a distinct IP address");
        stats.AvgSessionDuration.Should().BeApproximately(30, 0.01, "session A spans 60 minutes, session B spans 0");
        stats.BounceRate.Should().Be(50, "one of the two sessions has a single page view");

        stats.TopReferrers.Should().Contain(kv => kv.Key == "https://google.com" && kv.Value == 1);
        stats.TopCountries.Should().Contain(kv => kv.Key == "DE" && kv.Value == 1);
        stats.TopCities.Should().Contain(kv => kv.Key == "Berlin" && kv.Value == 1);
        stats.PeakHours.Should().NotBeEmpty();

        stats.ErrorRate.Should().Be(50);
        stats.SlowPages.First().Key.Should().Be("/scanner", "it has the highest average load time (300ms)");
        stats.FastestPages.First().Key.Should().Be("/products", "its average load time (75ms) is the lowest");

        stats.MostUsedFeatures.Should().Contain(kv => kv.Key == "Products Management" && kv.Value == 2);
        stats.MostUsedFeatures.Should().Contain(kv => kv.Key == "Scanner" && kv.Value == 1);

        stats.UserRetention.Should().Be(0, "all page views fall in the first half of the window, so nobody returns in the second half");
        stats.NewVsReturningUsers["New Users"].Should().Be(2, "no page views exist before 'from' for either user");
        stats.NewVsReturningUsers["Returning Users"].Should().Be(0);

        stats.TopErrorPages.Should().Contain(kv => kv.Key == "/api/products" && kv.Value == 1);
        stats.ApiEndpointPerformance.Should().Contain(kv => kv.Key == "/api/products" && kv.Value == 150);

        stats.WarehouseActivity.Should().BeEquivalentTo(new Dictionary<string, int> { ["WH1"] = 2, ["WH2"] = 1 });
        stats.RoleActivity.Should().BeEquivalentTo(new Dictionary<string, int> { ["Admin"] = 2, ["User"] = 1 });

        stats.HourlyPageViews.Should().NotBeEmpty();
        stats.HourlyApiRequests.Should().NotBeEmpty();
        stats.HourlyPageViews.Sum(h => h.Count).Should().Be(3);
        stats.HourlyApiRequests.Sum(h => h.Count).Should().Be(2);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_ReturningUser_CountedInUserRetentionAndNewVsReturning()
    {
        var factory = CreateFactory(nameof(GetDashboardStatsAsync_ReturningUser_CountedInUserRetentionAndNewVsReturning));
        var now = DateTime.UtcNow;
        var from = now.AddDays(-4);
        var to = now;

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            // Prior visit, before the window -> makes user 1 "returning".
            db.PageViews.Add(MakePageView(1, 1, "/a", from.AddDays(-1), sessionId: "prior", ipAddress: "10.0.0.9"));
            // First-half visit and second-half visit within the window -> retained.
            db.PageViews.Add(MakePageView(2, 1, "/a", from.AddHours(1), sessionId: "s1", ipAddress: "10.0.0.10"));
            db.PageViews.Add(MakePageView(3, 1, "/a", from.AddDays(3), sessionId: "s2", ipAddress: "10.0.0.11"));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var stats = await sut.GetDashboardStatsAsync(from, to);

        stats.UserRetention.Should().Be(100, "the only first-half visitor also appears in the second half");
        stats.NewVsReturningUsers["Returning Users"].Should().Be(1);
        stats.NewVsReturningUsers["New Users"].Should().Be(0);
    }
}
