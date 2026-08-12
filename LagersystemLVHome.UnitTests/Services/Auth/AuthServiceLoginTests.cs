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

    // ---- IP access + session bootstrap (uses AuthServiceTestSupport for a real HttpContext) ----

    [Fact]
    public async Task LoginAsync_WithIpAccessDenied_ReturnsIpDeniedWithoutCheckingPassword()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginAsync_WithIpAccessDenied_ReturnsIpDeniedWithoutCheckingPassword));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);
        fixture.IpAccess.CheckAccessAsync(user.Id, Arg.Any<string>())
            .Returns(Task.FromResult(LagersystemLVHome.Application.Services.IpAccessCheckResult.Denied("blocked", "rule-1")));

        var result = await fixture.Sut.LoginAsync(user.Username, "WRONG");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(LoginFailures.IpDenied);
        result.ErrorMessage.Should().Be("rule-1");

        await using var verify = fixture.Factory.CreateDbContext();
        (await verify.Users.FindAsync(user.Id))!.FailedLoginAttempts.Should().Be(0, "password should never be checked once IP access is denied");

        await fixture.Audit.Received(1).LogAsync("LOGIN_IP_DENIED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Warning);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoginAsync_WithIpAccessAllowed_Succeeds(bool restrictionsEnabled)
    {
        var fixture = AuthServiceTestSupport.CreateFixture($"{nameof(LoginAsync_WithIpAccessAllowed_Succeeds)}_{restrictionsEnabled}");
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);
        fixture.IpAccess.CheckAccessAsync(user.Id, Arg.Any<string>())
            .Returns(Task.FromResult(new LagersystemLVHome.Application.Services.IpAccessCheckResult
            {
                IsAllowed = true,
                RestrictionsEnabled = restrictionsEnabled
            }));

        var result = await fixture.Sut.LoginAsync(user.Username, "Correct!1");

        result.IsSuccess.Should().BeTrue($"error was '{result.ErrorCode}'");
    }

    [Fact]
    public async Task LoginAsync_WithSessionManagement_CreatesSessionAndStoresSessionIdInCircuit()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(circuitId: "circuit-1");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(LoginAsync_WithSessionManagement_CreatesSessionAndStoresSessionIdInCircuit), accessor);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);

        var result = await fixture.Sut.LoginAsync(user.Username, "Correct!1");

        result.IsSuccess.Should().BeTrue();
        await fixture.SessionMgmt.Received(1).CreateSessionAsync(user.Id, user.WarehouseId, Arg.Any<string>(), Arg.Any<string>());
        fixture.UserStore.GetSessionId().Should().NotBeNullOrEmpty();
        await fixture.SessionMonitor.Received(1).StartMonitoringAsync(user.Id, Arg.Any<string>(), "circuit-1");
    }

    [Fact]
    public async Task LoginAsync_WithSessionManagementButNoCircuitId_StartsMonitoringWithoutCircuit()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor();
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(LoginAsync_WithSessionManagementButNoCircuitId_StartsMonitoringWithoutCircuit), accessor);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);

        var result = await fixture.Sut.LoginAsync(user.Username, "Correct!1");

        result.IsSuccess.Should().BeTrue();
        await fixture.SessionMonitor.Received(1).StartMonitoringAsync(user.Id, Arg.Any<string>());
    }

    [Fact]
    public async Task LoginAsync_WithDeviceFingerprintCookie_SavesFingerprint()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(deviceFingerprintCookie: "fp-abc123");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(LoginAsync_WithDeviceFingerprintCookie_SavesFingerprint), accessor);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);

        var result = await fixture.Sut.LoginAsync(user.Username, "Correct!1");

        result.IsSuccess.Should().BeTrue();
        await fixture.DeviceFp.Received(1).SaveDeviceFingerprintAsync(Arg.Any<int>(), "fp-abc123", Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task LoginAsync_WithoutDeviceFingerprintCookie_DoesNotSaveFingerprint()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginAsync_WithoutDeviceFingerprintCookie_DoesNotSaveFingerprint));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);

        var result = await fixture.Sut.LoginAsync(user.Username, "Correct!1");

        result.IsSuccess.Should().BeTrue();
        await fixture.DeviceFp.DidNotReceiveWithAnyArgs().SaveDeviceFingerprintAsync(default, default!, default!);
    }

    [Fact]
    public async Task LoginAsync_WithoutSessionManagementService_StillSucceedsWithoutCreatingSession()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(LoginAsync_WithoutSessionManagementService_StillSucceedsWithoutCreatingSession), includeSessionMgmt: false);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);

        var result = await fixture.Sut.LoginAsync(user.Username, "Correct!1");

        result.IsSuccess.Should().BeTrue();
        fixture.UserStore.GetSessionId().Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task LoginAsync_WhenSessionCreationThrows_StillMarksUserAuthenticated()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(circuitId: "circuit-throw");
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginAsync_WhenSessionCreationThrows_StillMarksUserAuthenticated), accessor);
        fixture.SessionMgmt.CreateSessionAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task<LagersystemLVHome.Domain.Models.UserSession>>(_ => throw new InvalidOperationException("db unavailable"));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);

        var result = await fixture.Sut.LoginAsync(user.Username, "Correct!1");

        result.IsSuccess.Should().BeTrue("session bootstrap failures must not block a successful login");
        fixture.UserStore.GetUser().Should().NotBeNull();
    }

    [Fact]
    public async Task LoginAsync_WhenDeviceFingerprintSaveThrows_StillSucceeds()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(deviceFingerprintCookie: "fp-1");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(LoginAsync_WhenDeviceFingerprintSaveThrows_StillSucceeds), accessor);
        fixture.DeviceFp.SaveDeviceFingerprintAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<HttpContext>())
            .Returns<Task>(_ => throw new InvalidOperationException("fingerprint failure"));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);

        var result = await fixture.Sut.LoginAsync(user.Username, "Correct!1");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task LoginAsync_WhenSessionMonitorStartThrows_StillSucceeds()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(LoginAsync_WhenSessionMonitorStartThrows_StillSucceeds));
        fixture.SessionMonitor.StartMonitoringAsync(Arg.Any<int>(), Arg.Any<string>())
            .Returns<Task>(_ => throw new InvalidOperationException("monitor failure"));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);

        var result = await fixture.Sut.LoginAsync(user.Username, "Correct!1");

        result.IsSuccess.Should().BeTrue();
    }
}
