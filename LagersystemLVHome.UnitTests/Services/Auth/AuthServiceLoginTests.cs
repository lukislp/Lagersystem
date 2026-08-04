using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Auth;

/// <summary>
/// Covers <see cref="AuthService.LoginAsync"/> – the most security-critical
/// method. Each test uses an isolated in-memory database. Optional collaborators
/// (session management, 2FA, IP access, …) are omitted so only the core login
/// decision logic is exercised. The <see cref="LoginFailures"/> error codes are
/// verified directly against <see cref="Result{T}.ErrorCode"/>.
/// </summary>
public class AuthServiceLoginTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static (AuthService sut, IDbContextFactory<InventoryDbContext> factory, IAuditService audit)
        CreateSut(string dbName)
    {
        var factory = CreateFactory(dbName);
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var userStore = new CircuitUserStore(httpContextAccessor, NullLogger<CircuitUserStore>.Instance);
        var stateProvider = new CustomAuthStateProvider(
            userStore, httpContextAccessor, NullLogger<CustomAuthStateProvider>.Instance);

        var audit = Substitute.For<IAuditService>();

        var sut = new AuthService(
            factory,
            stateProvider,
            userStore,
            httpContextAccessor,
            NullLogger<AuthService>.Instance,
            sessionManagementService: null,
            twoFactorService: null,
            auditService: audit);

        return (sut, factory, audit);
    }

    private static User CreateValidUser(string password = "Correct!1") => new()
    {
        Username = "alice",
        Email = "alice@test.local",
        DisplayName = "Alice",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
        IsActive = true,
        IsDeleted = false,
        ApprovalStatus = UserApprovalStatus.Approved,
        GdprConsentGiven = true,
        AnalyticsConsent = true,
        DeviceFingerprintConsent = true,
        WarehouseId = 1,
        Role = UserRole.User
    };

    private static async Task<User> SeedAsync(
        IDbContextFactory<InventoryDbContext> factory, Action<User>? mutate = null, string password = "Correct!1")
    {
        await using var db = factory.CreateDbContext();
        // The User entity has a required Warehouse FK (non-nullable WarehouseId).
        // The EF Core InMemory provider treats Include(u => u.Warehouse) on a
        // required relationship as an INNER JOIN, so the user is filtered out
        // when no matching warehouse exists. Seed a Warehouse with Id=1 first.
        if (!await db.Warehouses.AnyAsync(w => w.Id == 1))
        {
            db.Warehouses.Add(new Warehouse
            {
                Id = 1,
                Name = "Test Warehouse",
                Code = "TEST",
                IsActive = true
            });
        }
        var user = CreateValidUser(password);
        mutate?.Invoke(user);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task LoginAsync_WithUnknownUser_ReturnsUserNotFoundAndAuditsFailure()
    {
        var (sut, _, audit) = CreateSut(nameof(LoginAsync_WithUnknownUser_ReturnsUserNotFoundAndAuditsFailure));

        var result = await sut.LoginAsync("ghost", "x");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.UserNotFound);
        await audit.Received(1).LogAsync(
            "LOGIN_FAILED", "User", null, Arg.Any<object?>(), AuditSeverity.Warning);
    }

    [Fact]
    public async Task LoginAsync_WithLockedAccount_ReturnsAccountLockedAndDoesNotIncrementAttempts()
    {
        var (sut, factory, audit) = CreateSut(nameof(LoginAsync_WithLockedAccount_ReturnsAccountLockedAndDoesNotIncrementAttempts));
        var user = await SeedAsync(factory, u =>
        {
            u.LockedUntil = DateTime.UtcNow.AddMinutes(10);
            u.FailedLoginAttempts = 5;
        });

        var result = await sut.LoginAsync("alice", "Correct!1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.AccountLocked);
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace("remaining lockout minutes are returned as ErrorMessage");
        await audit.Received(1).LogAsync(
            "LOGIN_BLOCKED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Warning);

        await using var verify = factory.CreateDbContext();
        (await verify.Users.FindAsync(user.Id))!.FailedLoginAttempts.Should().Be(5);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_IncrementsAttemptsAndReturnsInvalidPassword()
    {
        var (sut, factory, _) = CreateSut(nameof(LoginAsync_WithWrongPassword_IncrementsAttemptsAndReturnsInvalidPassword));
        var user = await SeedAsync(factory);

        var result = await sut.LoginAsync("alice", "WRONG");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.InvalidPassword);

        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(user.Id))!;
        refreshed.FailedLoginAttempts.Should().Be(1);
        refreshed.LastFailedLoginAt.Should().HaveValue();
        refreshed.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WithFifthWrongPassword_LocksAccount()
    {
        var (sut, factory, audit) = CreateSut(nameof(LoginAsync_WithFifthWrongPassword_LocksAccount));
        var user = await SeedAsync(factory, u => u.FailedLoginAttempts = 4);

        var result = await sut.LoginAsync("alice", "WRONG");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.InvalidPassword);

        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(user.Id))!;
        refreshed.FailedLoginAttempts.Should().Be(5);
        refreshed.LockedUntil.Should().HaveValue();
        refreshed.LockedUntil!.Value.Should().BeAfter(DateTime.UtcNow);

        await audit.Received(1).LogAsync(
            "ACCOUNT_LOCKED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Critical);
    }

    [Fact]
    public async Task LoginAsync_WithMissingGdprConsent_ReturnsGdprConsentRequired()
    {
        var (sut, factory, audit) = CreateSut(nameof(LoginAsync_WithMissingGdprConsent_ReturnsGdprConsentRequired));
        var user = await SeedAsync(factory, u => u.GdprConsentGiven = false);

        var result = await sut.LoginAsync("alice", "Correct!1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.GdprConsentRequired);
        await audit.Received().LogAsync(
            "LOGIN_DENIED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Warning);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task LoginAsync_WithMissingGranularConsents_ReturnsGranularConsentRequired(bool analytics, bool fingerprint)
    {
        var (sut, factory, _) = CreateSut(
            $"{nameof(LoginAsync_WithMissingGranularConsents_ReturnsGranularConsentRequired)}_{analytics}_{fingerprint}");
        await SeedAsync(factory, u =>
        {
            u.AnalyticsConsent = analytics;
            u.DeviceFingerprintConsent = fingerprint;
        });

        var result = await sut.LoginAsync("alice", "Correct!1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.GranularConsentRequired);
    }

    [Fact]
    public async Task LoginAsync_WithInactiveUser_ReturnsInactive()
    {
        var (sut, factory, _) = CreateSut(nameof(LoginAsync_WithInactiveUser_ReturnsInactive));
        await SeedAsync(factory, u => u.IsActive = false);

        var result = await sut.LoginAsync("alice", "Correct!1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.Inactive);
    }

    [Fact]
    public async Task LoginAsync_WithPendingUser_ReturnsPendingApproval()
    {
        var (sut, factory, audit) = CreateSut(nameof(LoginAsync_WithPendingUser_ReturnsPendingApproval));
        var user = await SeedAsync(factory, u => u.ApprovalStatus = UserApprovalStatus.Pending);

        var result = await sut.LoginAsync("alice", "Correct!1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.PendingApproval);
        await audit.Received().LogAsync(
            "LOGIN_DENIED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Warning);
    }

    [Fact]
    public async Task LoginAsync_WithRejectedUser_ReturnsRejected()
    {
        var (sut, factory, _) = CreateSut(nameof(LoginAsync_WithRejectedUser_ReturnsRejected));
        await SeedAsync(factory, u => u.ApprovalStatus = UserApprovalStatus.Rejected);

        var result = await sut.LoginAsync("alice", "Correct!1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.Rejected);
    }

    [Fact]
    public async Task LoginAsync_WithCorrectPassword_ResetsFailureCountersAndAuditsSuccess()
    {
        var (sut, factory, audit) = CreateSut(nameof(LoginAsync_WithCorrectPassword_ResetsFailureCountersAndAuditsSuccess));
        var user = await SeedAsync(factory, u =>
        {
            u.FailedLoginAttempts = 3;
            u.LastFailedLoginAt = DateTime.UtcNow.AddMinutes(-1);
        });

        var result = await sut.LoginAsync("alice", "Correct!1");

        result.IsSuccess.Should().BeTrue(
            $"login should succeed but failed with code='{result.ErrorCode}', message='{result.ErrorMessage}'");
        result.Value!.Id.Should().Be(user.Id);

        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(user.Id))!;
        refreshed.FailedLoginAttempts.Should().Be(0);
        refreshed.LastFailedLoginAt.Should().BeNull();
        refreshed.LockedUntil.Should().BeNull();
        refreshed.LastLoginAt.Should().BeOnOrBefore(DateTime.UtcNow);

        await audit.Received(1).LogAsync(
            "LOGIN_SUCCESS", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Info);
    }

    [Fact]
    public void GetCurrentWarehouseId_WithoutSession_ReturnsDefault()
    {
        var (sut, _, _) = CreateSut(nameof(GetCurrentWarehouseId_WithoutSession_ReturnsDefault));

        sut.GetCurrentWarehouseId().Should().Be(1);
    }

    [Fact]
    public void IsAuthenticated_WithoutSession_ReturnsFalse()
    {
        var (sut, _, _) = CreateSut(nameof(IsAuthenticated_WithoutSession_ReturnsFalse));

        sut.IsAuthenticated().Should().BeFalse();
    }
}
