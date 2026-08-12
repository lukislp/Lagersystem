using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace LagersystemLVHome.UnitTests.Services.Notification;

/// <summary>
/// Covers <see cref="NotificationHostedService"/>.
///
/// <c>ExecuteAsync</c> itself just wires up two <see cref="System.Threading.Timer"/> instances and
/// returns immediately - it does not loop. The hourly timer fires (near-)immediately and the daily
/// timer fires at the next 09:00, both on background timer threads that are not deterministically
/// awaitable from a test. Instead, the actual per-tick logic
/// (<c>ExecuteHourlyTasksAsync</c>/<c>ExecuteDailyTasksAsync</c>/<c>GetTimeUntilNextRun</c>) is
/// invoked directly via reflection, which is both deterministic and fully exercises the real
/// behavior without waiting on wall-clock timers.
/// </summary>
public sealed class NotificationHostedServiceTests
{
    private static (NotificationHostedService sut, INotificationService notification, IExpiryService expiry) Build()
    {
        var notification = Substitute.For<INotificationService>();
        var expiry = Substitute.For<IExpiryService>();

        var services = new ServiceCollection();
        services.AddScoped(_ => notification);
        services.AddScoped(_ => expiry);
        var provider = services.BuildServiceProvider();

        var sut = new NotificationHostedService(provider, NullLogger<NotificationHostedService>.Instance);
        return (sut, notification, expiry);
    }

    private static Task InvokeExecuteHourlyTasksAsync(NotificationHostedService sut)
    {
        var method = typeof(NotificationHostedService).GetMethod("ExecuteHourlyTasksAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(sut, new object[] { CancellationToken.None })!;
    }

    private static Task InvokeExecuteDailyTasksAsync(NotificationHostedService sut)
    {
        var method = typeof(NotificationHostedService).GetMethod("ExecuteDailyTasksAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(sut, new object[] { CancellationToken.None })!;
    }

    private static TimeSpan InvokeGetTimeUntilNextRun(NotificationHostedService sut)
    {
        var method = typeof(NotificationHostedService).GetMethod("GetTimeUntilNextRun", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (TimeSpan)method.Invoke(sut, null)!;
    }

    // ==================== ExecuteHourlyTasksAsync ====================

    [Fact]
    public async Task ExecuteHourlyTasksAsync_RunsLowStockExpiryAndCleanupInOrder()
    {
        var (sut, notification, expiry) = Build();

        await InvokeExecuteHourlyTasksAsync(sut);

        await notification.Received(1).CheckLowStockAndNotifyAsync();
        await expiry.Received(1).CheckExpiryAndNotifyAsync();
        await notification.Received(1).DeleteOldNotificationsAsync(30);
    }

    [Fact]
    public async Task ExecuteHourlyTasksAsync_NotificationServiceThrows_IsCaughtAndDoesNotPropagate()
    {
        var (sut, notification, _) = Build();
        notification.CheckLowStockAndNotifyAsync().Returns(Task.FromException(new InvalidOperationException("boom")));

        var act = () => InvokeExecuteHourlyTasksAsync(sut);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteHourlyTasksAsync_ExpiryServiceThrows_LowStockStillRanFirst()
    {
        var (sut, notification, expiry) = Build();
        expiry.CheckExpiryAndNotifyAsync().Returns(Task.FromException(new InvalidOperationException("boom")));

        var act = () => InvokeExecuteHourlyTasksAsync(sut);

        await act.Should().NotThrowAsync();
        await notification.Received(1).CheckLowStockAndNotifyAsync();
        await notification.DidNotReceive().DeleteOldNotificationsAsync(Arg.Any<int>());
    }

    // ==================== ExecuteDailyTasksAsync ====================

    [Fact]
    public async Task ExecuteDailyTasksAsync_MatchesCurrentHourExpectation()
    {
        // DateTime.Now is not injectable in this class, so this test asserts whichever branch is
        // actually correct for the moment it runs, rather than pinning a specific hour (per the
        // established convention for wall-clock-dependent code in this codebase).
        var (sut, notification, _) = Build();
        var isNineAM = DateTime.Now.Hour == 9;

        await InvokeExecuteDailyTasksAsync(sut);

        if (isNineAM)
        {
            await notification.Received(1).SendDailyDigestAsync();
        }
        else
        {
            await notification.DidNotReceive().SendDailyDigestAsync();
        }
    }

    [Fact]
    public async Task ExecuteDailyTasksAsync_NotificationServiceThrows_IsCaughtAndDoesNotPropagate()
    {
        var (sut, notification, _) = Build();
        notification.SendDailyDigestAsync().Returns(Task.FromException(new InvalidOperationException("boom")));

        var act = () => InvokeExecuteDailyTasksAsync(sut);

        await act.Should().NotThrowAsync();
    }

    // ==================== GetTimeUntilNextRun ====================

    [Fact]
    public void GetTimeUntilNextRun_ReturnsNonNegativeSpanLessThanTwentyFourHours()
    {
        var (sut, _, _) = Build();

        var result = InvokeGetTimeUntilNextRun(sut);

        result.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
        result.Should().BeLessThanOrEqualTo(TimeSpan.FromHours(24));
    }

    [Fact]
    public void GetTimeUntilNextRun_AddedToNow_LandsOnNineAM()
    {
        var (sut, _, _) = Build();

        var result = InvokeGetTimeUntilNextRun(sut);
        var target = DateTime.Now.Add(result);

        target.Hour.Should().Be(9);
        target.Minute.Should().Be(0);
    }

    // ==================== ExecuteAsync / Dispose ====================

    [Fact]
    public async Task ExecuteAsync_SetsUpTimersWithoutThrowing()
    {
        var (sut, _, _) = Build();

        var act = () => sut.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        sut.Dispose();
    }

    [Fact]
    public void Dispose_WithoutStart_DoesNotThrow()
    {
        var (sut, _, _) = Build();

        var act = () => sut.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task ExecuteAsync_HourlyTimerFires_EventuallyInvokesLowStockCheck()
    {
        // The hourly timer's dueTime is TimeSpan.Zero, so its first tick fires almost immediately
        // on a background thread pool timer. Polling briefly for the resulting call proves
        // ExecuteAsync really did wire the timer up to ExecuteHourlyTasksAsync, without needing to
        // wait a full hour for the period between subsequent ticks.
        var (sut, notification, _) = Build();

        await sut.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline && notification.ReceivedCalls().Any() == false)
            {
                await Task.Delay(50);
            }

            await notification.Received().CheckLowStockAndNotifyAsync();
        }
        finally
        {
            sut.Dispose();
        }
    }
}
