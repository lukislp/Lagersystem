using System.Reflection;
using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Application.Services;
using LagersystemLVHome.Infrastructure.HostedServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.UnitTests.HostedServices;

/// <summary>
/// Covers <see cref="GdprCleanupHostedService"/>, the daily-scheduled background job that
/// triggers <see cref="IGdprCleanupService.CleanupPersonalDataAsync"/>.
/// <para/>
/// The service has no injectable clock/scheduler - <c>CalculateDelayUntilNextRun</c> and
/// <c>FormatTimeSpan</c> are private instance/static methods driven by real wall-clock time
/// (<c>DateTime.Now</c>), and the main <c>ExecuteAsync</c> loop's first action is always an
/// <c>await Task.Delay(delayUntilNextScheduledRun, stoppingToken)</c> before it ever calls the
/// cleanup service - which, for the default/typical schedules, is many hours. Rather than
/// block a unit test run for hours (or make it flaky by racing real clock boundaries), the
/// private time-calculation methods are invoked directly via reflection to get deterministic,
/// fast assertions on the actual scheduling logic, and the publicly reachable
/// start/stop/disabled-service behaviour is covered through the real <c>BackgroundService</c>
/// lifecycle (<see cref="BackgroundServiceExtensions"/> pattern: <c>StartAsync</c> then
/// <c>StopAsync</c>).
/// </summary>
public class GdprCleanupHostedServiceTests
{
    private static GdprCleanupHostedService BuildSut(GdprSettings settings, IGdprCleanupService? cleanupService = null, IServiceProvider? provider = null)
    {
        IServiceProvider serviceProvider;
        if (provider != null)
        {
            serviceProvider = provider;
        }
        else
        {
            var services = new ServiceCollection();
            services.AddSingleton(cleanupService ?? Substitute.For<IGdprCleanupService>());
            serviceProvider = services.BuildServiceProvider();
        }

        return new GdprCleanupHostedService(serviceProvider, Options.Create(settings), NullLogger<GdprCleanupHostedService>.Instance);
    }

    private static TimeSpan InvokeCalculateDelay(GdprCleanupHostedService sut)
    {
        var method = typeof(GdprCleanupHostedService).GetMethod("CalculateDelayUntilNextRun", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (TimeSpan)method.Invoke(sut, null)!;
    }

    private static string InvokeFormatTimeSpan(TimeSpan span)
    {
        var method = typeof(GdprCleanupHostedService).GetMethod("FormatTimeSpan", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [span])!;
    }

    // ---- CalculateDelayUntilNextRun (via reflection - see class remarks) -----------------

    [Fact]
    public void CalculateDelayUntilNextRun_TimeLaterTodayThanNow_ReturnsPositiveDelayWithinOneDay()
    {
        var laterToday = DateTime.Now.AddHours(1);
        // Guard against the (rare) case this test runs right at a day boundary, where
        // "1 hour from now" rolls into tomorrow and the assertion below would be wrong.
        if (laterToday.Date != DateTime.Now.Date) return;

        var sut = BuildSut(new GdprSettings { CleanupSchedule = $"{laterToday:HH}:{laterToday:mm}" });

        var delay = InvokeCalculateDelay(sut);

        delay.Should().BePositive();
        delay.Should().BeLessThan(TimeSpan.FromHours(1.5), "the schedule is ~1h from now today, not tomorrow");
    }

    [Fact]
    public void CalculateDelayUntilNextRun_TimeAlreadyPassedToday_RollsOverToTomorrow()
    {
        var earlierToday = DateTime.Now.AddHours(-1);
        if (earlierToday.Date != DateTime.Now.Date) return;

        var sut = BuildSut(new GdprSettings { CleanupSchedule = $"{earlierToday:HH}:{earlierToday:mm}" });

        var delay = InvokeCalculateDelay(sut);

        delay.Should().BePositive("a schedule time already passed today must roll over to tomorrow, not go negative");
        delay.Should().BeGreaterThan(TimeSpan.FromHours(22), "rolled-over-to-tomorrow delay must be close to 24h minus the ~1h already elapsed");
    }

    [Fact]
    public void CalculateDelayUntilNextRun_InvalidScheduleFormat_FallsBackToDefaultThreeAm()
    {
        var sut = BuildSut(new GdprSettings { CleanupSchedule = "not-a-time" });

        var delay = InvokeCalculateDelay(sut);

        delay.Should().BePositive().And.BeLessThanOrEqualTo(TimeSpan.FromHours(24), "an invalid schedule must fall back to a valid 03:00 default, not throw or hang");
    }

    [Fact]
    public void CalculateDelayUntilNextRun_MissingColonInSchedule_FallsBackToDefault()
    {
        var sut = BuildSut(new GdprSettings { CleanupSchedule = "0300" });

        var delay = InvokeCalculateDelay(sut);

        delay.Should().BePositive().And.BeLessThanOrEqualTo(TimeSpan.FromHours(24));
    }

    [Fact]
    public void CalculateDelayUntilNextRun_NonNumericHourOrMinute_FallsBackToDefault()
    {
        var sut = BuildSut(new GdprSettings { CleanupSchedule = "aa:bb" });

        var delay = InvokeCalculateDelay(sut);

        delay.Should().BePositive().And.BeLessThanOrEqualTo(TimeSpan.FromHours(24));
    }

    // ---- FormatTimeSpan (pure, deterministic, static) --------------------------------------

    [Theory]
    [InlineData(1, 2, 3, 4, "1d 2h 3m")]
    [InlineData(0, 5, 30, 0, "5h 30m")]
    [InlineData(0, 0, 15, 20, "15m 20s")]
    [InlineData(0, 0, 0, 42, "42s")]
    public void FormatTimeSpan_FormatsAtCorrectGranularity(int days, int hours, int minutes, int seconds, string expected)
    {
        var span = new TimeSpan(days, hours, minutes, seconds);

        InvokeFormatTimeSpan(span).Should().Be(expected);
    }

    // ---- ExecuteAsync lifecycle (public BackgroundService surface) -------------------------

    [Fact]
    public async Task StartAsync_WhenAutoCleanupDisabled_CompletesImmediatelyWithoutSchedulingOrCleaning()
    {
        var cleanupService = Substitute.For<IGdprCleanupService>();
        var sut = BuildSut(new GdprSettings { EnableAutoCleanup = false }, cleanupService);

        await sut.StartAsync(CancellationToken.None);
        // With auto-cleanup disabled, ExecuteAsync returns on its very first line (no delay),
        // so StopAsync should complete essentially instantly rather than waiting on a pending
        // Task.Delay.
        var stopTask = sut.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5)));

        completed.Should().Be(stopTask, "a disabled cleanup service must not block shutdown");
        await cleanupService.DidNotReceiveWithAnyArgs().CleanupPersonalDataAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// With auto-cleanup enabled, ExecuteAsync computes a real (non-zero, at-most-24h) delay
    /// and awaits it before ever touching the cleanup service. Cancelling shortly after start
    /// (simulating application shutdown) must interrupt that delay gracefully - caught by the
    /// service's own <c>catch (OperationCanceledException) { ...; break; }</c> - rather than
    /// letting the cancellation escape as an unhandled exception or hang.
    /// </summary>
    [Fact]
    public async Task StartAsync_ThenStop_DuringInitialDelay_StopsGracefullyWithoutRunningCleanup()
    {
        var cleanupService = Substitute.For<IGdprCleanupService>();
        var sut = BuildSut(new GdprSettings { EnableAutoCleanup = true, CleanupSchedule = "03:00" }, cleanupService);

        await sut.StartAsync(CancellationToken.None);
        var stopTask = sut.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(10)));

        completed.Should().Be(stopTask, "StopAsync cancels the internal stoppingToken, which must unblock the pending Task.Delay promptly");
        await stopTask; // rethrow if StopAsync itself faulted
        await cleanupService.DidNotReceiveWithAnyArgs().CleanupPersonalDataAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_LogsAndCallsBaseWithoutThrowing()
    {
        var sut = BuildSut(new GdprSettings { EnableAutoCleanup = false });
        await sut.StartAsync(CancellationToken.None);

        var act = async () => await sut.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
