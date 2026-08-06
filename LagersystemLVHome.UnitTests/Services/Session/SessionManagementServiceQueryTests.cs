using LagersystemLVHome.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static LagersystemLVHome.UnitTests.Services.Session.SessionManagementServiceTestSupport;

namespace LagersystemLVHome.UnitTests.Services.Session;

/// <summary>
/// Covers the read-only query surface of <see cref="SessionManagementService"/>:
/// GetSessionAsync, both GetActiveSessionsAsync overloads, GetUserSessionsAsync,
/// GetSessionByUserAndFingerprintAsync, GetSuspiciousSessionsAsync,
/// GetSecurityEventsAsync and GetSessionStatisticsAsync.
/// </summary>
public class SessionManagementServiceQueryTests
{
    [Fact]
    public async Task GetSessionAsync_UnknownSessionId_ReturnsNull()
    {
        var factory = CreateFactory(nameof(GetSessionAsync_UnknownSessionId_ReturnsNull));
        var sut = BuildService(factory);

        (await sut.GetSessionAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task GetSessionAsync_KnownSessionId_ReturnsSessionWithIncludes()
    {
        var factory = CreateFactory(nameof(GetSessionAsync_KnownSessionId_ReturnsSessionWithIncludes));
        await SeedWarehouseAndUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1"));
            await db.SaveChangesAsync();
            db.SessionActivities.Add(new SessionActivity { SessionId = 1, ActivityType = "PageView" });
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var session = await sut.GetSessionAsync("s1");

        session.Should().NotBeNull();
        session!.User.Should().NotBeNull();
        session.Warehouse.Should().NotBeNull();
        session.Activities.Should().ContainSingle();
    }

    [Fact]
    public async Task GetActiveSessionsAsync_ExcludesInactiveAndStaleSessions()
    {
        var factory = CreateFactory(nameof(GetActiveSessionsAsync_ExcludesInactiveAndStaleSessions));
        await SeedWarehouseAndUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("active-recent", isActive: true, lastActivity: DateTime.UtcNow),
                MakeSession("active-stale", isActive: true, lastActivity: DateTime.UtcNow.AddMinutes(-40)),
                MakeSession("inactive-recent", isActive: false, lastActivity: DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var sessions = await sut.GetActiveSessionsAsync();

        sessions.Should().ContainSingle().Which.SessionId.Should().Be("active-recent");
    }

    [Fact]
    public async Task GetActiveSessionsAsync_FiltersByWarehouseId()
    {
        var factory = CreateFactory(nameof(GetActiveSessionsAsync_FiltersByWarehouseId));
        await SeedWarehouseAndUserAsync(factory, userId: 1, warehouseId: 1);
        await SeedWarehouseAndUserAsync(factory, userId: 2, warehouseId: 2);
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("wh1", userId: 1, warehouseId: 1),
                MakeSession("wh2", userId: 2, warehouseId: 2));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var sessions = await sut.GetActiveSessionsAsync(warehouseId: 2);

        sessions.Should().ContainSingle().Which.SessionId.Should().Be("wh2");
    }

    [Fact]
    public async Task GetActiveSessionsAsync_OnlyActiveFalse_IncludesInactiveAndStaleSessions()
    {
        var factory = CreateFactory(nameof(GetActiveSessionsAsync_OnlyActiveFalse_IncludesInactiveAndStaleSessions));
        await SeedWarehouseAndUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("active-recent", isActive: true, lastActivity: DateTime.UtcNow),
                MakeSession("active-stale", isActive: true, lastActivity: DateTime.UtcNow.AddMinutes(-90)),
                MakeSession("inactive", isActive: false, lastActivity: DateTime.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var sessions = await sut.GetActiveSessionsAsync(warehouseId: null, onlyActive: false);

        sessions.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetActiveSessionsAsync_OnlyActiveTrue_BehavesLikeOtherOverload()
    {
        var factory = CreateFactory(nameof(GetActiveSessionsAsync_OnlyActiveTrue_BehavesLikeOtherOverload));
        await SeedWarehouseAndUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("active-recent", isActive: true, lastActivity: DateTime.UtcNow),
                MakeSession("active-stale", isActive: true, lastActivity: DateTime.UtcNow.AddMinutes(-90)));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var sessions = await sut.GetActiveSessionsAsync(warehouseId: null, onlyActive: true);

        sessions.Should().ContainSingle().Which.SessionId.Should().Be("active-recent");
    }

    [Fact]
    public async Task GetActiveSessionsAsync_OnlyActiveOverload_FiltersByWarehouseId()
    {
        var factory = CreateFactory(nameof(GetActiveSessionsAsync_OnlyActiveOverload_FiltersByWarehouseId));
        await SeedWarehouseAndUserAsync(factory, userId: 1, warehouseId: 1);
        await SeedWarehouseAndUserAsync(factory, userId: 2, warehouseId: 2);
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("wh1", userId: 1, warehouseId: 1),
                MakeSession("wh2", userId: 2, warehouseId: 2));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var sessions = await sut.GetActiveSessionsAsync(warehouseId: 1, onlyActive: true);

        sessions.Should().ContainSingle().Which.SessionId.Should().Be("wh1");
    }

    [Fact]
    public async Task GetUserSessionsAsync_DefaultOnlyActive_ExcludesInactiveSessions()
    {
        var factory = CreateFactory(nameof(GetUserSessionsAsync_DefaultOnlyActive_ExcludesInactiveSessions));
        await SeedWarehouseAndUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("a", userId: 1, isActive: true, startTime: DateTime.UtcNow.AddMinutes(-2)),
                MakeSession("b", userId: 1, isActive: false, startTime: DateTime.UtcNow.AddMinutes(-1)));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var sessions = await sut.GetUserSessionsAsync(1);

        sessions.Should().ContainSingle().Which.SessionId.Should().Be("a");
    }

    [Fact]
    public async Task GetUserSessionsAsync_OnlyActiveFalse_ReturnsAllOrderedByStartTimeDescending()
    {
        var factory = CreateFactory(nameof(GetUserSessionsAsync_OnlyActiveFalse_ReturnsAllOrderedByStartTimeDescending));
        await SeedWarehouseAndUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("older", userId: 1, isActive: false, startTime: DateTime.UtcNow.AddHours(-2)),
                MakeSession("newer", userId: 1, isActive: true, startTime: DateTime.UtcNow.AddMinutes(-1)));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var sessions = await sut.GetUserSessionsAsync(1, onlyActive: false);

        sessions.Should().HaveCount(2);
        sessions[0].SessionId.Should().Be("newer");
        sessions[1].SessionId.Should().Be("older");
    }

    [Fact]
    public async Task GetSessionByUserAndFingerprintAsync_ExactFingerprintMatch_ReturnsIt()
    {
        var factory = CreateFactory(nameof(GetSessionByUserAndFingerprintAsync_ExactFingerprintMatch_ReturnsIt));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("fp-match", userId: 1, deviceFingerprint: "fp-1"),
                MakeSession("other", userId: 1, deviceFingerprint: "fp-2"));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var session = await sut.GetSessionByUserAndFingerprintAsync(1, "fp-1");

        session.Should().NotBeNull();
        session!.SessionId.Should().Be("fp-match");
    }

    [Fact]
    public async Task GetSessionByUserAndFingerprintAsync_NoFingerprintMatch_FallsBackToUserAgentMatch()
    {
        var factory = CreateFactory(nameof(GetSessionByUserAndFingerprintAsync_NoFingerprintMatch_FallsBackToUserAgentMatch));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("ua-match", userId: 1, deviceFingerprint: "old-fp", userAgent: "Chrome/120"));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["User-Agent"] = "Chrome/120";
        var sut = BuildService(factory, AccessorFor(ctx));

        var session = await sut.GetSessionByUserAndFingerprintAsync(1, deviceFingerprint: "unknown-fp");

        session.Should().NotBeNull();
        session!.SessionId.Should().Be("ua-match");
    }

    [Fact]
    public async Task GetSessionByUserAndFingerprintAsync_NoFingerprintNoUaMatch_FallsBackToMostRecentSession()
    {
        var factory = CreateFactory(nameof(GetSessionByUserAndFingerprintAsync_NoFingerprintNoUaMatch_FallsBackToMostRecentSession));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("older", userId: 1, userAgent: "Firefox/1", lastActivity: DateTime.UtcNow.AddMinutes(-10)),
                MakeSession("newest", userId: 1, userAgent: "Firefox/2", lastActivity: DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory); // no HttpContext -> no UA to match against

        var session = await sut.GetSessionByUserAndFingerprintAsync(1, deviceFingerprint: null);

        session.Should().NotBeNull();
        session!.SessionId.Should().Be("newest");
    }

    [Fact]
    public async Task GetSessionByUserAndFingerprintAsync_ExcludesApiSessions()
    {
        var factory = CreateFactory(nameof(GetSessionByUserAndFingerprintAsync_ExcludesApiSessions));
        await using (var db = factory.CreateDbContext())
        {
            var apiSession = MakeSession("api-1", userId: 1, deviceType: "API");
            db.UserSessions.Add(apiSession);
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var session = await sut.GetSessionByUserAndFingerprintAsync(1, deviceFingerprint: null);

        session.Should().BeNull();
    }

    [Fact]
    public async Task GetSessionByUserAndFingerprintAsync_OnlyActiveFalse_IncludesInactiveSessions()
    {
        var factory = CreateFactory(nameof(GetSessionByUserAndFingerprintAsync_OnlyActiveFalse_IncludesInactiveSessions));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("inactive-fp", userId: 1, isActive: false, deviceFingerprint: "fp-x"));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var session = await sut.GetSessionByUserAndFingerprintAsync(1, "fp-x", onlyActive: false);

        session.Should().NotBeNull();
        session!.SessionId.Should().Be("inactive-fp");
    }

    [Fact]
    public async Task GetSuspiciousSessionsAsync_FiltersByIsSuspiciousAndWarehouse()
    {
        var factory = CreateFactory(nameof(GetSuspiciousSessionsAsync_FiltersByIsSuspiciousAndWarehouse));
        await SeedWarehouseAndUserAsync(factory, userId: 1, warehouseId: 1);
        await SeedWarehouseAndUserAsync(factory, userId: 2, warehouseId: 2);
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("susp-wh1", userId: 1, warehouseId: 1, isSuspicious: true, lastSuspiciousActivity: DateTime.UtcNow),
                MakeSession("not-susp", userId: 1, warehouseId: 1, isSuspicious: false),
                MakeSession("susp-wh2", userId: 2, warehouseId: 2, isSuspicious: true, lastSuspiciousActivity: DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var sessions = await sut.GetSuspiciousSessionsAsync(warehouseId: 1);

        sessions.Should().ContainSingle().Which.SessionId.Should().Be("susp-wh1");
    }

    [Fact]
    public async Task GetSuspiciousSessionsAsync_OrdersByLastSuspiciousActivityDescending()
    {
        var factory = CreateFactory(nameof(GetSuspiciousSessionsAsync_OrdersByLastSuspiciousActivityDescending));
        await SeedWarehouseAndUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("old", isSuspicious: true, lastSuspiciousActivity: DateTime.UtcNow.AddHours(-1)),
                MakeSession("new", isSuspicious: true, lastSuspiciousActivity: DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var sessions = await sut.GetSuspiciousSessionsAsync();

        sessions.Should().HaveCount(2);
        sessions[0].SessionId.Should().Be("new");
    }

    [Fact]
    public async Task GetSecurityEventsAsync_OrdersByTimestampDescendingAndRespectsCount()
    {
        var factory = CreateFactory(nameof(GetSecurityEventsAsync_OrdersByTimestampDescendingAndRespectsCount));
        await using (var db = factory.CreateDbContext())
        {
            db.SecurityEvents.AddRange(
                new SecurityEvent { EventType = "E1", Timestamp = DateTime.UtcNow.AddMinutes(-10) },
                new SecurityEvent { EventType = "E2", Timestamp = DateTime.UtcNow.AddMinutes(-5) },
                new SecurityEvent { EventType = "E3", Timestamp = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var events = await sut.GetSecurityEventsAsync(count: 2);

        events.Should().HaveCount(2);
        events[0].EventType.Should().Be("E3");
        events[1].EventType.Should().Be("E2");
    }

    [Fact]
    public async Task GetSecurityEventsAsync_FiltersByWarehouseIdViaSession()
    {
        var factory = CreateFactory(nameof(GetSecurityEventsAsync_FiltersByWarehouseIdViaSession));
        await SeedWarehouseAndUserAsync(factory, userId: 1, warehouseId: 1);
        await SeedWarehouseAndUserAsync(factory, userId: 2, warehouseId: 2);
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("s-wh1", userId: 1, warehouseId: 1),
                MakeSession("s-wh2", userId: 2, warehouseId: 2));
            await db.SaveChangesAsync();

            var s1 = await db.UserSessions.SingleAsync(s => s.SessionId == "s-wh1");
            var s2 = await db.UserSessions.SingleAsync(s => s.SessionId == "s-wh2");
            db.SecurityEvents.AddRange(
                new SecurityEvent { EventType = "WH1_EVENT", SessionId = s1.Id, Timestamp = DateTime.UtcNow },
                new SecurityEvent { EventType = "WH2_EVENT", SessionId = s2.Id, Timestamp = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var events = await sut.GetSecurityEventsAsync(warehouseId: 1);

        events.Should().ContainSingle().Which.EventType.Should().Be("WH1_EVENT");
    }

    [Fact]
    public async Task GetSessionStatisticsAsync_EmptyDatabase_ReturnsZeroedStatistics()
    {
        var factory = CreateFactory(nameof(GetSessionStatisticsAsync_EmptyDatabase_ReturnsZeroedStatistics));
        var sut = BuildService(factory);

        var stats = await sut.GetSessionStatisticsAsync();

        stats.TotalSessions.Should().Be(0);
        stats.AverageSessionDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task GetSessionStatisticsAsync_ComputesAggregatesAcrossSessions()
    {
        var factory = CreateFactory(nameof(GetSessionStatisticsAsync_ComputesAggregatesAcrossSessions));
        var start = DateTime.UtcNow.AddHours(-1);
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                new Domain.Models.UserSession
                {
                    SessionId = "s1",
                    UserId = 1,
                    Username = "u1",
                    WarehouseId = 1,
                    StartTime = start,
                    EndTime = start.AddMinutes(10),
                    IsActive = false,
                    Country = "Germany",
                    DeviceType = "Desktop",
                    RiskLevel = SessionRiskLevel.Low,
                    PageViewsCount = 3,
                    ApiRequestsCount = 1
                },
                new Domain.Models.UserSession
                {
                    SessionId = "s2",
                    UserId = 2,
                    Username = "u2",
                    WarehouseId = 1,
                    StartTime = start,
                    EndTime = start.AddMinutes(20),
                    IsActive = false,
                    Country = "Germany",
                    DeviceType = "Mobile",
                    RiskLevel = SessionRiskLevel.High,
                    IsSuspicious = true,
                    IsVpn = true,
                    IsConcurrent = true,
                    WasForcedLogout = true,
                    PageViewsCount = 2,
                    ApiRequestsCount = 0
                },
                new Domain.Models.UserSession
                {
                    SessionId = "s3",
                    UserId = 3,
                    Username = "u3",
                    WarehouseId = 1,
                    StartTime = DateTime.UtcNow,
                    IsActive = true,
                    Country = "France",
                    DeviceType = "Desktop",
                    RiskLevel = SessionRiskLevel.Low
                });
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var stats = await sut.GetSessionStatisticsAsync(warehouseId: 1);

        stats.TotalSessions.Should().Be(3);
        stats.ActiveSessions.Should().Be(1);
        stats.SuspiciousSessions.Should().Be(1);
        stats.VpnSessions.Should().Be(1);
        stats.ConcurrentSessions.Should().Be(1);
        stats.ForcedLogouts.Should().Be(1);
        stats.AverageSessionDuration.Should().Be(TimeSpan.FromMinutes(15)); // (10+20)/2
        stats.TotalPageViews.Should().Be(5);
        stats.TotalApiRequests.Should().Be(1);
        stats.TopCountries.Should().ContainSingle(kvp => kvp.Key == "Germany" && kvp.Value == 2);
        stats.DeviceTypes["Desktop"].Should().Be(2);
        stats.DeviceTypes["Mobile"].Should().Be(1);
        stats.RiskLevelDistribution[SessionRiskLevel.Low.ToString()].Should().Be(2);
        stats.RiskLevelDistribution[SessionRiskLevel.High.ToString()].Should().Be(1);
    }

    [Fact]
    public async Task GetSessionStatisticsAsync_FiltersByFromAndTo()
    {
        var factory = CreateFactory(nameof(GetSessionStatisticsAsync_FiltersByFromAndTo));
        var inWindow = DateTime.UtcNow.AddDays(-1);
        var outOfWindow = DateTime.UtcNow.AddDays(-10);
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                new Domain.Models.UserSession { SessionId = "in", UserId = 1, Username = "u1", WarehouseId = 1, StartTime = inWindow },
                new Domain.Models.UserSession { SessionId = "out", UserId = 1, Username = "u1", WarehouseId = 1, StartTime = outOfWindow });
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var stats = await sut.GetSessionStatisticsAsync(
            from: DateTime.UtcNow.AddDays(-2), to: DateTime.UtcNow);

        stats.TotalSessions.Should().Be(1);
    }
}
