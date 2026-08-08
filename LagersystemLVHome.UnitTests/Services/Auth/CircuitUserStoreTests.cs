using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using UserSession = LagersystemLVHome.Application.Services.UserSession;

namespace LagersystemLVHome.UnitTests.Services.Auth;

/// <summary>
/// Covers <see cref="CircuitUserStore"/>: per-circuit user/session storage for Blazor Server,
/// keyed by a circuit ID resolved through a 3-strategy fallback (AsyncLocal set by the active
/// circuit handler -&gt; HttpContext.Items -&gt; connection-id mapping).
/// <para/>
/// <see cref="CircuitUserStore.SetCurrentCircuitId"/> sets BOTH the AsyncLocal and
/// HttpContext.Items for the caller's current async flow. Because the AsyncLocal backing
/// field is <c>private static</c>, tests that need to observe a lower-priority fallback
/// strategy (HttpContext.Items only, or connection-mapping only) either avoid calling
/// SetCurrentCircuitId in that test's own async flow, or populate the store from inside a
/// <c>Task.Run</c> - AsyncLocal writes only flow forward into child async operations, never
/// back up to the caller, so this reliably isolates the "populate" step from the
/// "observe via fallback" step without cross-test pollution.
/// </summary>
public class CircuitUserStoreTests
{
    private static IHttpContextAccessor CreateAccessor(HttpContext? context)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);
        return accessor;
    }

    private static UserSession CreateSession(int userId = 1, string username = "alice")
        => new() { UserId = userId, Username = username, Email = $"{username}@test.local", DisplayName = username, WarehouseId = 1 };

    private static CircuitUserStore Build(IHttpContextAccessor accessor)
        => new(accessor, NullLogger<CircuitUserStore>.Instance);

    // ---- No HttpContext / no circuit id anywhere -> fail-safe no-ops ----------------------

    [Fact]
    public void SetUser_NoHttpContextAndNoAsyncLocalCircuitId_LogsErrorAndDoesNotThrow()
    {
        var sut = Build(CreateAccessor(null));

        var act = () => sut.SetUser(CreateSession());

        act.Should().NotThrow();
        sut.GetUser().Should().BeNull("without a resolvable circuit id, SetUser must be a safe no-op");
    }

    [Fact]
    public void GetUser_NoCircuitIdResolvable_ReturnsNull()
    {
        var sut = Build(CreateAccessor(null));

        sut.GetUser().Should().BeNull();
    }

    [Fact]
    public void ClearUser_NoCircuitIdResolvable_DoesNotThrow()
    {
        var sut = Build(CreateAccessor(null));

        var act = () => sut.ClearUser();

        act.Should().NotThrow();
    }

    [Fact]
    public void SetSessionId_NoCircuitIdResolvable_DoesNotThrow()
    {
        var sut = Build(CreateAccessor(null));

        var act = () => sut.SetSessionId("sess-1");

        act.Should().NotThrow();
        sut.GetSessionId().Should().BeNull();
    }

    // ---- Strategy 1: AsyncLocal (set via SetCurrentCircuitId) -----------------------------

    [Fact]
    public void SetUser_ThenGetUser_ViaAsyncLocalCircuitId_RoundTrips()
    {
        var httpContext = new DefaultHttpContext();
        var sut = Build(CreateAccessor(httpContext));
        sut.SetCurrentCircuitId("circuit-async-1");
        var session = CreateSession(userId: 42, username: "bob");

        sut.SetUser(session);

        sut.GetUser().Should().BeSameAs(session);
    }

    [Fact]
    public void SetUser_WithNull_RemovesExistingUser()
    {
        var httpContext = new DefaultHttpContext();
        var sut = Build(CreateAccessor(httpContext));
        sut.SetCurrentCircuitId("circuit-async-2");
        sut.SetUser(CreateSession());

        sut.SetUser(null);

        sut.GetUser().Should().BeNull();
    }

    [Fact]
    public void SetCurrentCircuitId_AlsoPopulatesHttpContextItems()
    {
        var httpContext = new DefaultHttpContext();
        var sut = Build(CreateAccessor(httpContext));

        sut.SetCurrentCircuitId("circuit-items-check");

        httpContext.Items["CircuitId"].Should().Be("circuit-items-check");
    }

    [Fact]
    public void SetCurrentCircuitId_SameConnectionDifferentCircuitId_UpdatesMappingWithoutThrowing()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.Id = "conn-remap";
        var sut = Build(CreateAccessor(httpContext));

        sut.SetCurrentCircuitId("circuit-first");
        var act = () => sut.SetCurrentCircuitId("circuit-second");

        act.Should().NotThrow("re-mapping a connection to a new circuit id must be logged, not throw");
        sut.GetAllConnectionMappings()["conn-remap"].Should().Be("circuit-second");
    }

    [Fact]
    public void SetCurrentCircuitId_NoHttpContext_StillSetsAsyncLocalForGetCurrentCircuitId()
    {
        var sut = Build(CreateAccessor(null));

        sut.SetCurrentCircuitId("circuit-no-http");
        sut.SetUser(CreateSession());

        sut.GetUser().Should().NotBeNull("AsyncLocal strategy must work even without an HttpContext");
    }

    // ---- Strategy 2: HttpContext.Items (fallback when AsyncLocal unset in THIS test) ------

    [Fact]
    public void GetUser_CircuitIdOnlyInHttpContextItems_ResolvesViaFallbackStrategy()
    {
        // This test's own async flow never calls SetCurrentCircuitId, so the static
        // AsyncLocal is unset here - HttpContext.Items is the only source, exercising
        // strategy 2 specifically.
        var httpContext = new DefaultHttpContext();
        httpContext.Items["CircuitId"] = "circuit-from-items";
        var sut = Build(CreateAccessor(httpContext));
        sut.SetUser(CreateSession(userId: 7));

        sut.GetUser()!.UserId.Should().Be(7);
    }

    // ---- Strategy 3: connection-id mapping (populated in an isolated async flow) ----------

    [Fact]
    public async Task GetUser_NoAsyncLocalOrItemsButKnownConnectionMapping_ResolvesViaConnectionFallback()
    {
        // One accessor whose HttpContext can be swapped between phases, so the store's
        // internal connection-id -> circuit-id map (populated in phase 1) can be exercised
        // against a *different*, Items-less HttpContext in phase 2 that merely shares the
        // same Connection.Id.
        var populateContext = new DefaultHttpContext();
        populateContext.Connection.Id = "conn-fallback-1";
        HttpContext current = populateContext;
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(_ => current);
        var sut = Build(accessor);

        // Populate the connection -> circuit mapping (and store a user under that circuit)
        // from inside Task.Run so the AsyncLocal write doesn't leak into this test method's
        // own execution context - see class remarks. The connection-id map itself lives on
        // `sut` (a ConcurrentDictionary field), so it DOES persist past the Task.Run.
        await Task.Run(() =>
        {
            sut.SetCurrentCircuitId("circuit-via-connection");
            sut.SetUser(CreateSession(userId: 9));
        });

        // Swap to a fresh HttpContext with the same connection id but no "CircuitId" in
        // Items - only the connection-id mapping (strategy 3) can resolve the circuit now.
        var freshContext = new DefaultHttpContext();
        freshContext.Connection.Id = "conn-fallback-1";
        current = freshContext;

        var result = sut.GetUser();

        result.Should().NotBeNull();
        result!.UserId.Should().Be(9);
        freshContext.Items["CircuitId"].Should().Be(
            "circuit-via-connection", "the connection-mapping fallback caches the resolved circuit id back into HttpContext.Items for subsequent calls");
    }

    // ---- GetSessionId / SetSessionId -------------------------------------------------------

    [Fact]
    public void SetSessionId_ThenGetSessionId_RoundTrips()
    {
        var httpContext = new DefaultHttpContext();
        var sut = Build(CreateAccessor(httpContext));
        sut.SetCurrentCircuitId("circuit-session-1");

        sut.SetSessionId("sess-abc");

        sut.GetSessionId().Should().Be("sess-abc");
    }

    [Fact]
    public void SetSessionId_EmptyString_RemovesExistingSessionId()
    {
        var httpContext = new DefaultHttpContext();
        var sut = Build(CreateAccessor(httpContext));
        sut.SetCurrentCircuitId("circuit-session-2");
        sut.SetSessionId("sess-to-remove");

        sut.SetSessionId("");

        sut.GetSessionId().Should().BeNull();
    }

    // ---- ClearUser --------------------------------------------------------------------------

    [Fact]
    public void ClearUser_RemovesBothUserAndSessionId()
    {
        var httpContext = new DefaultHttpContext();
        var sut = Build(CreateAccessor(httpContext));
        sut.SetCurrentCircuitId("circuit-clear-1");
        sut.SetUser(CreateSession());
        sut.SetSessionId("sess-1");

        sut.ClearUser();

        sut.GetUser().Should().BeNull();
        sut.GetSessionId().Should().BeNull();
    }

    // ---- RemoveCircuit ------------------------------------------------------------------------

    [Fact]
    public void RemoveCircuit_RemovesUserSessionIdAndConnectionMappings()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.Id = "conn-remove-1";
        var sut = Build(CreateAccessor(httpContext));
        sut.SetCurrentCircuitId("circuit-remove-1");
        sut.SetUser(CreateSession());
        sut.SetSessionId("sess-1");
        sut.GetActiveCircuitCount().Should().Be(1);

        sut.RemoveCircuit("circuit-remove-1");

        sut.GetActiveCircuitCount().Should().Be(0);
        sut.GetAllConnectionMappings().Should().NotContainKey("conn-remove-1");
    }

    [Fact]
    public void RemoveCircuit_UnknownCircuitId_DoesNotThrow()
    {
        var sut = Build(CreateAccessor(null));

        var act = () => sut.RemoveCircuit("never-existed");

        act.Should().NotThrow();
    }

    // ---- GetActiveCircuitCount / GetAllCircuits / GetAllConnectionMappings ----------------

    [Fact]
    public void GetAllCircuits_ReturnsCircuitIdToUsernameMap()
    {
        var httpContext1 = new DefaultHttpContext();
        var sut = Build(CreateAccessor(httpContext1));
        sut.SetCurrentCircuitId("circuit-a");
        sut.SetUser(CreateSession(username: "alice"));

        var all = sut.GetAllCircuits();

        all.Should().ContainKey("circuit-a").WhoseValue.Should().Be("alice");
    }

    [Fact]
    public void GetActiveCircuitCount_ReflectsNumberOfStoredUsers()
    {
        var httpContext = new DefaultHttpContext();
        var sut = Build(CreateAccessor(httpContext));
        sut.SetCurrentCircuitId("circuit-count-1");
        sut.SetUser(CreateSession(userId: 1));

        sut.GetActiveCircuitCount().Should().Be(1);
    }

    // ---- FindCircuitBySessionId ------------------------------------------------------------

    [Fact]
    public void FindCircuitBySessionId_KnownSessionId_ReturnsMatchingCircuitId()
    {
        var httpContext = new DefaultHttpContext();
        var sut = Build(CreateAccessor(httpContext));
        sut.SetCurrentCircuitId("circuit-find-1");
        sut.SetSessionId("sess-findable");

        sut.FindCircuitBySessionId("sess-findable").Should().Be("circuit-find-1");
    }

    [Fact]
    public void FindCircuitBySessionId_UnknownSessionId_ReturnsNull()
    {
        var sut = Build(CreateAccessor(null));

        sut.FindCircuitBySessionId("no-such-session").Should().BeNull();
    }

    [Fact]
    public void FindCircuitBySessionId_EmptySessionId_ReturnsNull()
    {
        var sut = Build(CreateAccessor(null));

        sut.FindCircuitBySessionId("").Should().BeNull();
    }

    // ---- RestoreUserFromDbSession -----------------------------------------------------------

    [Fact]
    public void RestoreUserFromDbSession_ValidInputs_SetsCircuitIdAndStoresUser()
    {
        var httpContext = new DefaultHttpContext();
        var sut = Build(CreateAccessor(httpContext));
        var session = CreateSession(userId: 55, username: "restored-user");

        sut.RestoreUserFromDbSession(session, "circuit-restored-1");

        sut.GetUser().Should().BeSameAs(session, "RestoreUserFromDbSession must set the circuit id AND store the user in one call");
        httpContext.Items["CircuitId"].Should().Be("circuit-restored-1");
    }

    [Fact]
    public void RestoreUserFromDbSession_NullSession_DoesNotThrowAndDoesNotStoreAnything()
    {
        var httpContext = new DefaultHttpContext();
        var sut = Build(CreateAccessor(httpContext));

        var act = () => sut.RestoreUserFromDbSession(null!, "circuit-x");

        act.Should().NotThrow();
        sut.GetActiveCircuitCount().Should().Be(0);
    }

    [Fact]
    public void RestoreUserFromDbSession_EmptyCircuitId_DoesNotThrowAndDoesNotStoreAnything()
    {
        var sut = Build(CreateAccessor(null));

        var act = () => sut.RestoreUserFromDbSession(CreateSession(), "");

        act.Should().NotThrow();
        sut.GetActiveCircuitCount().Should().Be(0);
    }
}
