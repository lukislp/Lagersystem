namespace LagersystemLVHome.UnitTests.Services.Auth;

/// <summary>Covers <see cref="AuthService.LogoutAsync"/>.</summary>
public class AuthServiceLogoutTests
{
    [Fact]
    public async Task LogoutAsync_WithNoCurrentUser_OnlyClearsAuthStateWithoutAuditing()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LogoutAsync_WithNoCurrentUser_OnlyClearsAuthStateWithoutAuditing));

        await fixture.Sut.LogoutAsync();

        await fixture.Audit.DidNotReceiveWithAnyArgs().LogAsync(default!, default!);
        await fixture.SessionMgmt.DidNotReceiveWithAnyArgs().EndSessionAsync(default!, default);
    }

    [Fact]
    public async Task LogoutAsync_WithActiveSession_EndsSessionAndAuditsLogout()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(circuitId: "circuit-logout-1");
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LogoutAsync_WithActiveSession_EndsSessionAndAuditsLogout), accessor);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);
        (await fixture.Sut.LoginAsync(user.Username, "Correct!1")).IsSuccess.Should().BeTrue();
        var sessionId = fixture.UserStore.GetSessionId();
        sessionId.Should().NotBeNullOrEmpty();

        await fixture.Sut.LogoutAsync();

        await fixture.Audit.Received(1).LogAsync("LOGOUT", "User", user.Id, null, AuditSeverity.Info);
        await fixture.SessionMgmt.Received(1).EndSessionAsync(sessionId!, SessionEndReason.UserLogout, Arg.Any<string?>());
    }

    [Fact]
    public async Task LogoutAsync_WithSessionIdOnlyInHttpContextItems_EndsSessionUsingThatId()
    {
        // Simulate a scenario where the circuit store never received the session id (e.g.
        // login happened without session management) but the request-scoped middleware
        // populated HttpContext.Items["SessionId"] - LogoutAsync must fall back to it.
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(circuitId: "circuit-logout-2", sessionIdItem: "items-session-42");
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LogoutAsync_WithSessionIdOnlyInHttpContextItems_EndsSessionUsingThatId), accessor);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);
        await fixture.StateProvider.MarkUserAsAuthenticated(user);

        await fixture.Sut.LogoutAsync();

        await fixture.SessionMgmt.Received(1).EndSessionAsync("items-session-42", SessionEndReason.UserLogout, Arg.Any<string?>());
    }

    [Fact]
    public async Task LogoutAsync_WithNoSessionIdAnywhere_StillAuditsLogoutWithoutEndingSession()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(circuitId: "circuit-logout-3");
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LogoutAsync_WithNoSessionIdAnywhere_StillAuditsLogoutWithoutEndingSession), accessor);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);
        await fixture.StateProvider.MarkUserAsAuthenticated(user);

        await fixture.Sut.LogoutAsync();

        await fixture.Audit.Received(1).LogAsync("LOGOUT", "User", user.Id, null, AuditSeverity.Info);
        await fixture.SessionMgmt.DidNotReceiveWithAnyArgs().EndSessionAsync(default!, default);
    }

    [Fact]
    public async Task LogoutAsync_WhenSessionEndThrows_StillCompletesLogout()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(circuitId: "circuit-logout-4");
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LogoutAsync_WhenSessionEndThrows_StillCompletesLogout), accessor);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);
        await fixture.Sut.LoginAsync(user.Username, "Correct!1");
        fixture.SessionMgmt.EndSessionAsync(Arg.Any<string>(), Arg.Any<SessionEndReason>(), Arg.Any<string?>(), Arg.Any<int?>())
            .Returns<Task>(_ => throw new InvalidOperationException("boom"));

        await fixture.Sut.LogoutAsync();

        fixture.UserStore.GetUser().Should().BeNull("MarkUserAsLoggedOut must still run even if ending the session throws");
    }

    [Fact]
    public async Task LogoutAsync_WithoutSessionManagementService_SkipsSessionEndButStillAudits()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(circuitId: "circuit-logout-5");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(LogoutAsync_WithoutSessionManagementService_SkipsSessionEndButStillAudits), accessor, includeSessionMgmt: false);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);
        await fixture.StateProvider.MarkUserAsAuthenticated(user);

        await fixture.Sut.LogoutAsync();

        await fixture.Audit.Received(1).LogAsync("LOGOUT", "User", user.Id, null, AuditSeverity.Info);
    }
}
