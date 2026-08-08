using System.Reflection;
using LagersystemLVHome.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Session;

using UserSession = LagersystemLVHome.Domain.Models.UserSession;

/// <summary>
/// Covers <see cref="SessionMonitorService"/>.
///
/// <see cref="SessionMonitorService.RunMonitorLoopAsync"/> polls every 5 seconds via a
/// <see cref="PeriodicTimer"/>, so tests that start a real monitor immediately stop it again
/// (StopMonitoringAsync cancels the token, which PeriodicTimer.WaitForNextTickAsync observes
/// immediately rather than waiting for the next tick) instead of waiting out a real interval.
/// <see cref="SessionMonitorService.CheckSessionStatusAsync"/> (the per-tick body) is exercised
/// directly via reflection against a manually constructed private CircuitMonitorState, which
/// avoids relying on the timer at all for that logic.
/// </summary>
public sealed class SessionMonitorServiceTests
{
    private static (SessionMonitorService sut, ISessionManagementService sessionService, IAuthService authService) Build()
    {
        var sessionService = Substitute.For<ISessionManagementService>();
        var authService = Substitute.For<IAuthService>();

        var services = new ServiceCollection();
        services.AddScoped(_ => sessionService);
        services.AddScoped(_ => authService);
        var provider = services.BuildServiceProvider();

        var sut = new SessionMonitorService(provider, NullLogger<SessionMonitorService>.Instance);
        return (sut, sessionService, authService);
    }

    private static object CreateCircuitMonitorState(string circuitId, int userId, string sessionId, CancellationTokenSource cts, bool isTerminated = false)
    {
        var type = typeof(SessionMonitorService).GetNestedType("CircuitMonitorState", BindingFlags.NonPublic)!;
        var state = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetProperty("CircuitId")!.SetValue(state, circuitId);
        type.GetProperty("UserId")!.SetValue(state, userId);
        type.GetProperty("SessionId")!.SetValue(state, sessionId);
        type.GetProperty("CancellationTokenSource")!.SetValue(state, cts);
        type.GetProperty("IsTerminated")!.SetValue(state, isTerminated);
        return state;
    }

    private static Task InvokeCheckSessionStatusAsync(SessionMonitorService sut, object state)
    {
        var method = typeof(SessionMonitorService).GetMethod("CheckSessionStatusAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(sut, new[] { state, CancellationToken.None })!;
    }

    // ==================== StartMonitoringAsync / IsMonitoring / GetActiveMonitorCount ====================

    [Fact]
    public async Task StartMonitoringAsync_ValidArgs_AddsMonitorAndIsMonitoringReturnsTrue()
    {
        var (sut, _, _) = Build();

        await sut.StartMonitoringAsync(1, "session-1", "circuit-1");

        sut.IsMonitoring("circuit-1").Should().BeTrue();
        sut.GetActiveMonitorCount().Should().Be(1);

        await sut.StopAllMonitoringAsync();
    }

    [Fact]
    public async Task StartMonitoringAsync_EmptyCircuitId_DoesNotAddMonitor()
    {
        var (sut, _, _) = Build();

        await sut.StartMonitoringAsync(1, "session-1", "");

        sut.GetActiveMonitorCount().Should().Be(0);
    }

    [Fact]
    public async Task StartMonitoringAsync_EmptySessionId_DoesNotAddMonitor()
    {
        var (sut, _, _) = Build();

        await sut.StartMonitoringAsync(1, "", "circuit-1");

        sut.GetActiveMonitorCount().Should().Be(0);
        sut.IsMonitoring("circuit-1").Should().BeFalse();
    }

    [Fact]
    public async Task StartMonitoringAsync_CalledTwiceForSameCircuit_ReplacesExistingMonitorWithoutDuplication()
    {
        var (sut, _, _) = Build();

        await sut.StartMonitoringAsync(1, "session-1", "circuit-1");
        await sut.StartMonitoringAsync(2, "session-2", "circuit-1");

        sut.GetActiveMonitorCount().Should().Be(1, "starting a new monitor for the same circuit should stop the old one first");

        await sut.StopAllMonitoringAsync();
    }

    [Fact]
    public async Task StartMonitoringAsync_AfterDispose_DoesNotAddMonitor()
    {
        var (sut, _, _) = Build();
        sut.Dispose();

        await sut.StartMonitoringAsync(1, "session-1", "circuit-1");

        sut.GetActiveMonitorCount().Should().Be(0);
    }

    [Fact]
    public async Task StartMonitoringAsync_LegacyOverload_UsesAsyncLocalCircuitIdAndCanBeStoppedByLegacyStop()
    {
        var (sut, _, _) = Build();

        await sut.StartMonitoringAsync(1, "session-legacy");
        sut.GetActiveMonitorCount().Should().Be(1);

        await sut.StopMonitoringAsync();

        sut.GetActiveMonitorCount().Should().Be(0);
    }

    [Fact]
    public void IsMonitoring_EmptyCircuitId_ReturnsFalse()
    {
        var (sut, _, _) = Build();

        sut.IsMonitoring("").Should().BeFalse();
    }

    [Fact]
    public void IsMonitoring_UnknownCircuit_ReturnsFalse()
    {
        var (sut, _, _) = Build();

        sut.IsMonitoring("nope").Should().BeFalse();
    }

    // ==================== StopMonitoringAsync ====================

    [Fact]
    public async Task StopMonitoringAsync_KnownCircuit_RemovesMonitor()
    {
        var (sut, _, _) = Build();
        await sut.StartMonitoringAsync(1, "session-1", "circuit-1");

        await sut.StopMonitoringAsync("circuit-1");

        sut.IsMonitoring("circuit-1").Should().BeFalse();
        sut.GetActiveMonitorCount().Should().Be(0);
    }

    [Fact]
    public async Task StopMonitoringAsync_UnknownCircuit_DoesNothing()
    {
        var (sut, _, _) = Build();

        var act = () => sut.StopMonitoringAsync("does-not-exist");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopMonitoringAsync_EmptyCircuitId_DoesNothing()
    {
        var (sut, _, _) = Build();

        var act = () => sut.StopMonitoringAsync("");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopMonitoringAsync_LegacyOverload_NoActiveLegacySession_CompletesWithoutThrowing()
    {
        var (sut, _, _) = Build();

        var act = () => sut.StopMonitoringAsync();

        await act.Should().NotThrowAsync();
    }

    // ==================== StopAllMonitoringAsync ====================

    [Fact]
    public async Task StopAllMonitoringAsync_MultipleMonitors_StopsAllOfThem()
    {
        var (sut, _, _) = Build();
        await sut.StartMonitoringAsync(1, "s1", "c1");
        await sut.StartMonitoringAsync(2, "s2", "c2");
        await sut.StartMonitoringAsync(3, "s3", "c3");

        await sut.StopAllMonitoringAsync();

        sut.GetActiveMonitorCount().Should().Be(0);
    }

    [Fact]
    public async Task StopAllMonitoringAsync_NoMonitors_CompletesWithoutThrowing()
    {
        var (sut, _, _) = Build();

        var act = () => sut.StopAllMonitoringAsync();

        await act.Should().NotThrowAsync();
    }

    // ==================== ForceTerminateSessionAsync ====================

    [Fact]
    public async Task ForceTerminateSessionAsync_MatchingSession_RaisesEventAndStopsMonitor()
    {
        // NOTE: ForceTerminateSessionAsync (the admin/cleanup entry point) only raises the
        // SessionTerminated event and stops the monitor - unlike the private TerminateAsync
        // path used internally by CheckSessionStatusAsync, it does NOT call IAuthService.LogoutAsync
        // itself (see SessionMonitorService.cs ForceTerminateSessionAsync vs TerminateAsync).
        var (sut, _, authService) = Build();
        await sut.StartMonitoringAsync(1, "target-session", "circuit-1");

        SessionTerminatedEventArgs? raised = null;
        sut.SessionTerminated += (_, args) => raised = args;

        await sut.ForceTerminateSessionAsync("target-session", "Admin kicked user");

        raised.Should().NotBeNull();
        raised!.SessionId.Should().Be("target-session");
        raised.CircuitId.Should().Be("circuit-1");
        raised.Reason.Should().Be("Admin kicked user");
        sut.IsMonitoring("circuit-1").Should().BeFalse();
        await authService.DidNotReceiveWithAnyArgs().LogoutAsync(default);
    }

    [Fact]
    public async Task ForceTerminateSessionAsync_MultipleCircuitsSameSession_TerminatesAll()
    {
        var (sut, _, _) = Build();
        await sut.StartMonitoringAsync(1, "shared-session", "circuit-a");
        await sut.StartMonitoringAsync(1, "shared-session", "circuit-b");

        var raisedCount = 0;
        sut.SessionTerminated += (_, _) => raisedCount++;

        await sut.ForceTerminateSessionAsync("shared-session", "cleanup");

        raisedCount.Should().Be(2);
        sut.GetActiveMonitorCount().Should().Be(0);
    }

    [Fact]
    public async Task ForceTerminateSessionAsync_NoMatchingSession_DoesNotRaiseEventOrThrow()
    {
        var (sut, _, _) = Build();
        await sut.StartMonitoringAsync(1, "other-session", "circuit-1");

        var raised = false;
        sut.SessionTerminated += (_, _) => raised = true;

        var act = () => sut.ForceTerminateSessionAsync("no-such-session", "n/a");

        await act.Should().NotThrowAsync();
        raised.Should().BeFalse();
        sut.IsMonitoring("circuit-1").Should().BeTrue();

        await sut.StopAllMonitoringAsync();
    }

    [Fact]
    public async Task CheckSessionStatusAsync_SessionNotFound_AuthServiceLogoutThrows_IsCaughtAndStillStopsMonitor()
    {
        // Exercises the private TerminateAsync path (reached via CheckSessionStatusAsync), which
        // - unlike ForceTerminateSessionAsync - does call IAuthService.LogoutAsync and wraps it in
        // its own try/catch so a failure there does not prevent the monitor from being stopped.
        var (sut, sessionService, authService) = Build();
        sessionService.GetSessionAsync("target-session", Arg.Any<CancellationToken>()).Returns((UserSession?)null);
        authService.LogoutAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("boom")));
        await sut.StartMonitoringAsync(1, "target-session", "circuit-1");

        var state = CreateCircuitMonitorState("circuit-1", 1, "target-session", new CancellationTokenSource());
        var act = () => InvokeCheckSessionStatusAsync(sut, state);

        await act.Should().NotThrowAsync();
        await authService.Received(1).LogoutAsync(Arg.Any<CancellationToken>());
        sut.IsMonitoring("circuit-1").Should().BeFalse();
    }

    // ==================== CheckSessionStatusAsync (private, invoked via reflection) ====================

    [Fact]
    public async Task CheckSessionStatusAsync_SessionNotFound_TerminatesAndRaisesEvent()
    {
        var (sut, sessionService, _) = Build();
        sessionService.GetSessionAsync("missing-session", Arg.Any<CancellationToken>()).Returns((UserSession?)null);
        await sut.StartMonitoringAsync(1, "missing-session", "circuit-1");

        SessionTerminatedEventArgs? raised = null;
        sut.SessionTerminated += (_, args) => raised = args;

        var state = CreateCircuitMonitorState("circuit-1", 1, "missing-session", new CancellationTokenSource());
        await InvokeCheckSessionStatusAsync(sut, state);

        raised.Should().NotBeNull();
        raised!.Reason.Should().Contain("not found");
    }

    [Fact]
    public async Task CheckSessionStatusAsync_SessionInactive_TerminatesWithEndReason()
    {
        var (sut, sessionService, _) = Build();
        sessionService.GetSessionAsync("ended-session", Arg.Any<CancellationToken>())
            .Returns(new UserSession { SessionId = "ended-session", IsActive = false, EndReason = SessionEndReason.AdminForceLogout });

        SessionTerminatedEventArgs? raised = null;
        sut.SessionTerminated += (_, args) => raised = args;

        var state = CreateCircuitMonitorState("circuit-1", 1, "ended-session", new CancellationTokenSource());
        await InvokeCheckSessionStatusAsync(sut, state);

        raised.Should().NotBeNull();
        raised!.Reason.Should().Contain("AdminForceLogout");
    }

    [Fact]
    public async Task CheckSessionStatusAsync_SessionActive_DoesNotTerminate()
    {
        var (sut, sessionService, _) = Build();
        sessionService.GetSessionAsync("active-session", Arg.Any<CancellationToken>())
            .Returns(new UserSession { SessionId = "active-session", IsActive = true });

        var raised = false;
        sut.SessionTerminated += (_, _) => raised = true;

        var state = CreateCircuitMonitorState("circuit-1", 1, "active-session", new CancellationTokenSource());
        await InvokeCheckSessionStatusAsync(sut, state);

        raised.Should().BeFalse();
    }

    [Fact]
    public async Task CheckSessionStatusAsync_AlreadyTerminatedState_ReturnsImmediatelyWithoutQuerying()
    {
        var (sut, sessionService, _) = Build();

        var state = CreateCircuitMonitorState("circuit-1", 1, "any-session", new CancellationTokenSource(), isTerminated: true);
        await InvokeCheckSessionStatusAsync(sut, state);

        await sessionService.DidNotReceiveWithAnyArgs().GetSessionAsync(default!, default);
    }

    [Fact]
    public async Task CheckSessionStatusAsync_SessionServiceThrows_IsCaughtAndDoesNotPropagate()
    {
        var (sut, sessionService, _) = Build();
        sessionService.GetSessionAsync("broken-session", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<UserSession?>(new InvalidOperationException("db down")));

        var state = CreateCircuitMonitorState("circuit-1", 1, "broken-session", new CancellationTokenSource());
        var method = typeof(SessionMonitorService).GetMethod("CheckSessionStatusAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var act = async () => await (Task)method.Invoke(sut, new[] { state, CancellationToken.None })!;

        await act.Should().NotThrowAsync();
    }

    // ==================== Dispose ====================

    [Fact]
    public void Dispose_WithActiveMonitors_StopsThemAllSynchronously()
    {
        var (sut, _, _) = Build();
        sut.StartMonitoringAsync(1, "s1", "c1").GetAwaiter().GetResult();

        sut.Dispose();

        sut.GetActiveMonitorCount().Should().Be(0);
    }

    [Fact]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var (sut, _, _) = Build();

        sut.Dispose();
        var act = () => sut.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_StopAllMonitoringThrows_LogsErrorAndStillCompletes()
    {
        // Forces StopAllMonitoringAsync (called synchronously inside Dispose) to throw by
        // disposing the private _shutdownLock semaphore out from under it first - exercises
        // Dispose's own try/catch/finally without needing a genuine race condition.
        var (sut, _, _) = Build();
        var lockField = typeof(SessionMonitorService).GetField("_shutdownLock", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((SemaphoreSlim)lockField.GetValue(sut)!).Dispose();

        var act = () => sut.Dispose();

        act.Should().NotThrow();
    }

    // ==================== RunMonitorLoopAsync (real PeriodicTimer tick, ~5s) ====================

    [Fact(Timeout = 15000)]
    public async Task StartMonitoringAsync_RealTimerTick_EventuallyCallsCheckSessionStatus()
    {
        // Unlike the other tests in this file, this one lets the real 5-second PeriodicTimer in
        // RunMonitorLoopAsync fire at least once instead of bypassing it via reflection, to prove
        // the loop is genuinely wired up end-to-end. Bounded by both xUnit's own timeout and a
        // capped polling window so a regression here fails fast instead of hanging the suite.
        var (sut, sessionService, _) = Build();
        sessionService.GetSessionAsync("tick-session", Arg.Any<CancellationToken>())
            .Returns(new UserSession { SessionId = "tick-session", IsActive = true });

        await sut.StartMonitoringAsync(1, "tick-session", "circuit-tick");
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
            while (DateTime.UtcNow < deadline && sessionService.ReceivedCalls().Any() == false)
            {
                await Task.Delay(100);
            }

            await sessionService.Received().GetSessionAsync("tick-session", Arg.Any<CancellationToken>());
        }
        finally
        {
            await sut.StopAllMonitoringAsync();
        }
    }
}
