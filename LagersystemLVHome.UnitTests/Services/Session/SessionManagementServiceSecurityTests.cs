using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using static LagersystemLVHome.UnitTests.Services.Session.SessionManagementServiceTestSupport;

namespace LagersystemLVHome.UnitTests.Services.Session;

/// <summary>
/// Covers session-hijacking detection, suspicious-activity marking and the
/// configuration-based VPN detector (<see cref="SessionManagementService.DetectVpnAsync"/>).
/// </summary>
public class SessionManagementServiceSecurityTests
{
    [Fact]
    public async Task DetectSessionHijackingAsync_UnknownSession_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(DetectSessionHijackingAsync_UnknownSession_ReturnsFalse));
        var sut = BuildService(factory);

        (await sut.DetectSessionHijackingAsync("missing", "1.2.3.4", "UA")).Should().BeFalse();
    }

    [Fact]
    public async Task DetectSessionHijackingAsync_IpAndUserAgentBothChanged_MarksSuspiciousReturnsTrue()
    {
        var factory = CreateFactory(nameof(DetectSessionHijackingAsync_IpAndUserAgentBothChanged_MarksSuspiciousReturnsTrue));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", ipAddress: "1.1.1.1", userAgent: "OldAgent"));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var result = await sut.DetectSessionHijackingAsync("s1", "9.9.9.9", "NewAgent");

        result.Should().BeTrue();
        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.IsSuspicious.Should().BeTrue();
        session.SuspiciousReason.Should().Be("IP and User-Agent changed");
    }

    [Fact]
    public async Task DetectSessionHijackingAsync_OnlyUserAgentChanged_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(DetectSessionHijackingAsync_OnlyUserAgentChanged_ReturnsFalse));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", ipAddress: "1.1.1.1", userAgent: "OldAgent"));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var result = await sut.DetectSessionHijackingAsync("s1", "1.1.1.1", "NewAgent");

        result.Should().BeFalse();
        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.IsSuspicious.Should().BeFalse();
    }

    [Fact]
    public async Task DetectSessionHijackingAsync_IpChanged_BothCountriesLocal_SkipsImpossibleTravel()
    {
        var factory = CreateFactory(nameof(DetectSessionHijackingAsync_IpChanged_BothCountriesLocal_SkipsImpossibleTravel));
        await using (var db = factory.CreateDbContext())
        {
            // Old session was on a private-network IP (Country="Local Network"), same user agent.
            db.UserSessions.Add(MakeSession("s1", ipAddress: "192.168.1.5", userAgent: "SameAgent", country: "Local Network"));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        // New IP is also private and doesn't match a configured VPN subnet -> Country="Local Network" too.
        var result = await sut.DetectSessionHijackingAsync("s1", "10.0.0.5", "SameAgent");

        result.Should().BeFalse();
        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.IsSuspicious.Should().BeFalse();
    }

    [Fact]
    public async Task DetectSessionHijackingAsync_IpChanged_DifferentCountryRecentActivity_MarksSuspiciousReturnsTrue()
    {
        var factory = CreateFactory(nameof(DetectSessionHijackingAsync_IpChanged_DifferentCountryRecentActivity_MarksSuspiciousReturnsTrue));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession(
                "s1", ipAddress: "1.1.1.1", userAgent: "SameAgent",
                country: "Germany", lastActivity: DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        // Public IP -> DetectVpnAsync resolves Country="Unknown", which differs from "Germany"
        // and "Germany" is not a local keyword, so impossible-travel logic kicks in.
        var result = await sut.DetectSessionHijackingAsync("s1", "8.8.8.8", "SameAgent");

        result.Should().BeTrue();
        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.IsSuspicious.Should().BeTrue();
        session.SuspiciousReason.Should().Contain("Impossible travel");
    }

    [Fact]
    public async Task DetectSessionHijackingAsync_IpChanged_DifferentCountryButOldActivity_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(DetectSessionHijackingAsync_IpChanged_DifferentCountryButOldActivity_ReturnsFalse));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession(
                "s1", ipAddress: "1.1.1.1", userAgent: "SameAgent",
                country: "Germany", lastActivity: DateTime.UtcNow.AddHours(-2)));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var result = await sut.DetectSessionHijackingAsync("s1", "8.8.8.8", "SameAgent");

        result.Should().BeFalse();
        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.IsSuspicious.Should().BeFalse();
    }

    [Fact]
    public async Task DetectSessionHijackingAsync_IpChanged_OldCountryUnset_TreatsAsNotLocal()
    {
        var factory = CreateFactory(nameof(DetectSessionHijackingAsync_IpChanged_OldCountryUnset_TreatsAsNotLocal));
        await using (var db = factory.CreateDbContext())
        {
            // session.Country was never resolved (null) -> IsLocalCountry(null) short-circuits
            // to false, so the "both local" skip does not apply and impossible-travel logic runs.
            db.UserSessions.Add(MakeSession(
                "s1", ipAddress: "1.1.1.1", userAgent: "SameAgent",
                country: null, lastActivity: DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var result = await sut.DetectSessionHijackingAsync("s1", "8.8.8.8", "SameAgent");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MarkSessionSuspiciousAsync_UnknownSession_IsNoOp()
    {
        var factory = CreateFactory(nameof(MarkSessionSuspiciousAsync_UnknownSession_IsNoOp));
        var sut = BuildService(factory);

        await sut.MarkSessionSuspiciousAsync("missing", "test reason"); // should not throw

        (await factory.CreateDbContext().SecurityEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task MarkSessionSuspiciousAsync_KnownSession_SetsFieldsAndLogsSecurityEvent()
    {
        var factory = CreateFactory(nameof(MarkSessionSuspiciousAsync_KnownSession_SetsFieldsAndLogsSecurityEvent));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1"));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        await sut.MarkSessionSuspiciousAsync("s1", "unusual behaviour");

        await using var verify = factory.CreateDbContext();
        var session = await verify.UserSessions.SingleAsync();
        session.IsSuspicious.Should().BeTrue();
        session.SuspiciousActivityCount.Should().Be(1);
        session.SuspiciousReason.Should().Be("unusual behaviour");
        session.RiskLevel.Should().Be(SessionRiskLevel.High);
        session.LastSuspiciousActivity.Should().NotBeNull();

        var securityEvent = await verify.SecurityEvents.SingleAsync();
        securityEvent.EventType.Should().Be("SUSPICIOUS_ACTIVITY");
        securityEvent.Description.Should().Be("unusual behaviour");
    }

    [Fact]
    public async Task MarkSessionSuspiciousAsync_WorksOnInactiveSessionsToo()
    {
        var factory = CreateFactory(nameof(MarkSessionSuspiciousAsync_WorksOnInactiveSessionsToo));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s1", isActive: false));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        await sut.MarkSessionSuspiciousAsync("s1", "post-mortem flag");

        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.IsSuspicious.Should().BeTrue();
    }

    [Fact]
    public async Task DetectVpnAsync_LocalhostIp_ReturnsLocalhostCountryNoVpn()
    {
        var factory = CreateFactory(nameof(DetectVpnAsync_LocalhostIp_ReturnsLocalhostCountryNoVpn));
        var sut = BuildService(factory);

        var result = await sut.DetectVpnAsync("127.0.0.1");

        result.Country.Should().Be("Localhost");
        result.IsVpn.Should().BeFalse();
    }

    [Fact]
    public async Task DetectVpnAsync_PrivateIpNotMatchingSubnet_ReturnsLocalNetworkNoVpn()
    {
        var factory = CreateFactory(nameof(DetectVpnAsync_PrivateIpNotMatchingSubnet_ReturnsLocalNetworkNoVpn));
        var sut = BuildService(factory, vpnConfig: new VpnDetectionConfig { VpnSubnets = ["10.99.0.*"] });

        var result = await sut.DetectVpnAsync("192.168.1.50");

        result.Country.Should().Be("Local Network");
        result.IsVpn.Should().BeFalse();
    }

    [Fact]
    public async Task DetectVpnAsync_PrivateIpMatchingConfiguredSubnet_ReturnsVpnWithConfiguredConfidence()
    {
        var factory = CreateFactory(nameof(DetectVpnAsync_PrivateIpMatchingConfiguredSubnet_ReturnsVpnWithConfiguredConfidence));
        var sut = BuildService(factory, vpnConfig: new VpnDetectionConfig
        {
            VpnSubnets = ["10.0.5.*"],
            SubnetMatchConfidence = 80
        });

        var result = await sut.DetectVpnAsync("10.0.5.17");

        result.IsVpn.Should().BeTrue();
        result.ConfidenceScore.Should().Be(80);
        result.RiskFactors.Should().Contain("IP matches configured VPN subnet");
    }

    [Fact]
    public async Task DetectVpnAsync_PublicIp_ReturnsUnknownCountryNoVpn()
    {
        var factory = CreateFactory(nameof(DetectVpnAsync_PublicIp_ReturnsUnknownCountryNoVpn));
        var sut = BuildService(factory);

        var result = await sut.DetectVpnAsync("203.0.113.42");

        result.Country.Should().Be("Unknown");
        result.IsVpn.Should().BeFalse();
        result.RiskFactors.Should().Contain("Public IP - no VPN detection available");
    }
}
