using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.UnitTests.Services.Auth;

/// <summary>
/// Covers <see cref="AuthService.GetCurrentUserAsync"/>, the private cookie-based session
/// restoration it delegates to (<c>TryRestoreSessionFromCookieAsync</c>) and
/// <see cref="AuthService.GetCurrentSessionIdAsync"/>. Uses <see cref="AuthServiceTestSupport"/>
/// for a real (server-less) <c>HttpContext</c> so cookies/headers behave like a genuine request.
/// </summary>
public class AuthServiceSessionRestoreTests
{
    [Fact]
    public async Task GetCurrentUserAsync_WithUserAlreadyInCircuitStore_ReturnsUserWithoutTouchingCookies()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(circuitId: "circuit-a");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(GetCurrentUserAsync_WithUserAlreadyInCircuitStore_ReturnsUserWithoutTouchingCookies), accessor);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);
        (await fixture.Sut.LoginAsync(user.Username, "Correct!1")).IsSuccess.Should().BeTrue();

        var result = await fixture.Sut.GetCurrentUserAsync();

        result.Should().NotBeNull();
        result!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithNoSessionCookie_ReturnsNull()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(GetCurrentUserAsync_WithNoSessionCookie_ReturnsNull));

        (await fixture.Sut.GetCurrentUserAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithSessionIdAlreadyTrackedButNoUser_ReturnsNullWithoutQueryingDb()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(circuitId: "circuit-b");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(GetCurrentUserAsync_WithSessionIdAlreadyTrackedButNoUser_ReturnsNullWithoutQueryingDb), accessor);
        // Directly seed the circuit's session-id slot without a user, bypassing login,
        // to reach the "sessionId already exists in store" short-circuit branch.
        fixture.UserStore.SetSessionId("orphan-session-id");

        (await fixture.Sut.GetCurrentUserAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithApiPrefixedSessionCookie_DeniesRestoration()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(sessionIdCookie: "api-xyz123");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(GetCurrentUserAsync_WithApiPrefixedSessionCookie_DeniesRestoration), accessor);

        (await fixture.Sut.GetCurrentUserAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithoutSessionManagementService_ReturnsNull()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(sessionIdCookie: "session-1");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(GetCurrentUserAsync_WithoutSessionManagementService_ReturnsNull), accessor, includeSessionMgmt: false);

        (await fixture.Sut.GetCurrentUserAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithCookieNotMatchingAnyDbSession_ReturnsNull()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(sessionIdCookie: "unknown-session");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(GetCurrentUserAsync_WithCookieNotMatchingAnyDbSession_ReturnsNull), accessor);

        (await fixture.Sut.GetCurrentUserAsync()).Should().BeNull();
    }

    private static async Task<User> SeedUserWithDbSessionAsync(
        AuthServiceTestSupport.Fixture fixture,
        string sessionId,
        string deviceType = "Desktop",
        DateTime? lastActivity = null,
        bool userActive = true,
        bool userApproved = true)
    {
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u =>
        {
            u.IsActive = userActive;
            u.ApprovalStatus = userApproved ? UserApprovalStatus.Approved : UserApprovalStatus.Pending;
        });
        await using var db = fixture.Factory.CreateDbContext();
        db.UserSessions.Add(new LagersystemLVHome.Domain.Models.UserSession
        {
            SessionId = sessionId,
            UserId = user.Id,
            Username = user.Username,
            WarehouseId = user.WarehouseId,
            DeviceType = deviceType,
            IsActive = true,
            LastActivity = lastActivity ?? DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithApiDeviceTypeDbSession_DeniesRestoration()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(sessionIdCookie: "sess-api");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(GetCurrentUserAsync_WithApiDeviceTypeDbSession_DeniesRestoration), accessor);
        await SeedUserWithDbSessionAsync(fixture, "sess-api", deviceType: "API");

        (await fixture.Sut.GetCurrentUserAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithDeviceTypeMismatch_ReturnsNull()
    {
        // Session was created on a mobile device, but the current request looks like a
        // desktop UA (default TestAgent/1.0) -> mismatch -> restoration denied.
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(sessionIdCookie: "sess-mismatch", userAgent: "Mozilla/5.0 Desktop");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(GetCurrentUserAsync_WithDeviceTypeMismatch_ReturnsNull), accessor);
        await SeedUserWithDbSessionAsync(fixture, "sess-mismatch", deviceType: "Mobile");

        (await fixture.Sut.GetCurrentUserAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithExpiredInactiveSession_ReturnsNull()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(sessionIdCookie: "sess-expired");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(GetCurrentUserAsync_WithExpiredInactiveSession_ReturnsNull), accessor);
        await SeedUserWithDbSessionAsync(fixture, "sess-expired", lastActivity: DateTime.UtcNow.AddDays(-8));

        (await fixture.Sut.GetCurrentUserAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithInactiveUserOnValidSession_ReturnsNull()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(sessionIdCookie: "sess-inactive-user");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(GetCurrentUserAsync_WithInactiveUserOnValidSession_ReturnsNull), accessor);
        await SeedUserWithDbSessionAsync(fixture, "sess-inactive-user", userActive: false);

        (await fixture.Sut.GetCurrentUserAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithUnapprovedUserOnValidSession_ReturnsNull()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(sessionIdCookie: "sess-pending-user");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(GetCurrentUserAsync_WithUnapprovedUserOnValidSession_ReturnsNull), accessor);
        await SeedUserWithDbSessionAsync(fixture, "sess-pending-user", userApproved: false);

        (await fixture.Sut.GetCurrentUserAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithValidCookieAndExistingCircuitId_RestoresSessionAndUpdatesActivity()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(sessionIdCookie: "sess-valid", circuitId: "circuit-99");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(GetCurrentUserAsync_WithValidCookieAndExistingCircuitId_RestoresSessionAndUpdatesActivity), accessor);
        var user = await SeedUserWithDbSessionAsync(fixture, "sess-valid", lastActivity: DateTime.UtcNow.AddHours(-1));

        var result = await fixture.Sut.GetCurrentUserAsync();

        result.Should().NotBeNull();
        result!.Id.Should().Be(user.Id);
        fixture.UserStore.GetSessionId().Should().Be("sess-valid");

        await using var verify = fixture.Factory.CreateDbContext();
        var dbSession = await verify.UserSessions.SingleAsync(s => s.SessionId == "sess-valid");
        dbSession.LastActivity.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithValidCookieAndNoExistingCircuitId_GeneratesCircuitIdAndRestores()
    {
        // No pre-set "CircuitId" HttpContext.Items entry -> exercises the
        // "generate a new circuit id for restoration" branch instead of reusing one.
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(sessionIdCookie: "sess-valid2");
        var fixture = AuthServiceTestSupport.CreateFixture(
            nameof(GetCurrentUserAsync_WithValidCookieAndNoExistingCircuitId_GeneratesCircuitIdAndRestores), accessor);
        var user = await SeedUserWithDbSessionAsync(fixture, "sess-valid2");

        var result = await fixture.Sut.GetCurrentUserAsync();

        result.Should().NotBeNull();
        result!.Id.Should().Be(user.Id);
    }

    // ---- GetCurrentSessionIdAsync ------------------------------------------------------

    [Fact]
    public async Task GetCurrentSessionIdAsync_PrefersCircuitUserStoreSessionId()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(circuitId: "circuit-c");
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(GetCurrentSessionIdAsync_PrefersCircuitUserStoreSessionId), accessor);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);
        await fixture.Sut.LoginAsync(user.Username, "Correct!1");

        fixture.UserStore.GetSessionId().Should().NotBeNullOrEmpty("the fixture's circuit id must resolve for this assertion to be meaningful");

        var sessionId = await fixture.Sut.GetCurrentSessionIdAsync();

        sessionId.Should().Be(fixture.UserStore.GetSessionId());
    }

    [Fact]
    public async Task GetCurrentSessionIdAsync_FallsBackToHttpContextItems()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(sessionIdItem: "items-session-id");
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(GetCurrentSessionIdAsync_FallsBackToHttpContextItems), accessor);

        (await fixture.Sut.GetCurrentSessionIdAsync()).Should().Be("items-session-id");
    }

    [Fact]
    public async Task GetCurrentSessionIdAsync_FallsBackToInsightsCookie()
    {
        var accessor = AuthServiceTestSupport.CreateHttpContextAccessor(insightsSessionIdCookie: "insights-id");
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(GetCurrentSessionIdAsync_FallsBackToInsightsCookie), accessor);

        (await fixture.Sut.GetCurrentSessionIdAsync()).Should().Be("insights-id");
    }

    [Fact]
    public async Task GetCurrentSessionIdAsync_WithNothingAvailable_ReturnsNull()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(GetCurrentSessionIdAsync_WithNothingAvailable_ReturnsNull));

        (await fixture.Sut.GetCurrentSessionIdAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentSessionIdAsync_WhenHttpContextAccessorThrows_ReturnsNull()
    {
        var accessor = AuthServiceTestSupport.CreateThrowingHttpContextAccessor();
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(GetCurrentSessionIdAsync_WhenHttpContextAccessorThrows_ReturnsNull), accessor);

        (await fixture.Sut.GetCurrentSessionIdAsync()).Should().BeNull();
    }
}
