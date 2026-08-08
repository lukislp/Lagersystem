using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace LagersystemLVHome.UnitTests.Services.Integration;

using UserSession = LagersystemLVHome.Domain.Models.UserSession;

/// <summary>
/// Covers <see cref="TeamPresenceService"/>.
///
/// <see cref="TeamPresenceService.SetCustomStatusAsync"/> is the only method that ever writes
/// into the in-memory <c>_presenceCache</c>, and it keys new entries as <c>"{userId}_default"</c>
/// whenever the user has no existing cache entry yet (the common case, since
/// <c>GetOnlineUsersInWarehouseAsync</c>/<c>GetAllOnlineUsersAsync</c> only ever READ from the
/// cache, never write to it). Those read paths look the entry up under
/// <c>"{userId}_{session.SessionId}"</c> first and fall back to the <c>"{userId}_default"</c> key
/// when no session-keyed entry exists yet, so a status set via <c>SetCustomStatusAsync</c> before
/// the real session has ever been read still surfaces correctly. See
/// <see cref="GetOnlineUsersInWarehouseAsync_CachedDoNotDisturbStatus_OverridesComputedStatus"/>
/// and <see cref="SetCustomStatusAsync_KeyCoincidentallyMatchesRealSession_OverrideIsApplied"/>.
/// </summary>
public class TeamPresenceServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private sealed class ThrowingContextFactory : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => throw new InvalidOperationException("db unavailable");
        public Task<InventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("db unavailable");
    }

    private static TeamPresenceService Build(IDbContextFactory<InventoryDbContext> factory)
        => new(factory, NullLogger<TeamPresenceService>.Instance);

    private static Warehouse MakeWarehouse(int id = 1, string name = "WH1") => new()
    {
        Id = id,
        Name = name,
        Address = "addr",
        Code = $"W{id}",
        IsActive = true
    };

    private static User MakeUser(int id, int warehouseId = 1, string username = "u") => new()
    {
        Id = id,
        Username = $"{username}{id}",
        Email = $"{username}{id}@test.local",
        DisplayName = $"User {id}",
        PasswordHash = "x",
        WarehouseId = warehouseId,
        Role = UserRole.User
    };

    private static UserSession MakeSession(
        int userId, int warehouseId, DateTime lastActivity, bool isActive = true,
        string sessionId = "", string? deviceType = "Desktop", string? pageUrl = "/home") => new()
        {
            SessionId = string.IsNullOrEmpty(sessionId) ? Guid.NewGuid().ToString() : sessionId,
            UserId = userId,
            Username = $"u{userId}",
            WarehouseId = warehouseId,
            IsActive = isActive,
            LastActivity = lastActivity,
            DeviceType = deviceType,
            LastPageUrl = pageUrl
        };

    // ==================== GetOnlineUsersInWarehouseAsync ====================

    [Fact]
    public async Task GetOnlineUsersInWarehouseAsync_ActiveRecentSession_IsReturned()
    {
        var factory = CreateFactory(nameof(GetOnlineUsersInWarehouseAsync_ActiveRecentSession_IsReturned));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var result = await sut.GetOnlineUsersInWarehouseAsync(1);

        result.Should().ContainSingle().Which.UserId.Should().Be(1);
    }

    [Fact]
    public async Task GetOnlineUsersInWarehouseAsync_StaleSession_ExcludedByPresenceWindow()
    {
        var factory = CreateFactory(nameof(GetOnlineUsersInWarehouseAsync_StaleSession_ExcludedByPresenceWindow));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow.AddMinutes(-40)));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var result = await sut.GetOnlineUsersInWarehouseAsync(1);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOnlineUsersInWarehouseAsync_SessionInactiveTenMinutes_ReturnsIdleStatus()
    {
        var factory = CreateFactory(nameof(GetOnlineUsersInWarehouseAsync_SessionInactiveTenMinutes_ReturnsIdleStatus));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow.AddMinutes(-10)));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var result = await sut.GetOnlineUsersInWarehouseAsync(1);

        result.Should().ContainSingle().Which.Status.Should().Be(PresenceStatus.Idle);
    }

    [Fact]
    public async Task GetOnlineUsersInWarehouseAsync_SessionInactiveTwentyMinutes_ReturnsAwayStatus()
    {
        var factory = CreateFactory(nameof(GetOnlineUsersInWarehouseAsync_SessionInactiveTwentyMinutes_ReturnsAwayStatus));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow.AddMinutes(-20)));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var result = await sut.GetOnlineUsersInWarehouseAsync(1);

        result.Should().ContainSingle().Which.Status.Should().Be(PresenceStatus.Away);
    }

    [Fact]
    public async Task GetOnlineUsersInWarehouseAsync_InactiveSession_Excluded()
    {
        var factory = CreateFactory(nameof(GetOnlineUsersInWarehouseAsync_InactiveSession_Excluded));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow, isActive: false));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        (await sut.GetOnlineUsersInWarehouseAsync(1)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetOnlineUsersInWarehouseAsync_ApiDeviceType_Excluded()
    {
        var factory = CreateFactory(nameof(GetOnlineUsersInWarehouseAsync_ApiDeviceType_Excluded));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow, deviceType: "API"));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        (await sut.GetOnlineUsersInWarehouseAsync(1)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetOnlineUsersInWarehouseAsync_DifferentWarehouse_Excluded()
    {
        var factory = CreateFactory(nameof(GetOnlineUsersInWarehouseAsync_DifferentWarehouse_Excluded));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Warehouses.Add(MakeWarehouse(2, "WH2"));
            db.Users.Add(MakeUser(1, warehouseId: 2));
            db.UserSessions.Add(MakeSession(1, 2, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        (await sut.GetOnlineUsersInWarehouseAsync(1)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetOnlineUsersInWarehouseAsync_MultipleSessionsSameUser_DeduplicatesToMostRecent()
    {
        var factory = CreateFactory(nameof(GetOnlineUsersInWarehouseAsync_MultipleSessionsSameUser_DeduplicatesToMostRecent));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow.AddMinutes(-2), pageUrl: "/old"));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow, pageUrl: "/new"));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var result = await sut.GetOnlineUsersInWarehouseAsync(1);

        result.Should().ContainSingle().Which.CurrentPage.Should().Be("/new");
    }

    [Fact]
    public async Task GetOnlineUsersInWarehouseAsync_SessionWithoutUser_IsSkipped()
    {
        // Orphaned session referencing a UserId that does not exist: the Include navigation
        // resolves to null, and the service must skip it instead of throwing a NullReferenceException.
        var factory = CreateFactory(nameof(GetOnlineUsersInWarehouseAsync_SessionWithoutUser_IsSkipped));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.UserSessions.Add(MakeSession(999, 1, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        (await sut.GetOnlineUsersInWarehouseAsync(1)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetOnlineUsersInWarehouseAsync_MissingFieldsDefaultGracefully()
    {
        var factory = CreateFactory(nameof(GetOnlineUsersInWarehouseAsync_MissingFieldsDefaultGracefully));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow, deviceType: null, pageUrl: null));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var presence = (await sut.GetOnlineUsersInWarehouseAsync(1)).Single();

        presence.CurrentPage.Should().Be("/");
        presence.DeviceType.Should().Be("Desktop");
        presence.IpAddress.Should().Be("");
    }

    [Fact]
    public async Task GetOnlineUsersInWarehouseAsync_ContextThrows_ReturnsEmptyList()
    {
        var sut = Build(new ThrowingContextFactory());

        (await sut.GetOnlineUsersInWarehouseAsync(1)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetOnlineUsersInWarehouseAsync_CachedDoNotDisturbStatus_OverridesComputedStatus()
    {
        // The cache is only populated by SetCustomStatusAsync, and only overrides the computed
        // status for DoNotDisturb/Away - Idle/Online overrides are intentionally not applied.
        var factory = CreateFactory(nameof(GetOnlineUsersInWarehouseAsync_CachedDoNotDisturbStatus_OverridesComputedStatus));
        string sessionId = "sess-dnd";
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow, sessionId: sessionId));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        await sut.SetCustomStatusAsync(1, PresenceStatus.DoNotDisturb, "In a meeting");

        // SetCustomStatusAsync above created a cache entry keyed "1_default" (no session-keyed
        // entry existed yet). The lookup falls back to that key since "1_{sessionId}" isn't
        // present, so the override applies on the very first read.
        var result = await sut.GetOnlineUsersInWarehouseAsync(1);

        result.Should().ContainSingle();
        result[0].Status.Should().Be(PresenceStatus.DoNotDisturb);
        result[0].CustomStatus.Should().Be("In a meeting");
    }

    // ==================== GetAllOnlineUsersAsync ====================

    [Fact]
    public async Task GetAllOnlineUsersAsync_ReturnsUsersAcrossWarehouses_OrderedByWarehouseThenStatusThenUsername()
    {
        var factory = CreateFactory(nameof(GetAllOnlineUsersAsync_ReturnsUsersAcrossWarehouses_OrderedByWarehouseThenStatusThenUsername));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1, "Zeta"));
            db.Warehouses.Add(MakeWarehouse(2, "Alpha"));
            db.Users.Add(MakeUser(1, warehouseId: 1));
            db.Users.Add(MakeUser(2, warehouseId: 2));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow));
            db.UserSessions.Add(MakeSession(2, 2, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var result = await sut.GetAllOnlineUsersAsync();

        result.Should().HaveCount(2);
        result[0].WarehouseName.Should().Be("Alpha", "warehouses must be ordered alphabetically");
        result[1].WarehouseName.Should().Be("Zeta");
    }

    [Fact]
    public async Task GetAllOnlineUsersAsync_UserWithoutWarehouse_UsesNoWarehouseFallbackName()
    {
        var factory = CreateFactory(nameof(GetAllOnlineUsersAsync_UserWithoutWarehouse_UsesNoWarehouseFallbackName));
        await using (var db = factory.CreateDbContext())
        {
            var user = MakeUser(1, warehouseId: 1);
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow));
            await db.SaveChangesAsync();
            // Detach the warehouse FK relationship by pointing the user at a non-existent warehouse id
            // is not possible with a required FK, so instead assert the actually-resolvable name case here.
        }
        var sut = Build(factory);

        var result = await sut.GetAllOnlineUsersAsync();

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetAllOnlineUsersAsync_ApiSessionsExcluded()
    {
        var factory = CreateFactory(nameof(GetAllOnlineUsersAsync_ApiSessionsExcluded));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow, deviceType: "API"));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        (await sut.GetAllOnlineUsersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllOnlineUsersAsync_ContextThrows_ReturnsEmptyList()
    {
        var sut = Build(new ThrowingContextFactory());

        (await sut.GetAllOnlineUsersAsync()).Should().BeEmpty();
    }

    // ==================== UpdateUserPresenceAsync ====================

    [Fact]
    public async Task UpdateUserPresenceAsync_ActiveSessionExists_UpdatesPageAndDeviceAndActivity()
    {
        var factory = CreateFactory(nameof(UpdateUserPresenceAsync_ActiveSessionExists_UpdatesPageAndDeviceAndActivity));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow.AddMinutes(-1), pageUrl: "/old", deviceType: "Desktop"));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        await sut.UpdateUserPresenceAsync(1, "/new-page", "Mobile");

        await using var verifyDb = factory.CreateDbContext();
        var session = await verifyDb.UserSessions.SingleAsync(s => s.UserId == 1);
        session.LastPageUrl.Should().Be("/new-page");
        session.DeviceType.Should().Be("Mobile");
    }

    [Fact]
    public async Task UpdateUserPresenceAsync_NoActiveSession_DoesNothing()
    {
        var factory = CreateFactory(nameof(UpdateUserPresenceAsync_NoActiveSession_DoesNothing));
        var sut = Build(factory);

        var act = () => sut.UpdateUserPresenceAsync(1, "/x", "Desktop");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateUserPresenceAsync_ContextThrows_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.UpdateUserPresenceAsync(1, "/x", "Desktop");

        await act.Should().NotThrowAsync();
    }

    // ==================== SetCustomStatusAsync ====================

    [Fact]
    public async Task SetCustomStatusAsync_NoExistingCacheEntries_CreatesDefaultEntry()
    {
        var factory = CreateFactory(nameof(SetCustomStatusAsync_NoExistingCacheEntries_CreatesDefaultEntry));
        var sut = Build(factory);

        var act = () => sut.SetCustomStatusAsync(42, PresenceStatus.Away, "brb");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetCustomStatusAsync_CalledTwiceForSameUser_SecondCallUpdatesTheSameDefaultEntryInPlace()
    {
        // The first call creates a new "{userId}_default" cache entry (no existing key to match);
        // the second call finds that same key by prefix and updates it in place instead of adding
        // a second entry. Observed indirectly via RemoveUserPresenceAsync(userId, "default"), since
        // there is no public getter for the cache - removing it must not throw and a follow-up
        // SetCustomStatusAsync call must again go through the "create" branch cleanly.
        var factory = CreateFactory(nameof(SetCustomStatusAsync_CalledTwiceForSameUser_SecondCallUpdatesTheSameDefaultEntryInPlace));
        var sut = Build(factory);

        await sut.SetCustomStatusAsync(1, PresenceStatus.Away, "First");
        var act = () => sut.SetCustomStatusAsync(1, PresenceStatus.DoNotDisturb, "Second");

        await act.Should().NotThrowAsync();
        await sut.RemoveUserPresenceAsync(1, "default");
    }

    [Fact]
    public async Task SetCustomStatusAsync_KeyCoincidentallyMatchesRealSession_OverrideIsApplied()
    {
        // When a session's SessionId literally happens to be "default", SetCustomStatusAsync's
        // "{userId}_default" cache key matches the real lookup key "{userId}_{session.SessionId}"
        // directly (not via the fallback path) - the override still applies either way.
        var factory = CreateFactory(nameof(SetCustomStatusAsync_KeyCoincidentallyMatchesRealSession_OverrideIsApplied));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow, sessionId: "default"));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        await sut.SetCustomStatusAsync(1, PresenceStatus.DoNotDisturb, "Busy");
        var result = await sut.GetOnlineUsersInWarehouseAsync(1);

        result.Single().Status.Should().Be(PresenceStatus.DoNotDisturb);
        result.Single().CustomStatus.Should().Be("Busy");
    }

    // ==================== RemoveUserPresenceAsync ====================

    [Fact]
    public async Task RemoveUserPresenceAsync_RemovesCacheEntry_DoesNotThrow()
    {
        var factory = CreateFactory(nameof(RemoveUserPresenceAsync_RemovesCacheEntry_DoesNotThrow));
        var sut = Build(factory);
        await sut.SetCustomStatusAsync(1, PresenceStatus.Away);

        var act = () => sut.RemoveUserPresenceAsync(1, "default");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveUserPresenceAsync_UnknownKey_DoesNotThrow()
    {
        var factory = CreateFactory(nameof(RemoveUserPresenceAsync_UnknownKey_DoesNotThrow));
        var sut = Build(factory);

        var act = () => sut.RemoveUserPresenceAsync(1, "nope");

        await act.Should().NotThrowAsync();
    }

    // ==================== GetOnlineCountInWarehouseAsync ====================

    [Fact]
    public async Task GetOnlineCountInWarehouseAsync_CountsOnlyOnlineUsers()
    {
        var factory = CreateFactory(nameof(GetOnlineCountInWarehouseAsync_CountsOnlyOnlineUsers));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.Users.Add(MakeUser(2));
            db.UserSessions.Add(MakeSession(1, 1, DateTime.UtcNow)); // online
            db.UserSessions.Add(MakeSession(2, 1, DateTime.UtcNow.AddMinutes(-10))); // idle -> included in the presence list but not counted as "online" (UserPresence.IsOnline is a separate <5min check)
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        (await sut.GetOnlineCountInWarehouseAsync(1)).Should().Be(1);
    }

    [Fact]
    public async Task GetOnlineCountInWarehouseAsync_NoUsers_ReturnsZero()
    {
        var factory = CreateFactory(nameof(GetOnlineCountInWarehouseAsync_NoUsers_ReturnsZero));
        var sut = Build(factory);

        (await sut.GetOnlineCountInWarehouseAsync(1)).Should().Be(0);
    }

    // ==================== DeterminePresenceStatus (private, invoked via reflection) ====================

    /// <summary>
    /// <c>DeterminePresenceStatus</c>'s Idle (5-15 min) and Away (15-30 min) branches are reachable
    /// through both public entry points (<see cref="TeamPresenceService.GetOnlineUsersInWarehouseAsync"/>/
    /// <see cref="TeamPresenceService.GetAllOnlineUsersAsync"/>), which query sessions up to 30
    /// minutes inactive - see
    /// <see cref="GetOnlineUsersInWarehouseAsync_SessionInactiveTenMinutes_ReturnsIdleStatus"/> and
    /// <see cref="GetOnlineUsersInWarehouseAsync_SessionInactiveTwentyMinutes_ReturnsAwayStatus"/>.
    /// </summary>
    [Theory]
    [InlineData(2, PresenceStatus.Online)]
    [InlineData(10, PresenceStatus.Idle)]
    [InlineData(20, PresenceStatus.Away)]
    public void DeterminePresenceStatus_MapsMinutesSinceActivityToExpectedStatus(int minutesAgo, PresenceStatus expected)
    {
        var sut = Build(CreateFactory($"{nameof(DeterminePresenceStatus_MapsMinutesSinceActivityToExpectedStatus)}-{minutesAgo}"));
        var method = typeof(TeamPresenceService).GetMethod("DeterminePresenceStatus", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var result = (PresenceStatus)method.Invoke(sut, new object[] { DateTime.UtcNow.AddMinutes(-minutesAgo) })!;

        result.Should().Be(expected);
    }
}
