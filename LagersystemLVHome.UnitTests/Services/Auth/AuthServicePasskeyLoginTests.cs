using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.UnitTests.Services.Auth;

/// <summary>Covers <see cref="AuthService.LoginWithPasskeyAsync"/>.</summary>
public class AuthServicePasskeyLoginTests
{
    [Fact]
    public async Task LoginWithPasskeyAsync_WithUnknownUser_ReturnsUserNotFound()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginWithPasskeyAsync_WithUnknownUser_ReturnsUserNotFound));

        var result = await fixture.Sut.LoginWithPasskeyAsync(999);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.UserNotFound);
    }

    [Fact]
    public async Task LoginWithPasskeyAsync_WithInactiveUser_ReturnsInactiveAndAudits()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginWithPasskeyAsync_WithInactiveUser_ReturnsInactiveAndAudits));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u => u.IsActive = false);

        var result = await fixture.Sut.LoginWithPasskeyAsync(user.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.Inactive);
        await fixture.Audit.Received(1).LogAsync("PASSKEY_LOGIN_DENIED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Warning);
    }

    [Fact]
    public async Task LoginWithPasskeyAsync_WithPendingApproval_ReturnsPendingApproval()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginWithPasskeyAsync_WithPendingApproval_ReturnsPendingApproval));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u => u.ApprovalStatus = UserApprovalStatus.Pending);

        var result = await fixture.Sut.LoginWithPasskeyAsync(user.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.PendingApproval);
    }

    [Fact]
    public async Task LoginWithPasskeyAsync_WithRejectedApproval_ReturnsRejected()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginWithPasskeyAsync_WithRejectedApproval_ReturnsRejected));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u => u.ApprovalStatus = UserApprovalStatus.Rejected);

        var result = await fixture.Sut.LoginWithPasskeyAsync(user.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.Rejected);
    }

    [Fact]
    public async Task LoginWithPasskeyAsync_WithMissingGdprConsent_ReturnsGdprConsentRequired()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginWithPasskeyAsync_WithMissingGdprConsent_ReturnsGdprConsentRequired));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u => u.GdprConsentGiven = false);

        var result = await fixture.Sut.LoginWithPasskeyAsync(user.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.GdprConsentRequired);
    }

    [Fact]
    public async Task LoginWithPasskeyAsync_WithMissingGranularConsent_ReturnsGranularConsentRequired()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginWithPasskeyAsync_WithMissingGranularConsent_ReturnsGranularConsentRequired));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u => u.DeviceFingerprintConsent = false);

        var result = await fixture.Sut.LoginWithPasskeyAsync(user.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.GranularConsentRequired);
    }

    [Fact]
    public async Task LoginWithPasskeyAsync_WithIpDenied_ReturnsIpDeniedAndAudits()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginWithPasskeyAsync_WithIpDenied_ReturnsIpDeniedAndAudits));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);
        fixture.IpAccess.CheckAccessAsync(user.Id, Arg.Any<string>())
            .Returns(Task.FromResult(LagersystemLVHome.Application.Services.IpAccessCheckResult.Denied("nope", "rule-y")));

        var result = await fixture.Sut.LoginWithPasskeyAsync(user.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.IpDenied);
        await fixture.Audit.Received(1).LogAsync("PASSKEY_LOGIN_IP_DENIED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Warning);
    }

    [Fact]
    public async Task LoginWithPasskeyAsync_WithValidUser_UpdatesLoginDataCreatesSessionAndAudits()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(circuitId: "circuit-pk-1", deviceFingerprintCookie: "fp-pk");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(LoginWithPasskeyAsync_WithValidUser_UpdatesLoginDataCreatesSessionAndAudits), accessor);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u =>
        {
            u.FailedLoginAttempts = 2;
            u.LockedUntil = DateTime.UtcNow.AddMinutes(5);
        });

        var result = await fixture.Sut.LoginWithPasskeyAsync(user.Id);

        result.IsSuccess.Should().BeTrue($"error was '{result.ErrorCode}'");
        result.Value!.Id.Should().Be(user.Id);

        await using var verify = fixture.Factory.CreateDbContext();
        var refreshed = await verify.Users.FindAsync(user.Id);
        refreshed!.FailedLoginAttempts.Should().Be(0);
        refreshed.LockedUntil.Should().BeNull();
        refreshed.LastLoginAt.Should().BeOnOrBefore(DateTime.UtcNow);

        await fixture.Audit.Received(1).LogAsync("PASSKEY_LOGIN_SUCCESS", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Info);
        await fixture.SessionMgmt.Received(1).CreateSessionAsync(user.Id, user.WarehouseId, Arg.Any<string>(), Arg.Any<string>());
        await fixture.DeviceFp.Received(1).SaveDeviceFingerprintAsync(Arg.Any<int>(), "fp-pk", Arg.Any<Microsoft.AspNetCore.Http.HttpContext>());
        await fixture.SessionMonitor.Received(1).StartMonitoringAsync(user.Id, Arg.Any<string>(), "circuit-pk-1");
    }

    [Fact]
    public async Task LoginWithPasskeyAsync_WithoutSessionManagementService_StillSucceeds()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(LoginWithPasskeyAsync_WithoutSessionManagementService_StillSucceeds), includeSessionMgmt: false);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);

        var result = await fixture.Sut.LoginWithPasskeyAsync(user.Id);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task LoginWithPasskeyAsync_WhenSessionCreationThrows_StillSucceeds()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginWithPasskeyAsync_WhenSessionCreationThrows_StillSucceeds));
        fixture.SessionMgmt.CreateSessionAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task<LagersystemLVHome.Domain.Models.UserSession>>(_ => throw new InvalidOperationException("boom"));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);

        var result = await fixture.Sut.LoginWithPasskeyAsync(user.Id);

        result.IsSuccess.Should().BeTrue("session bootstrap failures must not block a successful passkey login");
    }

    [Fact]
    public async Task LoginWithPasskeyAsync_WithSessionManagementButNoCircuitId_StartsMonitoringWithoutCircuit()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor();
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(LoginWithPasskeyAsync_WithSessionManagementButNoCircuitId_StartsMonitoringWithoutCircuit), accessor);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);

        var result = await fixture.Sut.LoginWithPasskeyAsync(user.Id);

        result.IsSuccess.Should().BeTrue();
        await fixture.SessionMonitor.Received(1).StartMonitoringAsync(user.Id, Arg.Any<string>());
    }
}
