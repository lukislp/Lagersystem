using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Infrastructure.HostedServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace LagersystemLVHome.UnitTests.Infrastructure.HostedServices;

/// <summary>
/// Covers <see cref="CloudflareAutoEscalationService"/>. The private <c>CheckAndEscalateAsync</c>
/// method (the actual business logic) is invoked directly via reflection, bypassing
/// <c>ExecuteAsync</c>'s 1-minute startup delay and 1-minute polling loop entirely - those outer
/// timing concerns are covered separately by a StartAsync/StopAsync smoke test.
/// </summary>
public sealed class CloudflareAutoEscalationServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private sealed record Sut(
        CloudflareAutoEscalationService Service,
        ICloudflareService Cloudflare,
        INotificationService? Notification,
        IAuthService? Auth,
        IDbContextFactory<InventoryDbContext> Factory);

    private static Sut Build(
        IDbContextFactory<InventoryDbContext> factory,
        bool registerNotification = true,
        bool registerAuth = true,
        CloudflareSettings? settings = null)
    {
        var cloudflare = Substitute.For<ICloudflareService>();
        var notification = registerNotification ? Substitute.For<INotificationService>() : null;
        var auth = registerAuth ? Substitute.For<IAuthService>() : null;

        var services = new ServiceCollection();
        services.AddScoped(_ => cloudflare);
        services.AddScoped(_ => factory);
        if (notification is not null) services.AddScoped(_ => notification);
        if (auth is not null) services.AddScoped(_ => auth);
        var provider = services.BuildServiceProvider();

        var service = new CloudflareAutoEscalationService(
            NullLogger<CloudflareAutoEscalationService>.Instance,
            provider,
            Options.Create(settings ?? new CloudflareSettings()));

        return new Sut(service, cloudflare, notification, auth, factory);
    }

    private static Task InvokeCheckAndEscalateAsync(CloudflareAutoEscalationService sut)
    {
        var method = typeof(CloudflareAutoEscalationService).GetMethod("CheckAndEscalateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(sut, new object[] { CancellationToken.None })!;
    }

    private static CloudflareAnalytics AnalyticsWithThreats(long threatCount) => new()
    {
        Threats = new ThreatStats { All = threatCount }
    };

    [Fact]
    public async Task CheckAndEscalateAsync_CloudflareDisabled_DoesNothing()
    {
        var s = Build(CreateFactory(nameof(CheckAndEscalateAsync_CloudflareDisabled_DoesNothing)));
        s.Cloudflare.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(false);

        await InvokeCheckAndEscalateAsync(s.Service);

        await s.Cloudflare.DidNotReceiveWithAnyArgs().GetAnalyticsAsync(default, default);
    }

    [Fact]
    public async Task CheckAndEscalateAsync_AnalyticsNull_DoesNothing()
    {
        var s = Build(CreateFactory(nameof(CheckAndEscalateAsync_AnalyticsNull_DoesNothing)));
        s.Cloudflare.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Cloudflare.GetAnalyticsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((CloudflareAnalytics?)null);

        var act = () => InvokeCheckAndEscalateAsync(s.Service);

        await act.Should().NotThrowAsync();
        await s.Cloudflare.DidNotReceiveWithAnyArgs().GetEscalationStatusAsync(default);
    }

    [Fact]
    public async Task CheckAndEscalateAsync_NotEscalated_ThreatsBelowThreshold_DoesNotEscalate()
    {
        var settings = new CloudflareSettings();
        settings.AutoEscalation.ThreatsCountThreshold = 100;
        var s = Build(CreateFactory(nameof(CheckAndEscalateAsync_NotEscalated_ThreatsBelowThreshold_DoesNotEscalate)), settings: settings);
        s.Cloudflare.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Cloudflare.GetAnalyticsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(AnalyticsWithThreats(10));
        s.Cloudflare.GetEscalationStatusAsync(Arg.Any<CancellationToken>()).Returns(new EscalationStatus { IsEscalated = false });

        await InvokeCheckAndEscalateAsync(s.Service);

        await s.Cloudflare.DidNotReceiveWithAnyArgs().EscalateToUnderAttackAsync(default);
    }

    [Fact]
    public async Task CheckAndEscalateAsync_NotEscalated_ThreatsAtOrAboveThreshold_EscalatesAndNotifiesSuperAdmins()
    {
        var settings = new CloudflareSettings();
        settings.AutoEscalation.ThreatsCountThreshold = 5;
        var factory = CreateFactory(nameof(CheckAndEscalateAsync_NotEscalated_ThreatsAtOrAboveThreshold_EscalatesAndNotifiesSuperAdmins));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(new Warehouse { Id = 1, Name = "W1", Code = "W1", Address = "a", IsActive = true });
            db.Users.Add(new User { Id = 1, Username = "super", Email = "s@x.local", DisplayName = "Super", PasswordHash = "x", WarehouseId = 1, Role = UserRole.SuperAdmin, IsActive = true });
            db.Users.Add(new User { Id = 2, Username = "admin", Email = "a@x.local", DisplayName = "Admin", PasswordHash = "x", WarehouseId = 1, Role = UserRole.Admin, IsActive = true }); // not a super admin
            db.Users.Add(new User { Id = 3, Username = "inactive-super", Email = "i@x.local", DisplayName = "InactiveSuper", PasswordHash = "x", WarehouseId = 1, Role = UserRole.SuperAdmin, IsActive = false });
            await db.SaveChangesAsync();
        }
        var s = Build(factory, settings: settings);
        s.Cloudflare.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Cloudflare.GetAnalyticsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(AnalyticsWithThreats(10));
        s.Cloudflare.GetEscalationStatusAsync(Arg.Any<CancellationToken>()).Returns(new EscalationStatus { IsEscalated = false });
        s.Cloudflare.EscalateToUnderAttackAsync(Arg.Any<CancellationToken>()).Returns(true);

        await InvokeCheckAndEscalateAsync(s.Service);

        await s.Cloudflare.Received(1).EscalateToUnderAttackAsync(Arg.Any<CancellationToken>());
        await s.Notification!.Received(1).CreateNotificationAsync(
            1, NotificationType.SecurityAlert, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        await s.Notification.DidNotReceive().CreateNotificationAsync(
            2, Arg.Any<NotificationType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CheckAndEscalateAsync_NotEscalated_EscalateFails_DoesNotSendNotification()
    {
        var settings = new CloudflareSettings();
        settings.AutoEscalation.ThreatsCountThreshold = 1;
        var s = Build(CreateFactory(nameof(CheckAndEscalateAsync_NotEscalated_EscalateFails_DoesNotSendNotification)), settings: settings);
        s.Cloudflare.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Cloudflare.GetAnalyticsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(AnalyticsWithThreats(10));
        s.Cloudflare.GetEscalationStatusAsync(Arg.Any<CancellationToken>()).Returns(new EscalationStatus { IsEscalated = false });
        s.Cloudflare.EscalateToUnderAttackAsync(Arg.Any<CancellationToken>()).Returns(false);

        await InvokeCheckAndEscalateAsync(s.Service);

        await s.Notification!.DidNotReceiveWithAnyArgs().CreateNotificationAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task CheckAndEscalateAsync_Escalated_AutoDeEscalateWindowExpired_DeEscalatesAndNotifies()
    {
        var factory = CreateFactory(nameof(CheckAndEscalateAsync_Escalated_AutoDeEscalateWindowExpired_DeEscalatesAndNotifies));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(new Warehouse { Id = 1, Name = "W1", Code = "W1", Address = "a", IsActive = true });
            db.Users.Add(new User { Id = 1, Username = "super", Email = "s@x.local", DisplayName = "Super", PasswordHash = "x", WarehouseId = 1, Role = UserRole.SuperAdmin, IsActive = true });
            await db.SaveChangesAsync();
        }
        var s = Build(factory);
        s.Cloudflare.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Cloudflare.GetAnalyticsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(AnalyticsWithThreats(0));
        s.Cloudflare.GetEscalationStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new EscalationStatus { IsEscalated = true, AutoDeEscalateIn = TimeSpan.Zero });
        s.Cloudflare.DeEscalateFromUnderAttackAsync(Arg.Any<CancellationToken>()).Returns(true);

        await InvokeCheckAndEscalateAsync(s.Service);

        await s.Cloudflare.Received(1).DeEscalateFromUnderAttackAsync(Arg.Any<CancellationToken>());
        await s.Notification!.Received(1).CreateNotificationAsync(
            1, NotificationType.Info, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CheckAndEscalateAsync_Escalated_ThreatsDroppedBelowHalfThreshold_EarlyDeEscalates()
    {
        var settings = new CloudflareSettings();
        settings.AutoEscalation.ThreatsCountThreshold = 10;
        var s = Build(CreateFactory(nameof(CheckAndEscalateAsync_Escalated_ThreatsDroppedBelowHalfThreshold_EarlyDeEscalates)), settings: settings);
        s.Cloudflare.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Cloudflare.GetAnalyticsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(AnalyticsWithThreats(2));
        s.Cloudflare.GetEscalationStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new EscalationStatus { IsEscalated = true, AutoDeEscalateIn = TimeSpan.FromMinutes(5) });
        s.Cloudflare.DeEscalateFromUnderAttackAsync(Arg.Any<CancellationToken>()).Returns(true);

        await InvokeCheckAndEscalateAsync(s.Service);

        await s.Cloudflare.Received(1).DeEscalateFromUnderAttackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAndEscalateAsync_Escalated_ThreatsStillHighAndWindowNotExpired_NoAction()
    {
        var settings = new CloudflareSettings();
        settings.AutoEscalation.ThreatsCountThreshold = 10;
        var s = Build(CreateFactory(nameof(CheckAndEscalateAsync_Escalated_ThreatsStillHighAndWindowNotExpired_NoAction)), settings: settings);
        s.Cloudflare.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Cloudflare.GetAnalyticsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(AnalyticsWithThreats(9));
        s.Cloudflare.GetEscalationStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new EscalationStatus { IsEscalated = true, AutoDeEscalateIn = TimeSpan.FromMinutes(5) });

        await InvokeCheckAndEscalateAsync(s.Service);

        await s.Cloudflare.DidNotReceiveWithAnyArgs().DeEscalateFromUnderAttackAsync(default);
    }

    [Fact]
    public async Task CheckAndEscalateAsync_NotificationServiceNotRegistered_SkipsNotificationWithoutThrowing()
    {
        var settings = new CloudflareSettings();
        settings.AutoEscalation.ThreatsCountThreshold = 1;
        var s = Build(CreateFactory(nameof(CheckAndEscalateAsync_NotificationServiceNotRegistered_SkipsNotificationWithoutThrowing)),
            registerNotification: false, settings: settings);
        s.Cloudflare.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Cloudflare.GetAnalyticsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(AnalyticsWithThreats(10));
        s.Cloudflare.GetEscalationStatusAsync(Arg.Any<CancellationToken>()).Returns(new EscalationStatus { IsEscalated = false });
        s.Cloudflare.EscalateToUnderAttackAsync(Arg.Any<CancellationToken>()).Returns(true);

        var act = () => InvokeCheckAndEscalateAsync(s.Service);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckAndEscalateAsync_AuthServiceNotRegistered_SkipsNotificationWithoutThrowing()
    {
        var settings = new CloudflareSettings();
        settings.AutoEscalation.ThreatsCountThreshold = 1;
        var s = Build(CreateFactory(nameof(CheckAndEscalateAsync_AuthServiceNotRegistered_SkipsNotificationWithoutThrowing)),
            registerAuth: false, settings: settings);
        s.Cloudflare.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Cloudflare.GetAnalyticsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(AnalyticsWithThreats(10));
        s.Cloudflare.GetEscalationStatusAsync(Arg.Any<CancellationToken>()).Returns(new EscalationStatus { IsEscalated = false });
        s.Cloudflare.EscalateToUnderAttackAsync(Arg.Any<CancellationToken>()).Returns(true);

        var act = () => InvokeCheckAndEscalateAsync(s.Service);

        await act.Should().NotThrowAsync();
        await s.Notification!.DidNotReceiveWithAnyArgs().CreateNotificationAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task CheckAndEscalateAsync_CloudflareServiceThrows_IsCaughtAndDoesNotPropagate()
    {
        var s = Build(CreateFactory(nameof(CheckAndEscalateAsync_CloudflareServiceThrows_IsCaughtAndDoesNotPropagate)));
        s.Cloudflare.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Cloudflare.GetAnalyticsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CloudflareAnalytics?>(new InvalidOperationException("api down")));

        var act = () => InvokeCheckAndEscalateAsync(s.Service);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckAndEscalateAsync_StaleThreatHistoryEntry_IsPrunedAndExcludedFromWindowSum()
    {
        // Directly seeds a threat-history entry older than the configured time window (via
        // reflection on the private _threatHistory dictionary) to deterministically exercise the
        // pruning branch, instead of waiting real wall-clock minutes between two calls. If pruning
        // did not work, this stale entry alone would already exceed the threshold and escalate.
        var settings = new CloudflareSettings();
        settings.AutoEscalation.TimeWindowMinutes = 5;
        settings.AutoEscalation.ThreatsCountThreshold = 50;
        var s = Build(CreateFactory(nameof(CheckAndEscalateAsync_StaleThreatHistoryEntry_IsPrunedAndExcludedFromWindowSum)), settings: settings);
        s.Cloudflare.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Cloudflare.GetAnalyticsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(AnalyticsWithThreats(1));
        s.Cloudflare.GetEscalationStatusAsync(Arg.Any<CancellationToken>()).Returns(new EscalationStatus { IsEscalated = false });

        var historyField = typeof(CloudflareAutoEscalationService).GetField("_threatHistory", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var history = (Dictionary<DateTime, int>)historyField.GetValue(s.Service)!;
        history[DateTime.UtcNow.AddMinutes(-30)] = 1000; // far outside the 5-minute window - must be pruned

        await InvokeCheckAndEscalateAsync(s.Service);

        history.Keys.Should().NotContain(k => k < DateTime.UtcNow.AddMinutes(-5), "the stale entry must have been pruned");
        await s.Cloudflare.DidNotReceiveWithAnyArgs().EscalateToUnderAttackAsync(default);
    }

    [Fact]
    public async Task CheckAndEscalateAsync_EscalationNotificationThrows_IsCaughtAndDoesNotPropagate()
    {
        var settings = new CloudflareSettings();
        settings.AutoEscalation.ThreatsCountThreshold = 1;
        var factory = CreateFactory(nameof(CheckAndEscalateAsync_EscalationNotificationThrows_IsCaughtAndDoesNotPropagate));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(new Warehouse { Id = 1, Name = "W1", Code = "W1", Address = "a", IsActive = true });
            db.Users.Add(new User { Id = 1, Username = "super", Email = "s@x.local", DisplayName = "Super", PasswordHash = "x", WarehouseId = 1, Role = UserRole.SuperAdmin, IsActive = true });
            await db.SaveChangesAsync();
        }
        var s = Build(factory, settings: settings);
        s.Cloudflare.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Cloudflare.GetAnalyticsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(AnalyticsWithThreats(10));
        s.Cloudflare.GetEscalationStatusAsync(Arg.Any<CancellationToken>()).Returns(new EscalationStatus { IsEscalated = false });
        s.Cloudflare.EscalateToUnderAttackAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Notification!.CreateNotificationAsync(default, default, default!, default!, default)
            .ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("notify down")));

        var act = () => InvokeCheckAndEscalateAsync(s.Service);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckAndEscalateAsync_DeEscalationNotificationThrows_IsCaughtAndDoesNotPropagate()
    {
        var factory = CreateFactory(nameof(CheckAndEscalateAsync_DeEscalationNotificationThrows_IsCaughtAndDoesNotPropagate));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(new Warehouse { Id = 1, Name = "W1", Code = "W1", Address = "a", IsActive = true });
            db.Users.Add(new User { Id = 1, Username = "super", Email = "s@x.local", DisplayName = "Super", PasswordHash = "x", WarehouseId = 1, Role = UserRole.SuperAdmin, IsActive = true });
            await db.SaveChangesAsync();
        }
        var s = Build(factory);
        s.Cloudflare.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Cloudflare.GetAnalyticsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(AnalyticsWithThreats(0));
        s.Cloudflare.GetEscalationStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new EscalationStatus { IsEscalated = true, AutoDeEscalateIn = TimeSpan.Zero });
        s.Cloudflare.DeEscalateFromUnderAttackAsync(Arg.Any<CancellationToken>()).Returns(true);
        s.Notification!.CreateNotificationAsync(default, default, default!, default!, default)
            .ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("notify down")));

        var act = () => InvokeCheckAndEscalateAsync(s.Service);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_StartThenImmediateStop_DoesNotHangOrThrow()
    {
        var s = Build(CreateFactory(nameof(ExecuteAsync_StartThenImmediateStop_DoesNotHangOrThrow)));

        await s.Service.StartAsync(CancellationToken.None);
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var act = () => s.Service.StopAsync(stopCts.Token);

        await act.Should().NotThrowAsync();
    }
}
