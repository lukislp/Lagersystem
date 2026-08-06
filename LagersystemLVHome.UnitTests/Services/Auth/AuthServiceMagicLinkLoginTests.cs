namespace LagersystemLVHome.UnitTests.Services.Auth;

/// <summary>Covers <see cref="AuthService.LoginWithMagicLinkAsync"/>.</summary>
public class AuthServiceMagicLinkLoginTests
{
    [Fact]
    public async Task LoginWithMagicLinkAsync_WithoutPasswordlessService_ReturnsUnavailable()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(LoginWithMagicLinkAsync_WithoutPasswordlessService_ReturnsUnavailable), includePasswordless: false);

        var result = await fixture.Sut.LoginWithMagicLinkAsync("token");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.PasswordlessUnavailable);
    }

    [Fact]
    public async Task LoginWithMagicLinkAsync_WithInvalidToken_ReturnsMagicLinkInvalid()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginWithMagicLinkAsync_WithInvalidToken_ReturnsMagicLinkInvalid));
        fixture.Passwordless.ValidateMagicLinkAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(Task.FromResult<User?>(null));

        var result = await fixture.Sut.LoginWithMagicLinkAsync("bad-token");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.MagicLinkInvalid);
    }

    [Fact]
    public async Task LoginWithMagicLinkAsync_WithMissingGdprConsent_ReturnsGdprConsentRequired()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginWithMagicLinkAsync_WithMissingGdprConsent_ReturnsGdprConsentRequired));
        var user = AuthServiceTestSupport.CreateValidUser();
        user.GdprConsentGiven = false;
        fixture.Passwordless.ValidateMagicLinkAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(Task.FromResult<User?>(user));

        var result = await fixture.Sut.LoginWithMagicLinkAsync("token");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.GdprConsentRequired);
    }

    [Fact]
    public async Task LoginWithMagicLinkAsync_WithMissingGranularConsent_ReturnsGranularConsentRequired()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginWithMagicLinkAsync_WithMissingGranularConsent_ReturnsGranularConsentRequired));
        var user = AuthServiceTestSupport.CreateValidUser();
        user.AnalyticsConsent = false;
        fixture.Passwordless.ValidateMagicLinkAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(Task.FromResult<User?>(user));

        var result = await fixture.Sut.LoginWithMagicLinkAsync("token");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.GranularConsentRequired);
    }

    [Fact]
    public async Task LoginWithMagicLinkAsync_WithIpDenied_ReturnsIpDeniedAndAudits()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginWithMagicLinkAsync_WithIpDenied_ReturnsIpDeniedAndAudits));
        var user = AuthServiceTestSupport.CreateValidUser();
        user.Id = 7;
        fixture.Passwordless.ValidateMagicLinkAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(Task.FromResult<User?>(user));
        fixture.IpAccess.CheckAccessAsync(user.Id, Arg.Any<string>())
            .Returns(Task.FromResult(LagersystemLVHome.Application.Services.IpAccessCheckResult.Denied("nope", "rule-x")));

        var result = await fixture.Sut.LoginWithMagicLinkAsync("token");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.IpDenied);
        await fixture.Audit.Received(1).LogAsync("MAGIC_LINK_LOGIN_IP_DENIED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Warning);
    }

    [Fact]
    public async Task LoginWithMagicLinkAsync_WithValidToken_CreatesSessionAndMarksAuthenticated()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(circuitId: "circuit-ml-1");
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginWithMagicLinkAsync_WithValidToken_CreatesSessionAndMarksAuthenticated), accessor);
        var user = AuthServiceTestSupport.CreateValidUser();
        user.Id = 11;
        fixture.Passwordless.ValidateMagicLinkAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(Task.FromResult<User?>(user));

        var result = await fixture.Sut.LoginWithMagicLinkAsync("token");

        result.IsSuccess.Should().BeTrue($"error was '{result.ErrorCode}'");
        result.Value!.Id.Should().Be(user.Id);
        await fixture.SessionMgmt.Received(1).CreateSessionAsync(user.Id, user.WarehouseId, Arg.Any<string>(), Arg.Any<string>());
        fixture.UserStore.GetSessionId().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LoginWithMagicLinkAsync_WithoutSessionManagementService_StillSucceeds()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(LoginWithMagicLinkAsync_WithoutSessionManagementService_StillSucceeds), includeSessionMgmt: false);
        var user = AuthServiceTestSupport.CreateValidUser();
        user.Id = 12;
        fixture.Passwordless.ValidateMagicLinkAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(Task.FromResult<User?>(user));

        var result = await fixture.Sut.LoginWithMagicLinkAsync("token");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task LoginWithMagicLinkAsync_WhenSessionCreationThrows_StillSucceeds()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginWithMagicLinkAsync_WhenSessionCreationThrows_StillSucceeds));
        var user = AuthServiceTestSupport.CreateValidUser();
        user.Id = 13;
        fixture.Passwordless.ValidateMagicLinkAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(Task.FromResult<User?>(user));
        fixture.SessionMgmt.CreateSessionAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task<LagersystemLVHome.Domain.Models.UserSession>>(_ => throw new InvalidOperationException("boom"));

        var result = await fixture.Sut.LoginWithMagicLinkAsync("token");

        result.IsSuccess.Should().BeTrue("session bootstrap failures must not block a successful magic-link login");
    }
}
