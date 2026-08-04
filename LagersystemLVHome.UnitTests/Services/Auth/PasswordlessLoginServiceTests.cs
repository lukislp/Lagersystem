using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Auth;

public class PasswordlessLoginServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:ApplicationUrl"] = "https://app.test"
            })
            .Build();

    private static async Task SeedUserAsync(IDbContextFactory<InventoryDbContext> factory, int id, string email, bool passwordless = true,
        UserApprovalStatus status = UserApprovalStatus.Approved, bool isActive = true, bool isDeleted = false)
    {
        await using var db = factory.CreateDbContext();
        db.Users.Add(new User
        {
            Id = id,
            Username = $"u{id}",
            Email = email,
            PasswordHash = "h",
            IsActive = isActive,
            IsDeleted = isDeleted,
            PasswordlessEnabled = passwordless,
            ApprovalStatus = status
        });
        await db.SaveChangesAsync();
    }

    private static PasswordlessLoginService Build(IDbContextFactory<InventoryDbContext> factory, IEmailService email, IAuditService? audit = null)
        => new(factory, email, NullLogger<PasswordlessLoginService>.Instance, BuildConfig(), audit);

    [Fact]
    public async Task SendMagicLinkAsync_UnknownEmail_ReturnsTrueWithoutSend()
    {
        var factory = CreateFactory(nameof(SendMagicLinkAsync_UnknownEmail_ReturnsTrueWithoutSend));
        var email = Substitute.For<IEmailService>();

        var ok = await Build(factory, email).SendMagicLinkAsync("nobody@x");

        ok.Should().BeTrue();
        await email.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default!, default!);
    }

    [Fact]
    public async Task SendMagicLinkAsync_PasswordlessDisabled_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(SendMagicLinkAsync_PasswordlessDisabled_ReturnsFalse));
        await SeedUserAsync(factory, 1, "u@x", passwordless: false);
        var email = Substitute.For<IEmailService>();

        var ok = await Build(factory, email).SendMagicLinkAsync("u@x");

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task SendMagicLinkAsync_PendingUser_ReturnsTrueWithoutSend()
    {
        var factory = CreateFactory(nameof(SendMagicLinkAsync_PendingUser_ReturnsTrueWithoutSend));
        await SeedUserAsync(factory, 1, "u@x", status: UserApprovalStatus.Pending);
        var email = Substitute.For<IEmailService>();

        var ok = await Build(factory, email).SendMagicLinkAsync("u@x");

        ok.Should().BeTrue();
        await email.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default!, default!);
    }

    [Fact]
    public async Task SendMagicLinkAsync_HappyPath_PersistsTokenAndSendsEmail()
    {
        var factory = CreateFactory(nameof(SendMagicLinkAsync_HappyPath_PersistsTokenAndSendsEmail));
        await SeedUserAsync(factory, 1, "u@x");
        var email = Substitute.For<IEmailService>();

        var ok = await Build(factory, email).SendMagicLinkAsync("u@x", ipAddress: "1.1.1.1");

        ok.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        var token = await db.MagicLinkTokens.SingleAsync();
        token.IpAddress.Should().Be("1.1.1.1");
        token.IsUsed.Should().BeFalse();
        token.Token.Length.Should().BeGreaterThan(0);
        await email.Received(1).SendEmailAsync(
            "u@x",
            Arg.Any<string>(),
            Arg.Is<string>(b => b.Contains("https://app.test/login/magic?token=")),
            Arg.Any<bool>());
    }

    [Fact]
    public async Task SendMagicLinkAsync_RemovesPreviousUnusedTokens()
    {
        var factory = CreateFactory(nameof(SendMagicLinkAsync_RemovesPreviousUnusedTokens));
        await SeedUserAsync(factory, 1, "u@x");
        await using (var db = factory.CreateDbContext())
        {
            db.MagicLinkTokens.Add(new MagicLinkToken { UserId = 1, Token = "old", ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();

        await Build(factory, email).SendMagicLinkAsync("u@x");

        await using var db2 = factory.CreateDbContext();
        var tokens = await db2.MagicLinkTokens.ToListAsync();
        tokens.Should().ContainSingle().Which.Token.Should().NotBe("old");
    }

    [Fact]
    public async Task ValidateMagicLinkAsync_InvalidToken_ReturnsNull()
    {
        var factory = CreateFactory(nameof(ValidateMagicLinkAsync_InvalidToken_ReturnsNull));
        var email = Substitute.For<IEmailService>();

        (await Build(factory, email).ValidateMagicLinkAsync("not-a-token")).Should().BeNull();
    }

    [Fact]
    public async Task ValidateMagicLinkAsync_ExpiredToken_ReturnsNull()
    {
        var factory = CreateFactory(nameof(ValidateMagicLinkAsync_ExpiredToken_ReturnsNull));
        await SeedUserAsync(factory, 1, "u@x");
        await using (var db = factory.CreateDbContext())
        {
            db.MagicLinkTokens.Add(new MagicLinkToken { UserId = 1, Token = "tok", ExpiresAt = DateTime.UtcNow.AddMinutes(-1) });
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();

        (await Build(factory, email).ValidateMagicLinkAsync("tok")).Should().BeNull();
    }

    [Fact]
    public async Task ValidateMagicLinkAsync_ValidToken_LogsInUserAndMarksUsed()
    {
        var factory = CreateFactory(nameof(ValidateMagicLinkAsync_ValidToken_LogsInUserAndMarksUsed));
        await using (var db = factory.CreateDbContext())
        {
            var warehouse = new Warehouse { Id = 1, Name = "WH", Address = "x", IsActive = true };
            db.Warehouses.Add(warehouse);
            var user = new User
            {
                Id = 1,
                Username = "u1",
                Email = "u@x",
                PasswordHash = "h",
                IsActive = true,
                PasswordlessEnabled = true,
                ApprovalStatus = UserApprovalStatus.Approved,
                WarehouseId = 1,
                Warehouse = warehouse
            };
            db.Users.Add(user);
            db.MagicLinkTokens.Add(new MagicLinkToken { User = user, Token = "tok", ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();

        var loggedIn = await Build(factory, email).ValidateMagicLinkAsync("tok", ipAddress: "9.9.9.9");

        loggedIn.Should().NotBeNull();
        loggedIn!.Id.Should().Be(1);
        await using var db2 = factory.CreateDbContext();
        (await db2.MagicLinkTokens.SingleAsync()).IsUsed.Should().BeTrue();
        var u = await db2.Users.SingleAsync();
        u.LastLoginIp.Should().Be("9.9.9.9");
    }

    [Fact]
    public async Task ValidateMagicLinkAsync_DeactivatedUser_ReturnsNull()
    {
        var factory = CreateFactory(nameof(ValidateMagicLinkAsync_DeactivatedUser_ReturnsNull));
        await SeedUserAsync(factory, 1, "u@x", isActive: false);
        await using (var db = factory.CreateDbContext())
        {
            db.MagicLinkTokens.Add(new MagicLinkToken { UserId = 1, Token = "tok", ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();

        (await Build(factory, email).ValidateMagicLinkAsync("tok")).Should().BeNull();
    }

    [Fact]
    public async Task IsPasswordlessEnabledAsync_ReportsConfigState()
    {
        var factory = CreateFactory(nameof(IsPasswordlessEnabledAsync_ReportsConfigState));
        await SeedUserAsync(factory, 1, "u@x", passwordless: true);
        await SeedUserAsync(factory, 2, "v@x", passwordless: false);
        var email = Substitute.For<IEmailService>();
        var sut = Build(factory, email);

        (await sut.IsPasswordlessEnabledAsync("u@x")).Should().BeTrue();
        (await sut.IsPasswordlessEnabledAsync("v@x")).Should().BeFalse();
        (await sut.IsPasswordlessEnabledAsync("missing@x")).Should().BeFalse();
    }
}
