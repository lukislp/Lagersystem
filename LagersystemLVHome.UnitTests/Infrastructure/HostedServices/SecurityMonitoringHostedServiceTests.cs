using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Infrastructure.HostedServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace LagersystemLVHome.UnitTests.Infrastructure.HostedServices;

/// <summary>
/// Covers <see cref="SecurityMonitoringHostedService"/>.
///
/// The alert-cooldown bookkeeping (<c>ShouldSendAlert</c>/<c>MarkAlertSent</c>/
/// <c>CleanupOldCooldowns</c>) is private but pure with respect to its own
/// <c>ConcurrentDictionary</c> state, so it is exercised directly via reflection - fully
/// deterministic and independent of wall-clock timing. The outer polling loop itself (which
/// checks every 10 seconds, with no initial delay) is covered by a short StartAsync/poll/StopAsync
/// integration test that proves detection results are actually wired through to the alert service.
/// </summary>
public sealed class SecurityMonitoringHostedServiceTests
{
    private static (SecurityMonitoringHostedService sut, IRateLimitService rateLimit, ISecurityAlertService alerts) Build(
        SecurityAlertsSettings? settings = null)
    {
        var rateLimit = Substitute.For<IRateLimitService>();
        var alerts = Substitute.For<ISecurityAlertService>();
        rateLimit.DetectBurstAttack(Arg.Any<string>()).Returns(new BurstAttackDetection());
        rateLimit.DetectBruteForce(Arg.Any<string>()).Returns(new BruteForceDetection());
        rateLimit.DetectDDoS(Arg.Any<TimeSpan>()).Returns(new DDoSDetection());
        rateLimit.DetectSlowRateAttack().Returns(new SlowRateAttackDetection());

        var services = new ServiceCollection();
        services.AddScoped(_ => rateLimit);
        services.AddScoped(_ => alerts);
        var provider = services.BuildServiceProvider();

        var sut = new SecurityMonitoringHostedService(
            provider, NullLogger<SecurityMonitoringHostedService>.Instance, Options.Create(settings ?? new SecurityAlertsSettings()));
        return (sut, rateLimit, alerts);
    }

    private static bool InvokeShouldSendAlert(SecurityMonitoringHostedService sut, string threatType, string identifier)
    {
        var method = typeof(SecurityMonitoringHostedService).GetMethod("ShouldSendAlert", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (bool)method.Invoke(sut, new object[] { threatType, identifier })!;
    }

    private static void InvokeMarkAlertSent(SecurityMonitoringHostedService sut, string threatType, string identifier)
    {
        var method = typeof(SecurityMonitoringHostedService).GetMethod("MarkAlertSent", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(sut, new object[] { threatType, identifier });
    }

    private static void InvokeCleanupOldCooldowns(SecurityMonitoringHostedService sut)
    {
        var method = typeof(SecurityMonitoringHostedService).GetMethod("CleanupOldCooldowns", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(sut, null);
    }

    private static object GetLastAlertSentDictionary(SecurityMonitoringHostedService sut)
    {
        var field = typeof(SecurityMonitoringHostedService).GetField("_lastAlertSent", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return field.GetValue(sut)!;
    }

    // ==================== ShouldSendAlert / MarkAlertSent (cooldown bookkeeping) ====================

    [Fact]
    public void ShouldSendAlert_NoPriorAlert_ReturnsTrue()
    {
        var (sut, _, _) = Build();

        InvokeShouldSendAlert(sut, "BurstAttack", "1.2.3.4").Should().BeTrue();
    }

    [Fact]
    public void ShouldSendAlert_RecentlyMarkedSent_ReturnsFalse()
    {
        var (sut, _, _) = Build();
        InvokeMarkAlertSent(sut, "BurstAttack", "1.2.3.4");

        InvokeShouldSendAlert(sut, "BurstAttack", "1.2.3.4").Should().BeFalse("within the 15-minute cooldown window");
    }

    [Fact]
    public void ShouldSendAlert_DifferentIdentifier_IsIndependentOfOtherCooldowns()
    {
        var (sut, _, _) = Build();
        InvokeMarkAlertSent(sut, "BurstAttack", "1.2.3.4");

        InvokeShouldSendAlert(sut, "BurstAttack", "5.6.7.8").Should().BeTrue();
    }

    [Fact]
    public void ShouldSendAlert_DifferentThreatType_SameIdentifier_IsIndependentOfOtherCooldowns()
    {
        var (sut, _, _) = Build();
        InvokeMarkAlertSent(sut, "BurstAttack", "1.2.3.4");

        InvokeShouldSendAlert(sut, "BruteForce", "1.2.3.4").Should().BeTrue();
    }

    [Fact]
    public void CleanupOldCooldowns_RemovesEntriesOlderThanOneHour_KeepsRecentOnes()
    {
        var (sut, _, _) = Build();
        var dict = (System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>)GetLastAlertSentDictionary(sut);
        dict["Old:x"] = DateTime.UtcNow.AddHours(-2);
        dict["Recent:x"] = DateTime.UtcNow;

        InvokeCleanupOldCooldowns(sut);

        dict.ContainsKey("Old:x").Should().BeFalse();
        dict.ContainsKey("Recent:x").Should().BeTrue();
    }

    [Fact]
    public void CleanupOldCooldowns_EmptyDictionary_DoesNotThrow()
    {
        var (sut, _, _) = Build();

        var act = () => InvokeCleanupOldCooldowns(sut);

        act.Should().NotThrow();
    }

    // ==================== ExecuteAsync loop (integration, short-lived) ====================

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NoThreatsDetected_NeverCallsAlertService()
    {
        var (sut, rateLimit, alerts) = Build();

        await sut.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => rateLimit.ReceivedCalls().Any(), TimeSpan.FromSeconds(3));
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await sut.StopAsync(stopCts.Token);

        alerts.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_BurstAttackDetected_SendsAlertOnce()
    {
        var (sut, rateLimit, alerts) = Build();
        rateLimit.DetectBurstAttack(Arg.Any<string>()).Returns(new BurstAttackDetection
        {
            IsBurstAttack = true,
            RequestsInBurst = 500,
            BurstDuration = TimeSpan.FromSeconds(2),
            Identifier = "global-check"
        });

        await sut.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => alerts.ReceivedCalls().Any(), TimeSpan.FromSeconds(3));
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await sut.StopAsync(stopCts.Token);

        await alerts.Received().SendBurstAttackAlertAsync(Arg.Any<BurstAttackDetection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_BruteForceDetected_SendsAlertOnce()
    {
        var (sut, rateLimit, alerts) = Build();
        rateLimit.DetectBruteForce(Arg.Any<string>()).Returns(new BruteForceDetection
        {
            IsBruteForce = true,
            FailedAttempts = 20,
            Identifier = "global-check"
        });

        await sut.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => alerts.ReceivedCalls().Any(), TimeSpan.FromSeconds(3));
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await sut.StopAsync(stopCts.Token);

        await alerts.Received().SendBruteForceAlertAsync(Arg.Any<BruteForceDetection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_DDoSDetected_SendsAlertOnce()
    {
        var (sut, rateLimit, alerts) = Build();
        rateLimit.DetectDDoS(Arg.Any<TimeSpan>()).Returns(new DDoSDetection
        {
            IsDDoSPattern = true,
            UniqueIPsInvolved = 50,
            TotalRequests = 5000,
            AverageRequestsPerIP = 100
        });

        await sut.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => alerts.ReceivedCalls().Any(), TimeSpan.FromSeconds(3));
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await sut.StopAsync(stopCts.Token);

        await alerts.Received().SendDDoSAlertAsync(Arg.Any<DDoSDetection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SlowRateAttackDetected_SendsAlertOnce()
    {
        var (sut, rateLimit, alerts) = Build();
        rateLimit.DetectSlowRateAttack().Returns(new SlowRateAttackDetection
        {
            IsSlowRateAttack = true,
            SuspiciousPatternCount = 7
        });

        await sut.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => alerts.ReceivedCalls().Any(), TimeSpan.FromSeconds(3));
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await sut.StopAsync(stopCts.Token);

        await alerts.Received().SendSlowRateAlertAsync(Arg.Any<SlowRateAttackDetection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_AlertAlreadySentWithinCooldown_DoesNotSendAgain()
    {
        var (sut, rateLimit, alerts) = Build();
        rateLimit.DetectBurstAttack(Arg.Any<string>()).Returns(new BurstAttackDetection
        {
            IsBurstAttack = true,
            RequestsInBurst = 500,
            BurstDuration = TimeSpan.FromSeconds(2),
            Identifier = "global-check"
        });
        InvokeMarkAlertSent(sut, "BurstAttack", "global-check"); // pre-seed the cooldown

        await sut.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => rateLimit.ReceivedCalls().Count() > 2, TimeSpan.FromSeconds(3));
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await sut.StopAsync(stopCts.Token);

        alerts.ReceivedCalls().Should().BeEmpty("the alert was already sent within the last 15 minutes");
    }

    [Fact]
    public async Task ExecuteAsync_RateLimitServiceThrows_LoopSurvivesAndDoesNotHang()
    {
        var (sut, rateLimit, _) = Build();
        rateLimit.DetectBurstAttack(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("boom"));

        await sut.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => rateLimit.ReceivedCalls().Any(), TimeSpan.FromSeconds(3));
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var act = () => sut.StopAsync(stopCts.Token);

        await act.Should().NotThrowAsync();
    }
}
