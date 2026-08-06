using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static LagersystemLVHome.UnitTests.Services.Session.SessionManagementServiceTestSupport;

namespace LagersystemLVHome.UnitTests.Services.Session;

/// <summary>
/// Covers <see cref="SessionManagementService.CreateSessionAsync"/>: the client-IP
/// fallback chain, user-agent parsing (device/browser/OS), VPN/risk assignment and
/// concurrent-session bookkeeping.
/// </summary>
public class SessionManagementServiceCreateTests
{
    [Fact]
    public async Task CreateSessionAsync_UnknownUser_ThrowsArgumentException()
    {
        var factory = CreateFactory(nameof(CreateSessionAsync_UnknownUser_ThrowsArgumentException));
        var sut = BuildService(factory);

        var act = () => sut.CreateSessionAsync(userId: 999, warehouseId: 1, "8.8.8.8", "Mozilla/5.0");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("User not found");
    }

    [Fact]
    public async Task CreateSessionAsync_WithExplicitPublicIp_UsesItDirectly()
    {
        var factory = CreateFactory(nameof(CreateSessionAsync_WithExplicitPublicIp_UsesItDirectly));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var session = await sut.CreateSessionAsync(1, warehouseId: 1, "8.8.8.8", "Mozilla/5.0 (Windows NT 10.0)");

        session.IpAddress.Should().Be("8.8.8.8");
        session.UserId.Should().Be(1);
        session.Username.Should().Be("u1");
        session.SessionId.Should().NotBeNullOrEmpty();
        session.IsActive.Should().BeTrue();
        session.Country.Should().Be("Unknown"); // public IP, no external geo API configured
        session.IsConcurrent.Should().BeFalse();
        session.ConcurrentSessionCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateSessionAsync_LoopbackIp_FallsBackToXForwardedForHeader()
    {
        var factory = CreateFactory(nameof(CreateSessionAsync_LoopbackIp_FallsBackToXForwardedForHeader));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.5, 10.0.0.1";
        var sut = BuildService(factory, AccessorFor(ctx));

        var session = await sut.CreateSessionAsync(1, 1, "127.0.0.1", "Mozilla/5.0");

        session.IpAddress.Should().Be("203.0.113.5");
    }

    [Fact]
    public async Task CreateSessionAsync_LoopbackIp_NoXff_FallsBackToXRealIpHeader()
    {
        var factory = CreateFactory(nameof(CreateSessionAsync_LoopbackIp_NoXff_FallsBackToXRealIpHeader));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Real-IP"] = "198.51.100.7";
        var sut = BuildService(factory, AccessorFor(ctx));

        var session = await sut.CreateSessionAsync(1, 1, "::1", "Mozilla/5.0");

        session.IpAddress.Should().Be("198.51.100.7");
    }

    [Fact]
    public async Task CreateSessionAsync_LoopbackIp_NoXffNoXri_FallsBackToXOriginalForHeader()
    {
        var factory = CreateFactory(nameof(CreateSessionAsync_LoopbackIp_NoXffNoXri_FallsBackToXOriginalForHeader));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Original-For"] = "192.0.2.9";
        var sut = BuildService(factory, AccessorFor(ctx));

        var session = await sut.CreateSessionAsync(1, 1, "127.0.0.1", "Mozilla/5.0");

        session.IpAddress.Should().Be("192.0.2.9");
    }

    [Fact]
    public async Task CreateSessionAsync_LoopbackIp_NoHeaders_FallsBackToRemoteIpAddress()
    {
        var factory = CreateFactory(nameof(CreateSessionAsync_LoopbackIp_NoHeaders_FallsBackToRemoteIpAddress));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("172.16.5.9");
        var sut = BuildService(factory, AccessorFor(ctx));

        var session = await sut.CreateSessionAsync(1, 1, "127.0.0.1", "Mozilla/5.0");

        session.IpAddress.Should().Be("172.16.5.9");
    }

    [Fact]
    public async Task CreateSessionAsync_LoopbackIp_NothingAvailable_FallsBackToUnknown()
    {
        var factory = CreateFactory(nameof(CreateSessionAsync_LoopbackIp_NothingAvailable_FallsBackToUnknown));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        // No HttpContext at all -> every fallback returns null.
        var sut = BuildService(factory);

        var session = await sut.CreateSessionAsync(1, 1, "", "Mozilla/5.0");

        session.IpAddress.Should().Be("Unknown");
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Linux; Android 13)", "Mobile")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0)", "Mobile")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0)", "Tablet")]
    [InlineData("Mozilla/5.0 (Linux; Tablet)", "Tablet")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64)", "Desktop")]
    public async Task CreateSessionAsync_DetectsDeviceTypeFromUserAgent(string userAgent, string expectedDeviceType)
    {
        var factory = CreateFactory(nameof(CreateSessionAsync_DetectsDeviceTypeFromUserAgent) + expectedDeviceType + userAgent.GetHashCode());
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var session = await sut.CreateSessionAsync(1, 1, "8.8.8.8", userAgent);

        session.DeviceType.Should().Be(expectedDeviceType);
        session.DeviceInfo.Should().Be(expectedDeviceType);
    }

    [Theory]
    [InlineData("Mozilla/5.0 Edg/120.0", "Edge")]
    [InlineData("Mozilla/5.0 Chrome/120.0", "Chrome")]
    [InlineData("Mozilla/5.0 Firefox/120.0", "Firefox")]
    [InlineData("Mozilla/5.0 Safari/605.1", "Safari")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) Opera/89.0", "Opera")]
    [InlineData("SomeCustomBot/1.0", "Unknown")]
    public async Task CreateSessionAsync_DetectsBrowserFromUserAgent(string userAgent, string expectedBrowser)
    {
        var factory = CreateFactory(nameof(CreateSessionAsync_DetectsBrowserFromUserAgent) + expectedBrowser);
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var session = await sut.CreateSessionAsync(1, 1, "8.8.8.8", userAgent);

        session.Browser.Should().Be(expectedBrowser);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0)", "Windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X)", "macOS")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64)", "Linux")]
    [InlineData("Mozilla/5.0 (Android 13; Mobile)", "Android")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0)", "iOS")]
    [InlineData("SomeCustomBot/1.0", "Unknown")]
    public async Task CreateSessionAsync_DetectsOperatingSystemFromUserAgent(string userAgent, string expectedOs)
    {
        var factory = CreateFactory(nameof(CreateSessionAsync_DetectsOperatingSystemFromUserAgent) + expectedOs);
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var session = await sut.CreateSessionAsync(1, 1, "8.8.8.8", userAgent);

        session.OperatingSystem.Should().Be(expectedOs);
    }

    [Fact]
    public async Task CreateSessionAsync_PrivateIpMatchingVpnSubnet_MarksVpnAndLogsSecurityEvent()
    {
        var factory = CreateFactory(nameof(CreateSessionAsync_PrivateIpMatchingVpnSubnet_MarksVpnAndLogsSecurityEvent));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var vpnConfig = new VpnDetectionConfig { VpnSubnets = ["192.168.3.*"], SubnetMatchConfidence = 95 };
        var sut = BuildService(factory, vpnConfig: vpnConfig);

        var session = await sut.CreateSessionAsync(1, 1, "192.168.3.45", "Mozilla/5.0");

        session.IsVpn.Should().BeTrue();
        session.VpnConfidenceScore.Should().Be(95);
        session.Country.Should().Be("Local Network");
        session.RiskFactors.Should().Contain("IP matches configured VPN subnet");

        await using var db2 = factory.CreateDbContext();
        var securityEvent = await db2.SecurityEvents.SingleAsync();
        securityEvent.EventType.Should().Be("VPN_DETECTED");
        securityEvent.IsVpn.Should().BeTrue();
        securityEvent.SessionId.Should().Be(session.Id);
    }

    [Fact]
    public async Task CreateSessionAsync_VpnConfidenceInSeventiesRange_AddsMidTierRiskScore()
    {
        // Exercises the 70-89 confidence-score tier of CalculateRiskLevel (distinct from the
        // >=90 tier already covered above). Final level is still Low since the max score
        // achievable through the config-based VPN detector never reaches the Medium/High
        // thresholds (see final report for details).
        var factory = CreateFactory(nameof(CreateSessionAsync_VpnConfidenceInSeventiesRange_AddsMidTierRiskScore));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var vpnConfig = new VpnDetectionConfig { VpnSubnets = ["192.168.3.*"], SubnetMatchConfidence = 75 };
        var sut = BuildService(factory, vpnConfig: vpnConfig);

        var session = await sut.CreateSessionAsync(1, 1, "192.168.3.45", "Mozilla/5.0");

        session.IsVpn.Should().BeTrue();
        session.VpnConfidenceScore.Should().Be(75);
        session.RiskLevel.Should().Be(SessionRiskLevel.Low);
    }

    [Fact]
    public async Task CreateSessionAsync_VpnConfidenceInFiftiesRange_AddsLowTierRiskScore()
    {
        // Exercises the 50-69 confidence-score tier of CalculateRiskLevel.
        var factory = CreateFactory(nameof(CreateSessionAsync_VpnConfidenceInFiftiesRange_AddsLowTierRiskScore));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var vpnConfig = new VpnDetectionConfig { VpnSubnets = ["192.168.3.*"], SubnetMatchConfidence = 55 };
        var sut = BuildService(factory, vpnConfig: vpnConfig);

        var session = await sut.CreateSessionAsync(1, 1, "192.168.3.45", "Mozilla/5.0");

        session.IsVpn.Should().BeTrue();
        session.VpnConfidenceScore.Should().Be(55);
        session.RiskLevel.Should().Be(SessionRiskLevel.Low);
    }

    [Fact]
    public async Task CreateSessionAsync_PrivateIpNotMatchingVpnSubnet_DoesNotLogSecurityEvent()
    {
        var factory = CreateFactory(nameof(CreateSessionAsync_PrivateIpNotMatchingVpnSubnet_DoesNotLogSecurityEvent));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory, vpnConfig: new VpnDetectionConfig { VpnSubnets = ["10.99.0.*"] });

        var session = await sut.CreateSessionAsync(1, 1, "192.168.3.45", "Mozilla/5.0");

        session.IsVpn.Should().BeFalse();
        session.Country.Should().Be("Local Network");

        await using var db2 = factory.CreateDbContext();
        (await db2.SecurityEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateSessionAsync_ExistingActiveSessionForUser_MarksNewSessionAsConcurrent()
    {
        var factory = CreateFactory(nameof(CreateSessionAsync_ExistingActiveSessionForUser_MarksNewSessionAsConcurrent));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(MakeSession("existing", userId: 1));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var session = await sut.CreateSessionAsync(1, 1, "8.8.8.8", "Mozilla/5.0");

        session.IsConcurrent.Should().BeTrue();
        session.ConcurrentSessionCount.Should().Be(2);
    }

    [Fact]
    public async Task CreateSessionAsync_LocalhostIp_SetsCountryLocalhost()
    {
        var factory = CreateFactory(nameof(CreateSessionAsync_LocalhostIp_SetsCountryLocalhost));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
        var sut = BuildService(factory, AccessorFor(ctx));

        // Explicit "127.0.0.1" param IP bypasses the fallback chain entirely
        // and is passed straight into VPN/geo detection.
        var session = await sut.CreateSessionAsync(1, 1, "127.0.0.1", "Mozilla/5.0");

        session.Country.Should().Be("Localhost");
        session.IsVpn.Should().BeFalse();
    }
}
