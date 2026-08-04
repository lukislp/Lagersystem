using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Auth;

public class TwoFactorManagementServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static (TwoFactorManagementService sut, IDbContextFactory<InventoryDbContext> factory,
        ITwoFactorService twoFactor, IEmailOtpService emailOtp, IAuditService audit) CreateSut(string dbName)
    {
        var factory = CreateFactory(dbName);
        var twoFactor = Substitute.For<ITwoFactorService>();
        var emailOtp = Substitute.For<IEmailOtpService>();
        var audit = Substitute.For<IAuditService>();
        var http = Substitute.For<IHttpContextAccessor>();
        http.HttpContext.Returns((HttpContext?)null);

        var sut = new TwoFactorManagementService(
            factory,
            NullLogger<TwoFactorManagementService>.Instance,
            twoFactor,
            emailOtp,
            audit,
            http);

        return (sut, factory, twoFactor, emailOtp, audit);
    }

    private static async Task SeedWarehouseAsync(IDbContextFactory<InventoryDbContext> factory)
    {
        await using var db = factory.CreateDbContext();
        if (await db.Warehouses.AnyAsync(w => w.Id == 1)) return;
        db.Warehouses.Add(new Warehouse { Id = 1, Name = "W", Code = "W", IsActive = true, MaxUsers = 10 });
        await db.SaveChangesAsync();
    }

    private static async Task<User> SeedUserAsync(
        IDbContextFactory<InventoryDbContext> factory,
        string password = "Correct!1",
        Action<User>? mutate = null)
    {
        await SeedWarehouseAsync(factory);
        await using var db = factory.CreateDbContext();
        var u = new User
        {
            Username = "alice",
            Email = "alice@t.local",
            DisplayName = "Alice",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsActive = true,
            ApprovalStatus = UserApprovalStatus.Approved,
            WarehouseId = 1,
            Role = UserRole.User
        };
        mutate?.Invoke(u);
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    [Fact]
    public async Task Enable2FAAsync_ValidCode_EnablesAndStoresRecoveryCodes()
    {
        var (sut, factory, twoFactor, _, audit) = CreateSut(nameof(Enable2FAAsync_ValidCode_EnablesAndStoresRecoveryCodes));
        var user = await SeedUserAsync(factory);
        twoFactor.ValidateCode("SEC", "123456").Returns(true);
        twoFactor.GenerateRecoveryCodes().Returns(["a", "b", "c"]);

        var ok = await sut.Enable2FAAsync(user.Id, "SEC", "123456");

        ok.Should().BeTrue();
        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(user.Id))!;
        refreshed.TwoFactorEnabled.Should().BeTrue();
        refreshed.TwoFactorSecret.Should().Be("SEC");
        refreshed.TwoFactorRecoveryCodes.Should().Contain("\"a\"");
        await audit.Received(1).LogAsync(
            "2FA_ENABLED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Info);
    }

    [Fact]
    public async Task Enable2FAAsync_InvalidCode_ReturnsFalseAndAudits()
    {
        var (sut, factory, twoFactor, _, audit) = CreateSut(nameof(Enable2FAAsync_InvalidCode_ReturnsFalseAndAudits));
        var user = await SeedUserAsync(factory);
        twoFactor.ValidateCode("SEC", "000000").Returns(false);

        var ok = await sut.Enable2FAAsync(user.Id, "SEC", "000000");

        ok.Should().BeFalse();
        await audit.Received(1).LogAsync(
            "2FA_ENABLE_FAILED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Warning);
    }

    [Fact]
    public async Task Enable2FAAsync_NoTwoFactorService_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(Enable2FAAsync_NoTwoFactorService_ReturnsFalse));
        await SeedWarehouseAsync(factory);
        var sut = new TwoFactorManagementService(factory, NullLogger<TwoFactorManagementService>.Instance);

        (await sut.Enable2FAAsync(1, "s", "c")).Should().BeFalse();
    }

    [Fact]
    public async Task Disable2FAAsync_WrongPassword_ReturnsFalseAndAudits()
    {
        var (sut, factory, _, _, audit) = CreateSut(nameof(Disable2FAAsync_WrongPassword_ReturnsFalseAndAudits));
        var user = await SeedUserAsync(factory, "Correct!1", u => u.TwoFactorEnabled = true);

        var ok = await sut.Disable2FAAsync(user.Id, "WRONG");

        ok.Should().BeFalse();
        await audit.Received(1).LogAsync(
            "2FA_DISABLE_FAILED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Warning);
    }

    [Fact]
    public async Task Disable2FAAsync_CorrectPassword_ClearsSecrets()
    {
        var (sut, factory, _, _, audit) = CreateSut(nameof(Disable2FAAsync_CorrectPassword_ClearsSecrets));
        var user = await SeedUserAsync(factory, "Correct!1", u =>
        {
            u.TwoFactorEnabled = true;
            u.TwoFactorSecret = "SEC";
            u.TwoFactorRecoveryCodes = "[]";
        });

        var ok = await sut.Disable2FAAsync(user.Id, "Correct!1");

        ok.Should().BeTrue();
        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(user.Id))!;
        refreshed.TwoFactorEnabled.Should().BeFalse();
        refreshed.TwoFactorSecret.Should().BeNull();
        refreshed.TwoFactorRecoveryCodes.Should().BeNull();
        await audit.Received(1).LogAsync(
            "2FA_DISABLED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Info);
    }

    [Fact]
    public async Task EnableEmailOtpAsync_WithoutAuthenticator_SetsAsPreferred()
    {
        var (sut, factory, _, _, _) = CreateSut(nameof(EnableEmailOtpAsync_WithoutAuthenticator_SetsAsPreferred));
        var user = await SeedUserAsync(factory);

        (await sut.EnableEmailOtpAsync(user.Id)).Should().BeTrue();

        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(user.Id))!;
        refreshed.EmailOtpEnabled.Should().BeTrue();
        refreshed.Preferred2FAMethod.Should().Be(TwoFactorMethods.EmailOtp);
    }

    [Fact]
    public async Task EnableEmailOtpAsync_WithAuthenticator_DoesNotOverridePreferred()
    {
        var (sut, factory, _, _, _) = CreateSut(nameof(EnableEmailOtpAsync_WithAuthenticator_DoesNotOverridePreferred));
        var user = await SeedUserAsync(factory, mutate: u =>
        {
            u.TwoFactorEnabled = true;
            u.Preferred2FAMethod = TwoFactorMethods.Authenticator;
        });

        (await sut.EnableEmailOtpAsync(user.Id)).Should().BeTrue();

        await using var verify = factory.CreateDbContext();
        (await verify.Users.FindAsync(user.Id))!.Preferred2FAMethod
            .Should().Be(TwoFactorMethods.Authenticator);
    }

    [Fact]
    public async Task DisableEmailOtpAsync_CorrectPassword_FallsBackToAuthenticator()
    {
        var (sut, factory, _, _, _) = CreateSut(nameof(DisableEmailOtpAsync_CorrectPassword_FallsBackToAuthenticator));
        var user = await SeedUserAsync(factory, "Correct!1", u =>
        {
            u.EmailOtpEnabled = true;
            u.TwoFactorEnabled = true;
            u.Preferred2FAMethod = TwoFactorMethods.EmailOtp;
        });

        (await sut.DisableEmailOtpAsync(user.Id, "Correct!1")).Should().BeTrue();

        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(user.Id))!;
        refreshed.EmailOtpEnabled.Should().BeFalse();
        refreshed.Preferred2FAMethod.Should().Be(TwoFactorMethods.Authenticator);
    }

    [Fact]
    public async Task SendEmailOtpAsync_NoService_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(SendEmailOtpAsync_NoService_ReturnsFalse));
        var sut = new TwoFactorManagementService(factory, NullLogger<TwoFactorManagementService>.Instance);

        (await sut.SendEmailOtpAsync(1)).Should().BeFalse();
    }

    [Fact]
    public async Task SendEmailOtpAsync_DelegatesToService()
    {
        var (sut, _, _, emailOtp, _) = CreateSut(nameof(SendEmailOtpAsync_DelegatesToService));
        emailOtp.SendOtpAsync(42, Arg.Any<string?>()).Returns(true);

        (await sut.SendEmailOtpAsync(42)).Should().BeTrue();
        await emailOtp.Received(1).SendOtpAsync(42, Arg.Any<string?>());
    }

    [Fact]
    public async Task SetPreferred2FAMethodAsync_UnknownMethod_ReturnsFalse()
    {
        var (sut, factory, _, _, _) = CreateSut(nameof(SetPreferred2FAMethodAsync_UnknownMethod_ReturnsFalse));
        var user = await SeedUserAsync(factory);

        (await sut.SetPreferred2FAMethodAsync(user.Id, "Nope")).Should().BeFalse();
    }

    [Fact]
    public async Task SetPreferred2FAMethodAsync_AuthenticatorNotEnabled_ReturnsFalse()
    {
        var (sut, factory, _, _, _) = CreateSut(nameof(SetPreferred2FAMethodAsync_AuthenticatorNotEnabled_ReturnsFalse));
        var user = await SeedUserAsync(factory);

        (await sut.SetPreferred2FAMethodAsync(user.Id, TwoFactorMethods.Authenticator))
            .Should().BeFalse();
    }

    [Fact]
    public async Task SetPreferred2FAMethodAsync_EmailOtpEnabled_Succeeds()
    {
        var (sut, factory, _, _, audit) = CreateSut(nameof(SetPreferred2FAMethodAsync_EmailOtpEnabled_Succeeds));
        var user = await SeedUserAsync(factory, mutate: u => u.EmailOtpEnabled = true);

        (await sut.SetPreferred2FAMethodAsync(user.Id, TwoFactorMethods.EmailOtp))
            .Should().BeTrue();

        await using var verify = factory.CreateDbContext();
        (await verify.Users.FindAsync(user.Id))!.Preferred2FAMethod
            .Should().Be(TwoFactorMethods.EmailOtp);
        await audit.Received(1).LogAsync(
            "2FA_PREFERRED_METHOD_CHANGED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Info);
    }

    [Fact]
    public async Task Get2FAMethodsAsync_ReturnsConfig()
    {
        var (sut, factory, _, _, _) = CreateSut(nameof(Get2FAMethodsAsync_ReturnsConfig));
        var user = await SeedUserAsync(factory, mutate: u =>
        {
            u.TwoFactorEnabled = true;
            u.EmailOtpEnabled = true;
            u.Preferred2FAMethod = TwoFactorMethods.EmailOtp;
        });

        var info = await sut.Get2FAMethodsAsync(user.Id);

        info.AuthenticatorEnabled.Should().BeTrue();
        info.EmailOtpEnabled.Should().BeTrue();
        info.PreferredMethod.Should().Be(TwoFactorMethods.EmailOtp);
        info.Any2FAEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Get2FAMethodsAsync_UnknownUser_ReturnsEmptyInfo()
    {
        var (sut, factory, _, _, _) = CreateSut(nameof(Get2FAMethodsAsync_UnknownUser_ReturnsEmptyInfo));
        await SeedWarehouseAsync(factory);

        var info = await sut.Get2FAMethodsAsync(999);

        info.AuthenticatorEnabled.Should().BeFalse();
        info.EmailOtpEnabled.Should().BeFalse();
    }
}
