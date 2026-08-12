using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Auth;

/// <summary>
/// Covers <see cref="PasswordResetService"/>. The service issues single-use
/// reset tokens (24h lifetime), invalidates previous tokens on new requests,
/// verifies the old password on change, and clears the failed-login counter
/// / lockout on successful reset.
/// </summary>
public class PasswordResetServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static (PasswordResetService sut, IDbContextFactory<InventoryDbContext> factory, IAuditService audit)
        CreateSut(string dbName)
    {
        var factory = new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(dbName).Options);
        var audit = Substitute.For<IAuditService>();
        var sut = new PasswordResetService(
            factory, NullLogger<PasswordResetService>.Instance, httpContextAccessor: null, auditService: audit);
        return (sut, factory, audit);
    }

    private static async Task<User> SeedUserAsync(
        IDbContextFactory<InventoryDbContext> factory,
        string password = "Current1!",
        Action<User>? mutate = null)
    {
        await using var db = factory.CreateDbContext();
        if (!await db.Warehouses.AnyAsync(w => w.Id == 1))
        {
            db.Warehouses.Add(new Warehouse { Id = 1, Name = "Test", Code = "T", IsActive = true });
        }
        var user = new User
        {
            Username = "alice",
            Email = "alice@test.local",
            DisplayName = "Alice",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsActive = true,
            ApprovalStatus = UserApprovalStatus.Approved,
            GdprConsentGiven = true,
            WarehouseId = 1
        };
        mutate?.Invoke(user);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // --- ChangePasswordAsync ---

    [Fact]
    public async Task ChangePasswordAsync_WithUnknownUser_ReturnsFalse()
    {
        var (sut, _, _) = CreateSut(nameof(ChangePasswordAsync_WithUnknownUser_ReturnsFalse));

        (await sut.ChangePasswordAsync(999, "x", "New1!abc")).Should().BeFalse();
    }

    [Fact]
    public async Task ChangePasswordAsync_WithWrongOldPassword_ReturnsFalseAndLogsAudit()
    {
        var (sut, factory, audit) = CreateSut(nameof(ChangePasswordAsync_WithWrongOldPassword_ReturnsFalseAndLogsAudit));
        var user = await SeedUserAsync(factory);

        var ok = await sut.ChangePasswordAsync(user.Id, "wrong-old", "New1!abc");

        ok.Should().BeFalse();
        await audit.Received(1).LogAsync(
            "PASSWORD_CHANGE_FAILED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Warning);

        await using var verify = factory.CreateDbContext();
        BCrypt.Net.BCrypt.Verify("Current1!", (await verify.Users.FindAsync(user.Id))!.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task ChangePasswordAsync_WithCorrectOldPassword_RehashesAndAudits()
    {
        var (sut, factory, audit) = CreateSut(nameof(ChangePasswordAsync_WithCorrectOldPassword_RehashesAndAudits));
        var user = await SeedUserAsync(factory);

        var ok = await sut.ChangePasswordAsync(user.Id, "Current1!", "NewStrong1!");

        ok.Should().BeTrue();
        await audit.Received(1).LogAsync(
            "PASSWORD_CHANGED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Info);

        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(user.Id))!;
        BCrypt.Net.BCrypt.Verify("NewStrong1!", refreshed.PasswordHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify("Current1!", refreshed.PasswordHash).Should().BeFalse();
        refreshed.LastPasswordChangeAt.Should().NotBeNull();
    }

    // --- RequestPasswordResetAsync ---

    [Fact]
    public async Task RequestPasswordResetAsync_WithUnknownEmail_ReturnsNullAndLogsAudit()
    {
        var (sut, _, audit) = CreateSut(nameof(RequestPasswordResetAsync_WithUnknownEmail_ReturnsNullAndLogsAudit));

        var token = await sut.RequestPasswordResetAsync("ghost@nowhere.local");

        token.Should().BeNull();
        await audit.Received(1).LogAsync(
            "PASSWORD_RESET_REQUESTED_INVALID", "User", null, Arg.Any<object?>(), AuditSeverity.Warning);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_WithInactiveUser_ReturnsNull()
    {
        var (sut, factory, _) = CreateSut(nameof(RequestPasswordResetAsync_WithInactiveUser_ReturnsNull));
        await SeedUserAsync(factory, mutate: u => u.IsActive = false);

        (await sut.RequestPasswordResetAsync("alice@test.local")).Should().BeNull();
    }

    [Fact]
    public async Task RequestPasswordResetAsync_WithActiveUser_CreatesTokenAndInvalidatesPrevious()
    {
        var (sut, factory, _) = CreateSut(nameof(RequestPasswordResetAsync_WithActiveUser_CreatesTokenAndInvalidatesPrevious));
        var user = await SeedUserAsync(factory);

        // Seed a previous unused token that must be cleaned up.
        await using (var db = factory.CreateDbContext())
        {
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                Token = "OLD",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        var token = await sut.RequestPasswordResetAsync("alice@test.local");

        token.Should().NotBeNullOrWhiteSpace();

        await using var verify = factory.CreateDbContext();
        var tokens = await verify.PasswordResetTokens.Where(t => t.UserId == user.Id).ToListAsync();
        tokens.Should().HaveCount(1, "previous unused tokens must be removed");
        tokens[0].Token.Should().Be(token);
        tokens[0].IsUsed.Should().BeFalse();
        tokens[0].ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddHours(23));
    }

    // --- ValidateResetTokenAsync ---

    [Fact]
    public async Task ValidateResetTokenAsync_WithValidToken_ReturnsTrue()
    {
        var (sut, factory, _) = CreateSut(nameof(ValidateResetTokenAsync_WithValidToken_ReturnsTrue));
        var user = await SeedUserAsync(factory);

        await using (var db = factory.CreateDbContext())
        {
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                Token = "VALID",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        (await sut.ValidateResetTokenAsync("VALID")).Should().BeTrue();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("used")]
    [InlineData("expired")]
    public async Task ValidateResetTokenAsync_WithInvalidToken_ReturnsFalse(string scenario)
    {
        var (sut, factory, _) = CreateSut($"{nameof(ValidateResetTokenAsync_WithInvalidToken_ReturnsFalse)}_{scenario}");
        var user = await SeedUserAsync(factory);

        if (scenario != "unknown")
        {
            await using var db = factory.CreateDbContext();
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                Token = "TOK",
                IsUsed = scenario == "used",
                ExpiresAt = scenario == "expired" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        var lookup = scenario == "unknown" ? "does-not-exist" : "TOK";
        (await sut.ValidateResetTokenAsync(lookup)).Should().BeFalse();
    }

    // --- ResetPasswordAsync ---

    [Fact]
    public async Task ResetPasswordAsync_WithValidToken_RehashesAndClearsLockout()
    {
        var (sut, factory, audit) = CreateSut(nameof(ResetPasswordAsync_WithValidToken_RehashesAndClearsLockout));
        var user = await SeedUserAsync(factory, mutate: u =>
        {
            u.FailedLoginAttempts = 5;
            u.LockedUntil = DateTime.UtcNow.AddMinutes(10);
        });

        await using (var db = factory.CreateDbContext())
        {
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                Token = "RES",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        var ok = await sut.ResetPasswordAsync("RES", "BrandNew1!");

        ok.Should().BeTrue();
        await audit.Received(1).LogAsync(
            "PASSWORD_RESET_SUCCESS", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Info);

        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(user.Id))!;
        BCrypt.Net.BCrypt.Verify("BrandNew1!", refreshed.PasswordHash).Should().BeTrue();
        refreshed.FailedLoginAttempts.Should().Be(0);
        refreshed.LockedUntil.Should().BeNull();

        var tokenRow = await verify.PasswordResetTokens.SingleAsync();
        tokenRow.IsUsed.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPasswordAsync_WithExpiredToken_Fails()
    {
        var (sut, factory, _) = CreateSut(nameof(ResetPasswordAsync_WithExpiredToken_Fails));
        var user = await SeedUserAsync(factory);

        await using (var db = factory.CreateDbContext())
        {
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                Token = "EXP",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
            });
            await db.SaveChangesAsync();
        }

        (await sut.ResetPasswordAsync("EXP", "New1!abc")).Should().BeFalse();

        await using var verify = factory.CreateDbContext();
        BCrypt.Net.BCrypt.Verify("Current1!", (await verify.Users.FindAsync(user.Id))!.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task ResetPasswordAsync_WithAlreadyUsedToken_Fails()
    {
        var (sut, factory, _) = CreateSut(nameof(ResetPasswordAsync_WithAlreadyUsedToken_Fails));
        var user = await SeedUserAsync(factory);

        await using (var db = factory.CreateDbContext())
        {
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                Token = "USED",
                IsUsed = true,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        (await sut.ResetPasswordAsync("USED", "New1!abc")).Should().BeFalse();
    }
}
