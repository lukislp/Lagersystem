using System.Net;
using LagersystemLVHome.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Auth;

/// <summary>
/// Shared test scaffolding for <see cref="AuthService"/> specs, split across several
/// sibling *Tests.cs files by feature area (login, session restore, logout, 2FA
/// verification, magic-link login, passkey login). <see cref="AuthService"/> has eight
/// optional collaborators plus <see cref="IHttpContextAccessor"/>-driven branching
/// (client IP, cookies, circuit IDs), so the fixture wiring is centralised here instead
/// of being re-implemented per file the way the smaller test classes in this project do.
/// </summary>
internal static class AuthServiceTestSupport
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    public static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    /// <summary>
    /// Builds a real (server-less) <see cref="DefaultHttpContext"/> wrapped in a substitute
    /// accessor so that <c>AuthHelpers.GetClientIp</c>, cookie reads and header reads behave
    /// like a genuine request instead of needing per-property NSubstitute stubs.
    /// </summary>
    public static IHttpContextAccessor CreateHttpContextAccessor(
        string? remoteIp = "203.0.113.5",
        string? userAgent = "TestAgent/1.0",
        string? forwardedFor = null,
        string? deviceFingerprintCookie = null,
        string? sessionIdCookie = null,
        string? insightsSessionIdCookie = null,
        string? circuitId = null,
        string? sessionIdItem = null)
    {
        var httpContext = new DefaultHttpContext();

        if (remoteIp != null)
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);

        if (userAgent != null)
            httpContext.Request.Headers["User-Agent"] = userAgent;

        if (forwardedFor != null)
            httpContext.Request.Headers["X-Forwarded-For"] = forwardedFor;

        var cookiePairs = new List<string>();
        if (deviceFingerprintCookie != null)
            cookiePairs.Add($"DeviceFingerprint={deviceFingerprintCookie}");
        if (sessionIdCookie != null)
            cookiePairs.Add($"LagerSystem.SessionId={sessionIdCookie}");
        if (insightsSessionIdCookie != null)
            cookiePairs.Add($"InsightsSessionId={insightsSessionIdCookie}");
        if (cookiePairs.Count > 0)
            httpContext.Request.Headers["Cookie"] = string.Join("; ", cookiePairs);

        if (circuitId != null)
            httpContext.Items["CircuitId"] = circuitId;
        if (sessionIdItem != null)
            httpContext.Items["SessionId"] = sessionIdItem;

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    /// <summary>Accessor whose <see cref="IHttpContextAccessor.HttpContext"/> getter throws, to force the outer catch blocks.</summary>
    public static IHttpContextAccessor CreateThrowingHttpContextAccessor()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(_ => throw new InvalidOperationException("boom"));
        return accessor;
    }

    public sealed class Fixture
    {
        public required AuthService Sut { get; init; }
        public required IDbContextFactory<InventoryDbContext> Factory { get; init; }
        public required IAuditService Audit { get; init; }
        public required ISessionManagementService SessionMgmt { get; init; }
        public required ITwoFactorService TwoFactor { get; init; }
        public required IEmailOtpService EmailOtp { get; init; }
        public required IPasswordlessLoginService Passwordless { get; init; }
        public required IUserIpAccessService IpAccess { get; init; }
        public required IDeviceFingerprintService DeviceFp { get; init; }
        public required ISessionMonitorService SessionMonitor { get; init; }
        public required CircuitUserStore UserStore { get; init; }
        public required CustomAuthStateProvider StateProvider { get; init; }
        public required IHttpContextAccessor HttpContextAccessor { get; init; }
    }

    public static Fixture CreateFixture(
        string dbName,
        IHttpContextAccessor? httpContextAccessor = null,
        bool includeSessionMgmt = true,
        bool includeTwoFactor = true,
        bool includeEmailOtp = true,
        bool includePasswordless = true,
        bool includeIpAccess = true,
        bool includeDeviceFp = true,
        bool includeSessionMonitor = true,
        bool includeAudit = true)
    {
        var factory = CreateFactory(dbName);
        var accessor = httpContextAccessor ?? CreateHttpContextAccessor();
        var userStore = new CircuitUserStore(accessor, NullLogger<CircuitUserStore>.Instance);
        var stateProvider = new CustomAuthStateProvider(userStore, accessor, NullLogger<CustomAuthStateProvider>.Instance);

        var audit = Substitute.For<IAuditService>();
        var sessionMgmt = Substitute.For<ISessionManagementService>();
        var twoFactor = Substitute.For<ITwoFactorService>();
        var emailOtp = Substitute.For<IEmailOtpService>();
        var passwordless = Substitute.For<IPasswordlessLoginService>();
        var ipAccess = Substitute.For<IUserIpAccessService>();
        var deviceFp = Substitute.For<IDeviceFingerprintService>();
        var sessionMonitor = Substitute.For<ISessionMonitorService>();

        // Sensible defaults so login/session-bootstrap code paths don't NRE on an
        // unconfigured substitute (e.g. AuthService dereferences the IpAccessCheckResult
        // it gets back without a null check); individual tests can override with .Returns(...).
        ipAccess.CheckAccessAsync(Arg.Any<int>(), Arg.Any<string>())
            .Returns(Task.FromResult(LagersystemLVHome.Application.Services.IpAccessCheckResult.Allowed()));

        sessionMgmt.CreateSessionAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo => Task.FromResult(new LagersystemLVHome.Domain.Models.UserSession
            {
                Id = 1,
                SessionId = $"sess-{Guid.NewGuid():N}",
                UserId = callInfo.ArgAt<int>(0),
                WarehouseId = callInfo.ArgAt<int>(1),
                IpAddress = callInfo.ArgAt<string>(2),
                UserAgent = callInfo.ArgAt<string>(3),
                DeviceType = "Desktop"
            }));

        var sut = new AuthService(
            factory,
            stateProvider,
            userStore,
            accessor,
            NullLogger<AuthService>.Instance,
            sessionManagementService: includeSessionMgmt ? sessionMgmt : null,
            twoFactorService: includeTwoFactor ? twoFactor : null,
            auditService: includeAudit ? audit : null,
            emailOtpService: includeEmailOtp ? emailOtp : null,
            passwordlessLoginService: includePasswordless ? passwordless : null,
            ipAccessService: includeIpAccess ? ipAccess : null,
            deviceFingerprintService: includeDeviceFp ? deviceFp : null,
            sessionMonitorService: includeSessionMonitor ? sessionMonitor : null);

        return new Fixture
        {
            Sut = sut,
            Factory = factory,
            Audit = audit,
            SessionMgmt = sessionMgmt,
            TwoFactor = twoFactor,
            EmailOtp = emailOtp,
            Passwordless = passwordless,
            IpAccess = ipAccess,
            DeviceFp = deviceFp,
            SessionMonitor = sessionMonitor,
            UserStore = userStore,
            StateProvider = stateProvider,
            HttpContextAccessor = accessor
        };
    }

    public static User CreateValidUser(string password = "Correct!1", int warehouseId = 1, string username = "alice") => new()
    {
        Username = username,
        Email = $"{username}@test.local",
        DisplayName = username,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
        IsActive = true,
        IsDeleted = false,
        ApprovalStatus = UserApprovalStatus.Approved,
        GdprConsentGiven = true,
        AnalyticsConsent = true,
        DeviceFingerprintConsent = true,
        WarehouseId = warehouseId,
        Role = UserRole.User
    };

    public static async Task SeedWarehouseAsync(IDbContextFactory<InventoryDbContext> factory, int warehouseId = 1)
    {
        await using var db = factory.CreateDbContext();
        if (!await db.Warehouses.AnyAsync(w => w.Id == warehouseId))
        {
            db.Warehouses.Add(new Warehouse { Id = warehouseId, Name = $"WH{warehouseId}", Code = $"TEST{warehouseId}", IsActive = true });
            await db.SaveChangesAsync();
        }
    }

    public static async Task<User> SeedUserAsync(
        IDbContextFactory<InventoryDbContext> factory, Action<User>? mutate = null, string password = "Correct!1",
        int warehouseId = 1, string username = "alice")
    {
        await using var db = factory.CreateDbContext();
        if (!await db.Warehouses.AnyAsync(w => w.Id == warehouseId))
        {
            db.Warehouses.Add(new Warehouse { Id = warehouseId, Name = $"WH{warehouseId}", Code = $"TEST{warehouseId}", IsActive = true });
        }
        var user = CreateValidUser(password, warehouseId, username);
        mutate?.Invoke(user);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
