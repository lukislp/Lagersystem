using LagersystemLVHome.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static LagersystemLVHome.UnitTests.Services.Session.SessionManagementServiceTestSupport;

namespace LagersystemLVHome.UnitTests.Services.Session;

/// <summary>
/// Covers session lifecycle mutations: activity/fingerprint updates, ending
/// sessions, forced logouts, concurrent-login checks and expired-session cleanup.
/// </summary>
public class SessionManagementServiceLifecycleTests
{
    [Fact]
    public async Task UpdateSessionActivityAsync_UnknownSessionId_IsNoOp()
    {
        var factory = CreateFactory(nameof(UpdateSessionActivityAsync_UnknownSessionId_IsNoOp));
        var sut = BuildService(factory);

        await sut.UpdateSessionActivityAsync("missing"); // should not throw

        (await factory.CreateDbContext().SessionActivities.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateSessionActivityAsync_NoHttpContext_IncrementsPageViewsAndRecordsActivity()
    {
        var factory = CreateFactory(nameof(UpdateSessionActivityAsync_NoHttpContext_IncrementsPageViewsAndRecordsActivity));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1"));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        await sut.UpdateSessionActivityAsync("s1", pageUrl: "/inventory");

        await using var verify = factory.CreateDbContext();
        var session = await verify.UserSessions.SingleAsync();
        session.PageViewsCount.Should().Be(1);
        session.LastPageUrl.Should().Be("/inventory");
        var activity = await verify.SessionActivities.SingleAsync();
        activity.ActivityType.Should().Be("PageView");
        activity.PageUrl.Should().Be("/inventory");
    }

    [Fact]
    public async Task UpdateSessionActivityAsync_IpChanged_UpdatesIpAddressAndLogs()
    {
        var factory = CreateFactory(nameof(UpdateSessionActivityAsync_IpChanged_UpdatesIpAddressAndLogs));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", ipAddress: "1.1.1.1"));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("2.2.2.2");
        var sut = BuildService(factory, AccessorFor(ctx));

        await sut.UpdateSessionActivityAsync("s1");

        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.IpAddress.Should().Be("2.2.2.2");
    }

    [Fact]
    public async Task UpdateSessionActivityAsync_UsesXForwardedForHeaderWhenPresent()
    {
        var factory = CreateFactory(nameof(UpdateSessionActivityAsync_UsesXForwardedForHeaderWhenPresent));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", ipAddress: "1.1.1.1"));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("5.5.5.5"); // would apply only without XFF
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.9, 10.0.0.1";
        var sut = BuildService(factory, AccessorFor(ctx));

        await sut.UpdateSessionActivityAsync("s1");

        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.IpAddress.Should().Be("203.0.113.9");
    }

    [Fact]
    public async Task UpdateSessionActivityAsync_IpUnchanged_DoesNotTriggerGeoLookup()
    {
        var factory = CreateFactory(nameof(UpdateSessionActivityAsync_IpUnchanged_DoesNotTriggerGeoLookup));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", ipAddress: "2.2.2.2", country: "OriginalCountry"));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("2.2.2.2");
        var services = new ServiceCollection();
        var geo = Substitute.For<IGeoLocationService>();
        geo.IsAvailable.Returns(true);
        services.AddSingleton(geo);
        ctx.RequestServices = services.BuildServiceProvider();
        var sut = BuildService(factory, AccessorFor(ctx));

        await sut.UpdateSessionActivityAsync("s1");

        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.Country.Should().Be("OriginalCountry"); // unchanged - IP didn't change
    }

    [Fact]
    public async Task UpdateSessionActivityAsync_LoopbackOrUnknownCurrentIp_DoesNotUpdateIp()
    {
        var factory = CreateFactory(nameof(UpdateSessionActivityAsync_LoopbackOrUnknownCurrentIp_DoesNotUpdateIp));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", ipAddress: "9.9.9.9"));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("::1");
        var sut = BuildService(factory, AccessorFor(ctx));

        await sut.UpdateSessionActivityAsync("s1");

        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.IpAddress.Should().Be("9.9.9.9");
    }

    [Fact]
    public async Task UpdateSessionActivityAsync_GeoServiceAvailable_UpdatesGeoAndRiskLevel()
    {
        var factory = CreateFactory(nameof(UpdateSessionActivityAsync_GeoServiceAvailable_UpdatesGeoAndRiskLevel));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", ipAddress: "1.1.1.1", country: "OldCountry"));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("8.8.4.4"); // public IP -> DetectVpnAsync returns Country="Unknown"
        var services = new ServiceCollection();
        var geo = Substitute.For<IGeoLocationService>();
        geo.IsAvailable.Returns(true);
        services.AddSingleton(geo);
        ctx.RequestServices = services.BuildServiceProvider();
        var sut = BuildService(factory, AccessorFor(ctx));

        await sut.UpdateSessionActivityAsync("s1");

        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.IpAddress.Should().Be("8.8.4.4");
        session.Country.Should().Be("Unknown");
    }

    [Fact]
    public async Task UpdateSessionActivityAsync_GeoServiceUnavailable_SkipsGeoUpdate()
    {
        var factory = CreateFactory(nameof(UpdateSessionActivityAsync_GeoServiceUnavailable_SkipsGeoUpdate));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", ipAddress: "1.1.1.1", country: "OldCountry"));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("8.8.4.4");
        var services = new ServiceCollection();
        var geo = Substitute.For<IGeoLocationService>();
        geo.IsAvailable.Returns(false);
        services.AddSingleton(geo);
        ctx.RequestServices = services.BuildServiceProvider();
        var sut = BuildService(factory, AccessorFor(ctx));

        await sut.UpdateSessionActivityAsync("s1");

        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.IpAddress.Should().Be("8.8.4.4"); // IP still updates
        session.Country.Should().Be("OldCountry"); // but geo lookup is skipped
    }

    [Fact]
    public async Task UpdateSessionActivityAsync_GeoServiceThrows_IsCaughtAndDoesNotFailUpdate()
    {
        var factory = CreateFactory(nameof(UpdateSessionActivityAsync_GeoServiceThrows_IsCaughtAndDoesNotFailUpdate));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", ipAddress: "1.1.1.1"));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("8.8.4.4");
        var services = new ServiceCollection();
        var geo = Substitute.For<IGeoLocationService>();
        geo.IsAvailable.Returns(_ => throw new InvalidOperationException("boom"));
        services.AddSingleton(geo);
        ctx.RequestServices = services.BuildServiceProvider();
        var sut = BuildService(factory, AccessorFor(ctx));

        await sut.UpdateSessionActivityAsync("s1"); // must not throw

        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.IpAddress.Should().Be("8.8.4.4"); // IP change itself already happened before the throw
    }

    [Fact]
    public async Task UpdateSessionActivityAsync_UserAgentChangedAndLongEnough_UpdatesUserAgent()
    {
        var factory = CreateFactory(nameof(UpdateSessionActivityAsync_UserAgentChangedAndLongEnough_UpdatesUserAgent));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", userAgent: "OldAgent/1.0"));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["User-Agent"] = "Mozilla/5.0 (New Agent)";
        var sut = BuildService(factory, AccessorFor(ctx));

        await sut.UpdateSessionActivityAsync("s1");

        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.UserAgent.Should().Be("Mozilla/5.0 (New Agent)");
    }

    [Fact]
    public async Task UpdateSessionActivityAsync_UserAgentTooShort_IsNotUpdated()
    {
        var factory = CreateFactory(nameof(UpdateSessionActivityAsync_UserAgentTooShort_IsNotUpdated));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", userAgent: "OriginalAgent/1.0"));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["User-Agent"] = "short"; // length <= 10
        var sut = BuildService(factory, AccessorFor(ctx));

        await sut.UpdateSessionActivityAsync("s1");

        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.UserAgent.Should().Be("OriginalAgent/1.0");
    }

    [Fact]
    public async Task UpdateSessionFingerprintAsync_EmptySessionId_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(UpdateSessionFingerprintAsync_EmptySessionId_ReturnsFalse));
        var sut = BuildService(factory);

        (await sut.UpdateSessionFingerprintAsync("", "fp")).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateSessionFingerprintAsync_EmptyFingerprint_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(UpdateSessionFingerprintAsync_EmptyFingerprint_ReturnsFalse));
        var sut = BuildService(factory);

        (await sut.UpdateSessionFingerprintAsync("s1", "  ")).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateSessionFingerprintAsync_UnknownSession_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(UpdateSessionFingerprintAsync_UnknownSession_ReturnsFalse));
        var sut = BuildService(factory);

        (await sut.UpdateSessionFingerprintAsync("missing", "fp")).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateSessionFingerprintAsync_KnownActiveSession_UpdatesFingerprintReturnsTrue()
    {
        var factory = CreateFactory(nameof(UpdateSessionFingerprintAsync_KnownActiveSession_UpdatesFingerprintReturnsTrue));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", deviceFingerprint: null));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var result = await sut.UpdateSessionFingerprintAsync("s1", "new-fp");

        result.Should().BeTrue();
        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.DeviceFingerprint.Should().Be("new-fp");
    }

    [Fact]
    public async Task EndSessionAsync_UnknownSession_IsNoOp()
    {
        var factory = CreateFactory(nameof(EndSessionAsync_UnknownSession_IsNoOp));
        var sut = BuildService(factory);

        await sut.EndSessionAsync("missing", SessionEndReason.UserLogout); // should not throw
    }

    [Fact]
    public async Task EndSessionAsync_KnownSession_SetsTerminationFields()
    {
        var factory = CreateFactory(nameof(EndSessionAsync_KnownSession_SetsTerminationFields));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1"));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        await sut.EndSessionAsync("s1", SessionEndReason.UserLogout, details: "manual logout");

        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.IsActive.Should().BeFalse();
        session.EndReason.Should().Be(SessionEndReason.UserLogout);
        session.EndReasonDetails.Should().Be("manual logout");
        session.EndTime.Should().NotBeNull();
        session.WasForcedLogout.Should().BeFalse();
        session.TerminatedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task EndSessionAsync_WithTerminatedByUserId_MarksAsForcedLogout()
    {
        var factory = CreateFactory(nameof(EndSessionAsync_WithTerminatedByUserId_MarksAsForcedLogout));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1"));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        await sut.EndSessionAsync("s1", SessionEndReason.AdminForceLogout, terminatedByUserId: 42);

        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.WasForcedLogout.Should().BeTrue();
        session.TerminatedByUserId.Should().Be(42);
    }

    [Fact]
    public async Task ForceLogoutAsync_UnknownSession_IsNoOp()
    {
        var factory = CreateFactory(nameof(ForceLogoutAsync_UnknownSession_IsNoOp));
        var sut = BuildService(factory);

        await sut.ForceLogoutAsync("missing", adminUserId: 1, reason: "test");

        (await factory.CreateDbContext().SecurityEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ForceLogoutAsync_KnownSession_EndsSessionAndLogsSecurityEvent()
    {
        var factory = CreateFactory(nameof(ForceLogoutAsync_KnownSession_EndsSessionAndLogsSecurityEvent));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1"));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        await sut.ForceLogoutAsync("s1", adminUserId: 7, reason: "policy violation");

        await using var verify = factory.CreateDbContext();
        var session = await verify.UserSessions.SingleAsync();
        session.IsActive.Should().BeFalse();
        session.EndReason.Should().Be(SessionEndReason.AdminForceLogout);
        session.WasForcedLogout.Should().BeTrue();
        session.TerminatedByUserId.Should().Be(7);

        var securityEvent = await verify.SecurityEvents.SingleAsync();
        securityEvent.EventType.Should().Be("ADMIN_FORCE_LOGOUT");
        securityEvent.UserId.Should().Be(session.UserId);
    }

    [Fact]
    public async Task ForceLogoutUserAsync_MultipleActiveSessions_LogsOutAllOfThem()
    {
        var factory = CreateFactory(nameof(ForceLogoutUserAsync_MultipleActiveSessions_LogsOutAllOfThem));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("s1", userId: 1),
                MakeSession("s2", userId: 1),
                MakeSession("other-user", userId: 2));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        await sut.ForceLogoutUserAsync(userId: 1, adminUserId: 9, reason: "bulk logout");

        await using var verify = factory.CreateDbContext();
        (await verify.UserSessions.Where(s => s.UserId == 1).AllAsync(s => !s.IsActive)).Should().BeTrue();
        (await verify.UserSessions.SingleAsync(s => s.SessionId == "other-user")).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ForceLogoutUserAsync_NoActiveSessions_IsNoOp()
    {
        var factory = CreateFactory(nameof(ForceLogoutUserAsync_NoActiveSessions_IsNoOp));
        var sut = BuildService(factory);

        await sut.ForceLogoutUserAsync(userId: 1, adminUserId: 9, reason: "n/a"); // should not throw
    }

    [Fact]
    public async Task CheckConcurrentLoginAsync_NoOtherActiveSessions_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(CheckConcurrentLoginAsync_NoOtherActiveSessions_ReturnsFalse));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", userId: 1));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        (await sut.CheckConcurrentLoginAsync(1, "s1")).Should().BeFalse();
    }

    [Fact]
    public async Task CheckConcurrentLoginAsync_OtherActiveSessionsExist_ReturnsTrue()
    {
        var factory = CreateFactory(nameof(CheckConcurrentLoginAsync_OtherActiveSessionsExist_ReturnsTrue));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(MakeSession("s1", userId: 1), MakeSession("s2", userId: 1));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        (await sut.CheckConcurrentLoginAsync(1, "s2")).Should().BeTrue();
    }

    [Fact]
    public async Task TerminatePreviousSessionsAsync_EndsOtherActiveSessionsForUser_KeepsCurrentActive()
    {
        var factory = CreateFactory(nameof(TerminatePreviousSessionsAsync_EndsOtherActiveSessionsForUser_KeepsCurrentActive));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("old1", userId: 1),
                MakeSession("old2", userId: 1),
                MakeSession("current", userId: 1));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        await sut.TerminatePreviousSessionsAsync(1, "current");

        await using var verify = factory.CreateDbContext();
        (await verify.UserSessions.SingleAsync(s => s.SessionId == "current")).IsActive.Should().BeTrue();
        (await verify.UserSessions.SingleAsync(s => s.SessionId == "old1")).EndReason.Should().Be(SessionEndReason.ConcurrentLogin);
        (await verify.UserSessions.SingleAsync(s => s.SessionId == "old2")).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task CleanupExpiredSessionsAsync_NoExpiredSessions_IsNoOp()
    {
        var factory = CreateFactory(nameof(CleanupExpiredSessionsAsync_NoExpiredSessions_IsNoOp));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", lastActivity: DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        await sut.CleanupExpiredSessionsAsync();

        (await factory.CreateDbContext().UserSessions.SingleAsync()).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CleanupExpiredSessionsAsync_StaleActiveSessions_AreEndedWithTimeoutReason()
    {
        var factory = CreateFactory(nameof(CleanupExpiredSessionsAsync_StaleActiveSessions_AreEndedWithTimeoutReason));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.AddRange(
                MakeSession("expired", lastActivity: DateTime.UtcNow.AddMinutes(-45)),
                MakeSession("fresh", lastActivity: DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        await sut.CleanupExpiredSessionsAsync();

        await using var verify = factory.CreateDbContext();
        var expired = await verify.UserSessions.SingleAsync(s => s.SessionId == "expired");
        expired.IsActive.Should().BeFalse();
        expired.EndReason.Should().Be(SessionEndReason.Timeout);
        expired.EndTime.Should().NotBeNull();

        var fresh = await verify.UserSessions.SingleAsync(s => s.SessionId == "fresh");
        fresh.IsActive.Should().BeTrue();
    }
}
