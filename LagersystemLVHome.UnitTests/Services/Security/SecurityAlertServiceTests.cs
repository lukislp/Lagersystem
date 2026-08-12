using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.UnitTests.Services.Security;

public class SecurityAlertServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static SecurityAlertService Build(
        IDbContextFactory<InventoryDbContext> factory,
        INotificationService notif,
        IEmailService email,
        SecurityAlertsSettings settings)
        => new(factory, notif, email, Options.Create(settings), new ConfigurationBuilder().Build(), NullLogger<SecurityAlertService>.Instance);

    private static async Task SeedSuperAdminAsync(IDbContextFactory<InventoryDbContext> factory, string email = "admin@test.local")
    {
        await using var db = factory.CreateDbContext();
        db.Users.Add(new User
        {
            Username = "superadmin",
            Email = email,
            PasswordHash = "x",
            Role = UserRole.SuperAdmin,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SendBurstAttackAlertAsync_WhenDisabled_DoesNothing()
    {
        var factory = CreateFactory(nameof(SendBurstAttackAlertAsync_WhenDisabled_DoesNothing));
        var notif = Substitute.For<INotificationService>();
        var email = Substitute.For<IEmailService>();
        var settings = new SecurityAlertsSettings { Enabled = false };
        var sut = Build(factory, notif, email, settings);

        await sut.SendBurstAttackAlertAsync(new BurstAttackDetection { Identifier = "1.2.3.4" });

        await notif.DidNotReceiveWithAnyArgs().CreateNotificationAsync(default, default, default!, default!);
        await email.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default!, default!);
    }

    [Fact]
    public async Task SendBurstAttackAlertAsync_NoSuperAdmin_DoesNotCreateNotification()
    {
        var factory = CreateFactory(nameof(SendBurstAttackAlertAsync_NoSuperAdmin_DoesNotCreateNotification));
        var notif = Substitute.For<INotificationService>();
        var email = Substitute.For<IEmailService>();
        var sut = Build(factory, notif, email, new SecurityAlertsSettings { Enabled = true });

        await sut.SendBurstAttackAlertAsync(new BurstAttackDetection
        {
            Identifier = "1.2.3.4",
            RequestsInBurst = 100,
            BurstDuration = TimeSpan.FromSeconds(1),
            RequestsPerSecond = 100
        });

        await notif.DidNotReceiveWithAnyArgs().CreateNotificationAsync(default, default, default!, default!);
    }

    [Fact]
    public async Task SendBurstAttackAlertAsync_WithSuperAdmin_CreatesNotification()
    {
        var factory = CreateFactory(nameof(SendBurstAttackAlertAsync_WithSuperAdmin_CreatesNotification));
        await SeedSuperAdminAsync(factory);
        var notif = Substitute.For<INotificationService>();
        var email = Substitute.For<IEmailService>();
        var settings = new SecurityAlertsSettings { Enabled = true };
        settings.BurstAttack.EmailEnabled = false;
        var sut = Build(factory, notif, email, settings);

        await sut.SendBurstAttackAlertAsync(new BurstAttackDetection
        {
            Identifier = "1.2.3.4",
            RequestsInBurst = 50,
            BurstDuration = TimeSpan.FromSeconds(2),
            RequestsPerSecond = 25
        });

        await notif.Received(1).CreateNotificationAsync(
            Arg.Any<int>(),
            NotificationType.SecurityAlert,
            "Burst Attack erkannt",
            Arg.Is<string>(m => m.Contains("1.2.3.4")),
            Arg.Any<string?>(),
            Arg.Any<NotificationChannel>(),
            Arg.Any<CancellationToken>());
        await email.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default!, default!);
    }

    [Fact]
    public async Task SendBurstAttackAlertAsync_EmailEnabled_SendsEmail()
    {
        var factory = CreateFactory(nameof(SendBurstAttackAlertAsync_EmailEnabled_SendsEmail));
        await SeedSuperAdminAsync(factory, "alert@host.local");
        var notif = Substitute.For<INotificationService>();
        var email = Substitute.For<IEmailService>();
        var settings = new SecurityAlertsSettings { Enabled = true };
        settings.BurstAttack.EmailEnabled = true;
        var sut = Build(factory, notif, email, settings);

        await sut.SendBurstAttackAlertAsync(new BurstAttackDetection { Identifier = "x" });

        await email.Received(1).SendEmailAsync(
            "alert@host.local",
            Arg.Is<string>(s => s.StartsWith("[SECURITY ALERT]")),
            Arg.Any<string>());
    }

    [Fact]
    public async Task SendBruteForceAlertAsync_WithSuperAdmin_CreatesNotification()
    {
        var factory = CreateFactory(nameof(SendBruteForceAlertAsync_WithSuperAdmin_CreatesNotification));
        await SeedSuperAdminAsync(factory);
        var notif = Substitute.For<INotificationService>();
        var email = Substitute.For<IEmailService>();
        var settings = new SecurityAlertsSettings { Enabled = true };
        settings.BruteForce.EmailEnabled = false;
        var sut = Build(factory, notif, email, settings);

        await sut.SendBruteForceAlertAsync(new BruteForceDetection
        {
            Identifier = "evil",
            FailedAttempts = 12,
            AttackDuration = TimeSpan.FromMinutes(3),
            TargetedEndpoints = new() { "/login" }
        });

        await notif.Received(1).CreateNotificationAsync(
            Arg.Any<int>(),
            NotificationType.SecurityAlert,
            "Brute-Force Angriff",
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<NotificationChannel>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendDDoSAlertAsync_WithSuperAdmin_CreatesNotification()
    {
        var factory = CreateFactory(nameof(SendDDoSAlertAsync_WithSuperAdmin_CreatesNotification));
        await SeedSuperAdminAsync(factory);
        var notif = Substitute.For<INotificationService>();
        var email = Substitute.For<IEmailService>();
        var settings = new SecurityAlertsSettings { Enabled = true };
        settings.DDoS.EmailEnabled = false;
        var sut = Build(factory, notif, email, settings);

        await sut.SendDDoSAlertAsync(new DDoSDetection
        {
            UniqueIPsInvolved = 100,
            TotalRequests = 5000,
            AverageRequestsPerIP = 50,
            SuspiciousIPs = new() { "1.1.1.1", "2.2.2.2" }
        });

        await notif.Received(1).CreateNotificationAsync(
            Arg.Any<int>(),
            NotificationType.SecurityAlert,
            "DDoS Pattern erkannt",
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<NotificationChannel>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendSlowRateAlertAsync_WithSuperAdmin_CreatesNotification()
    {
        var factory = CreateFactory(nameof(SendSlowRateAlertAsync_WithSuperAdmin_CreatesNotification));
        await SeedSuperAdminAsync(factory);
        var notif = Substitute.For<INotificationService>();
        var email = Substitute.For<IEmailService>();
        var settings = new SecurityAlertsSettings { Enabled = true };
        settings.SlowRate.EmailEnabled = false;
        var sut = Build(factory, notif, email, settings);

        await sut.SendSlowRateAlertAsync(new SlowRateAttackDetection
        {
            SuspiciousPatternCount = 5,
            ConsistentOffenders = new() { "9.9.9.9" }
        });

        await notif.Received(1).CreateNotificationAsync(
            Arg.Any<int>(),
            NotificationType.SecurityAlert,
            "Slow-Rate Attack Pattern",
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<NotificationChannel>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendBruteForceAlertAsync_WhenDisabled_DoesNothing()
    {
        var factory = CreateFactory(nameof(SendBruteForceAlertAsync_WhenDisabled_DoesNothing));
        var notif = Substitute.For<INotificationService>();
        var email = Substitute.For<IEmailService>();
        var sut = Build(factory, notif, email, new SecurityAlertsSettings { Enabled = false });

        await sut.SendBruteForceAlertAsync(new BruteForceDetection { Identifier = "x" });

        await notif.DidNotReceiveWithAnyArgs().CreateNotificationAsync(default, default, default!, default!);
    }

    [Fact]
    public async Task SendDDoSAlertAsync_WhenDisabled_DoesNothing()
    {
        var factory = CreateFactory(nameof(SendDDoSAlertAsync_WhenDisabled_DoesNothing));
        var notif = Substitute.For<INotificationService>();
        var email = Substitute.For<IEmailService>();
        var sut = Build(factory, notif, email, new SecurityAlertsSettings { Enabled = false });

        await sut.SendDDoSAlertAsync(new DDoSDetection());

        await notif.DidNotReceiveWithAnyArgs().CreateNotificationAsync(default, default, default!, default!);
    }

    [Fact]
    public async Task SendSlowRateAlertAsync_WhenDisabled_DoesNothing()
    {
        var factory = CreateFactory(nameof(SendSlowRateAlertAsync_WhenDisabled_DoesNothing));
        var notif = Substitute.For<INotificationService>();
        var email = Substitute.For<IEmailService>();
        var sut = Build(factory, notif, email, new SecurityAlertsSettings { Enabled = false });

        await sut.SendSlowRateAlertAsync(new SlowRateAttackDetection());

        await notif.DidNotReceiveWithAnyArgs().CreateNotificationAsync(default, default, default!, default!);
    }
}
