using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Infrastructure.HostedServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace LagersystemLVHome.UnitTests.Infrastructure.HostedServices;

using UserSession = LagersystemLVHome.Domain.Models.UserSession;

/// <summary>
/// Covers <see cref="SessionCleanupHostedService"/>. The private <c>CleanupSessionsAsync</c>
/// method is the testable seam extracted from the polling loop (which waits 1 minute before the
/// first run and 5 minutes between runs) - it is invoked directly via reflection so tests run
/// instantly instead of waiting on real wall-clock delays. A separate smoke test drives the
/// actual <see cref="BackgroundService"/> lifecycle (StartAsync/StopAsync) with an immediate stop
/// to prove the outer loop itself does not hang or throw.
/// </summary>
public sealed class SessionCleanupHostedServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static (SessionCleanupHostedService sut, ISessionMonitorService sessionMonitor) Build(
        IDbContextFactory<InventoryDbContext> factory)
    {
        var sessionMonitor = Substitute.For<ISessionMonitorService>();

        var services = new ServiceCollection();
        services.AddScoped(_ => factory);
        services.AddScoped(_ => sessionMonitor);
        var provider = services.BuildServiceProvider();

        var sut = new SessionCleanupHostedService(provider, NullLogger<SessionCleanupHostedService>.Instance);
        return (sut, sessionMonitor);
    }

    private static Task InvokeCleanupSessionsAsync(SessionCleanupHostedService sut, CancellationToken token = default)
    {
        var method = typeof(SessionCleanupHostedService).GetMethod("CleanupSessionsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(sut, new object[] { token })!;
    }

    private static UserSession MakeSession(string sessionId, bool isActive, DateTime lastActivity, DateTime startTime) => new()
    {
        SessionId = sessionId,
        UserId = 1,
        Username = "u1",
        WarehouseId = 1,
        IsActive = isActive,
        LastActivity = lastActivity,
        StartTime = startTime
    };

    [Fact]
    public async Task CleanupSessionsAsync_InactiveSessionOverThreshold_TerminatesAndTriggersForceLogout()
    {
        var factory = CreateFactory(nameof(CleanupSessionsAsync_InactiveSessionOverThreshold_TerminatesAndTriggersForceLogout));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s-inactive", isActive: true, lastActivity: DateTime.UtcNow.AddMinutes(-45), startTime: DateTime.UtcNow.AddMinutes(-45)));
            await db.SaveChangesAsync();
        }
        var (sut, sessionMonitor) = Build(factory);

        await InvokeCleanupSessionsAsync(sut);

        await using var verifyDb = factory.CreateDbContext();
        var session = await verifyDb.UserSessions.SingleAsync(s => s.SessionId == "s-inactive");
        session.IsActive.Should().BeFalse();
        session.EndReason.Should().Be(SessionEndReason.Timeout);
        await sessionMonitor.Received(1).ForceTerminateSessionAsync("s-inactive", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CleanupSessionsAsync_RecentlyActiveSession_IsNotTouched()
    {
        var factory = CreateFactory(nameof(CleanupSessionsAsync_RecentlyActiveSession_IsNotTouched));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s-fresh", isActive: true, lastActivity: DateTime.UtcNow, startTime: DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var (sut, sessionMonitor) = Build(factory);

        await InvokeCleanupSessionsAsync(sut);

        await using var verifyDb = factory.CreateDbContext();
        var session = await verifyDb.UserSessions.SingleAsync(s => s.SessionId == "s-fresh");
        session.IsActive.Should().BeTrue();
        await sessionMonitor.DidNotReceiveWithAnyArgs().ForceTerminateSessionAsync(default!, default!, default);
    }

    [Fact]
    public async Task CleanupSessionsAsync_ForceTerminateThrows_DbStillUpdatedAndNoExceptionPropagates()
    {
        var factory = CreateFactory(nameof(CleanupSessionsAsync_ForceTerminateThrows_DbStillUpdatedAndNoExceptionPropagates));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s-inactive", isActive: true, lastActivity: DateTime.UtcNow.AddMinutes(-45), startTime: DateTime.UtcNow.AddMinutes(-45)));
            await db.SaveChangesAsync();
        }
        var (sut, sessionMonitor) = Build(factory);
        sessionMonitor.ForceTerminateSessionAsync(default!, default!, default)
            .ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("monitor unavailable")));

        var act = () => InvokeCleanupSessionsAsync(sut);

        await act.Should().NotThrowAsync();
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.UserSessions.SingleAsync(s => s.SessionId == "s-inactive")).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task CleanupSessionsAsync_OldTerminatedSession_IsDeletedWithItsActivities()
    {
        var factory = CreateFactory(nameof(CleanupSessionsAsync_OldTerminatedSession_IsDeletedWithItsActivities));
        int sessionRowId;
        await using (var db = factory.CreateDbContext())
        {
            var session = MakeSession("s-old", isActive: false, lastActivity: DateTime.UtcNow.AddDays(-40), startTime: DateTime.UtcNow.AddDays(-40));
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();
            sessionRowId = session.Id;
            db.SessionActivities.Add(new SessionActivity { SessionId = sessionRowId, ActivityType = "PageView", Timestamp = DateTime.UtcNow.AddDays(-40) });
            await db.SaveChangesAsync();
        }
        var (sut, _) = Build(factory);

        await InvokeCleanupSessionsAsync(sut);

        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.UserSessions.AnyAsync(s => s.SessionId == "s-old")).Should().BeFalse();
        (await verifyDb.SessionActivities.AnyAsync(a => a.SessionId == sessionRowId)).Should().BeFalse();
    }

    [Fact]
    public async Task CleanupSessionsAsync_RecentTerminatedSession_IsKept()
    {
        var factory = CreateFactory(nameof(CleanupSessionsAsync_RecentTerminatedSession_IsKept));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("s-recent-terminated", isActive: false, lastActivity: DateTime.UtcNow.AddDays(-1), startTime: DateTime.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
        }
        var (sut, _) = Build(factory);

        await InvokeCleanupSessionsAsync(sut);

        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.UserSessions.AnyAsync(s => s.SessionId == "s-recent-terminated")).Should().BeTrue();
    }

    [Fact]
    public async Task CleanupSessionsAsync_NoSessionsAtAll_CompletesWithoutThrowing()
    {
        var factory = CreateFactory(nameof(CleanupSessionsAsync_NoSessionsAtAll_CompletesWithoutThrowing));
        var (sut, _) = Build(factory);

        var act = () => InvokeCleanupSessionsAsync(sut);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_StartThenImmediateStop_DoesNotHangOrThrow()
    {
        var factory = CreateFactory(nameof(ExecuteAsync_StartThenImmediateStop_DoesNotHangOrThrow));
        var (sut, _) = Build(factory);

        await sut.StartAsync(CancellationToken.None);
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var act = () => sut.StopAsync(stopCts.Token);

        await act.Should().NotThrowAsync();
    }
}
