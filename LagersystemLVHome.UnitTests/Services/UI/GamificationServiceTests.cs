using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.UI;

public class GamificationServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    /// <summary>
    /// A context factory that always fails, used to exercise the try/catch fallback paths
    /// that are otherwise unreachable with a healthy InMemory provider.
    /// </summary>
    private sealed class ThrowingContextFactory : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => throw new InvalidOperationException("Simulated DB failure");

        public Task<InventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated DB failure");
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static GamificationService Build(IDbContextFactory<InventoryDbContext> factory)
        => new(factory, NullLogger<GamificationService>.Instance);

    private static User MakeUser(int id, int warehouseId = 1, bool isActive = true, bool isDeleted = false) => new()
    {
        Id = id,
        Username = $"u{id}",
        Email = $"u{id}@x.local",
        DisplayName = $"User {id}",
        PasswordHash = "x",
        WarehouseId = warehouseId,
        IsActive = isActive,
        IsDeleted = isDeleted
    };

    private static AuditLog MakeAuditLog(int userId, string action, DateTime timestamp, string? details = null) => new()
    {
        UserId = userId,
        Action = action,
        Timestamp = timestamp,
        Details = details
    };

    [Fact]
    public async Task RecordActionAsync_NonPositiveUserId_NoOps()
    {
        var factory = CreateFactory(nameof(RecordActionAsync_NonPositiveUserId_NoOps));
        await Build(factory).RecordActionAsync(0, "STOCK_MOVEMENT");

        await using var db = factory.CreateDbContext();
        (await db.UserGamificationStats.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RecordActionAsync_FirstCall_CreatesStatsAndIncrementsCounter()
    {
        var factory = CreateFactory(nameof(RecordActionAsync_FirstCall_CreatesStatsAndIncrementsCounter));

        await Build(factory).RecordActionAsync(1, "PRODUCT_CREATED");

        await using var db = factory.CreateDbContext();
        var stats = await db.UserGamificationStats.SingleAsync();
        stats.UserId.Should().Be(1);
        stats.ProductsCreated.Should().Be(1);
        stats.CurrentStreak.Should().Be(1);
        stats.LongestStreak.Should().Be(1);
        stats.TotalActiveDays.Should().Be(1);
    }

    [Fact]
    public async Task RecordActionAsync_StockMovementWithScanDetail_IncrementsScans()
    {
        var factory = CreateFactory(nameof(RecordActionAsync_StockMovementWithScanDetail_IncrementsScans));
        var sut = Build(factory);

        await sut.RecordActionAsync(1, "STOCK_MOVEMENT", details: "Scan via Camera");

        await using var db = factory.CreateDbContext();
        var stats = await db.UserGamificationStats.SingleAsync();
        stats.TotalMovements.Should().Be(1);
        stats.TotalScans.Should().Be(1);
    }

    [Fact]
    public async Task RecordActionAsync_LoginAliases_IncrementsTotalLogins()
    {
        var factory = CreateFactory(nameof(RecordActionAsync_LoginAliases_IncrementsTotalLogins));
        var sut = Build(factory);

        await sut.RecordActionAsync(1, "LOGIN_SUCCESS");
        await sut.RecordActionAsync(1, "PASSKEY_LOGIN_SUCCESS");
        await sut.RecordActionAsync(1, "MAGIC_LINK_LOGIN");

        await using var db = factory.CreateDbContext();
        (await db.UserGamificationStats.SingleAsync()).TotalLogins.Should().Be(3);
    }

    [Fact]
    public async Task RecordActionAsync_UnknownAction_StillCreatesStatsButIgnoresCounter()
    {
        var factory = CreateFactory(nameof(RecordActionAsync_UnknownAction_StillCreatesStatsButIgnoresCounter));
        await Build(factory).RecordActionAsync(1, "TOTALLY_NEW_ACTION");

        await using var db = factory.CreateDbContext();
        var stats = await db.UserGamificationStats.SingleAsync();
        stats.TotalMovements.Should().Be(0);
        stats.TotalActiveDays.Should().Be(1, because: "the streak/active-days are updated regardless of action type");
    }

    [Fact]
    public async Task RecordActionAsync_StockMovementWithoutScanDetail_DoesNotIncrementScans()
    {
        var factory = CreateFactory(nameof(RecordActionAsync_StockMovementWithoutScanDetail_DoesNotIncrementScans));
        await Build(factory).RecordActionAsync(1, "STOCK_MOVEMENT", details: "Manual entry");

        await using var db = factory.CreateDbContext();
        var stats = await db.UserGamificationStats.SingleAsync();
        stats.TotalMovements.Should().Be(1);
        stats.TotalScans.Should().Be(0);
    }

    [Theory]
    [InlineData("PRODUCT_UPDATED", nameof(UserGamificationStats.ProductsUpdated))]
    [InlineData("PRODUCT_DELETED", nameof(UserGamificationStats.ProductsDeleted))]
    [InlineData("CATEGORY_CREATED", nameof(UserGamificationStats.CategoriesCreated))]
    [InlineData("STORAGE_LOCATION_CREATED", nameof(UserGamificationStats.StorageLocationsCreated))]
    [InlineData("ROOM_CREATED", nameof(UserGamificationStats.RoomsCreated))]
    [InlineData("IMPORT_SUCCESS", nameof(UserGamificationStats.ImportsCompleted))]
    [InlineData("DATA_IMPORT", nameof(UserGamificationStats.ImportsCompleted))]
    [InlineData("EXPORT", nameof(UserGamificationStats.ExportsCompleted))]
    [InlineData("DATA_EXPORT", nameof(UserGamificationStats.ExportsCompleted))]
    [InlineData("PASSWORD_CHANGED", nameof(UserGamificationStats.PasswordChanges))]
    [InlineData("PASSWORD_RESET_SUCCESS", nameof(UserGamificationStats.PasswordChanges))]
    [InlineData("2FA_ENABLED", nameof(UserGamificationStats.TwoFactorToggles))]
    [InlineData("2FA_DISABLED", nameof(UserGamificationStats.TwoFactorToggles))]
    [InlineData("EMAIL_OTP_ENABLED", nameof(UserGamificationStats.TwoFactorToggles))]
    [InlineData("EMAIL_OTP_DISABLED", nameof(UserGamificationStats.TwoFactorToggles))]
    public async Task RecordActionAsync_KnownAction_IncrementsExpectedCounter(string action, string counterProperty)
    {
        var factory = CreateFactory($"{nameof(RecordActionAsync_KnownAction_IncrementsExpectedCounter)}_{action}");
        await Build(factory).RecordActionAsync(1, action);

        await using var db = factory.CreateDbContext();
        var stats = await db.UserGamificationStats.SingleAsync();
        var value = (int)typeof(UserGamificationStats).GetProperty(counterProperty)!.GetValue(stats)!;
        value.Should().Be(1, because: $"action {action} should increment {counterProperty}");
    }

    [Fact]
    public async Task RecordActionAsync_ConsecutiveDayCall_IncrementsCurrentStreakAndLongestStreak()
    {
        var factory = CreateFactory(nameof(RecordActionAsync_ConsecutiveDayCall_IncrementsCurrentStreakAndLongestStreak));
        await using (var db = factory.CreateDbContext())
        {
            db.UserGamificationStats.Add(new UserGamificationStats
            {
                UserId = 1,
                LastActiveDate = DateTime.UtcNow.Date.AddDays(-1),
                CurrentStreak = 2,
                LongestStreak = 2,
                TotalActiveDays = 2
            });
            await db.SaveChangesAsync();
        }

        await Build(factory).RecordActionAsync(1, "LOGIN_SUCCESS");

        await using var check = factory.CreateDbContext();
        var stats = await check.UserGamificationStats.SingleAsync();
        stats.CurrentStreak.Should().Be(3, "the previous active day was yesterday, so the streak continues");
        stats.LongestStreak.Should().Be(3);
        stats.TotalActiveDays.Should().Be(3);
        stats.LastActiveDate.Should().Be(DateTime.UtcNow.Date);
    }

    [Fact]
    public async Task RecordActionAsync_GapInActivity_ResetsCurrentStreakButKeepsLongest()
    {
        var factory = CreateFactory(nameof(RecordActionAsync_GapInActivity_ResetsCurrentStreakButKeepsLongest));
        await using (var db = factory.CreateDbContext())
        {
            db.UserGamificationStats.Add(new UserGamificationStats
            {
                UserId = 1,
                LastActiveDate = DateTime.UtcNow.Date.AddDays(-5),
                CurrentStreak = 4,
                LongestStreak = 4,
                TotalActiveDays = 4
            });
            await db.SaveChangesAsync();
        }

        await Build(factory).RecordActionAsync(1, "LOGIN_SUCCESS");

        await using var check = factory.CreateDbContext();
        var stats = await check.UserGamificationStats.SingleAsync();
        stats.CurrentStreak.Should().Be(1, "the last active day was more than a day ago, so the streak resets");
        stats.LongestStreak.Should().Be(4, "the historical best streak is preserved");
    }

    [Fact]
    public async Task RecordActionAsync_SecondCallSameDay_DoesNotIncrementActiveDaysOrStreak()
    {
        var factory = CreateFactory(nameof(RecordActionAsync_SecondCallSameDay_DoesNotIncrementActiveDaysOrStreak));
        var sut = Build(factory);

        await sut.RecordActionAsync(1, "PRODUCT_CREATED");
        await sut.RecordActionAsync(1, "PRODUCT_CREATED");

        await using var db = factory.CreateDbContext();
        var stats = await db.UserGamificationStats.SingleAsync();
        stats.ProductsCreated.Should().Be(2, "the action counter itself still increments on every call");
        stats.TotalActiveDays.Should().Be(1, "the streak/active-days block only runs once per calendar day");
        stats.CurrentStreak.Should().Be(1);
    }

    [Fact]
    public async Task RecordActionAsync_ContextFactoryThrows_DoesNotThrow()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.RecordActionAsync(1, "LOGIN_SUCCESS");

        await act.Should().NotThrowAsync();
    }

    // ---- MigrateFromAuditLogsAsync -----------------------------------------

    [Fact]
    public async Task MigrateFromAuditLogsAsync_StatsAlreadyExist_NoOps()
    {
        var factory = CreateFactory(nameof(MigrateFromAuditLogsAsync_StatsAlreadyExist_NoOps));
        await using (var db = factory.CreateDbContext())
        {
            db.UserGamificationStats.Add(new UserGamificationStats { UserId = 1, TotalMovements = 42 });
            db.AuditLogs.Add(MakeAuditLog(1, "STOCK_MOVEMENT", DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        await Build(factory).MigrateFromAuditLogsAsync(1);

        await using var check = factory.CreateDbContext();
        (await check.UserGamificationStats.SingleAsync()).TotalMovements.Should().Be(42, "an existing stats row must not be overwritten");
    }

    [Fact]
    public async Task MigrateFromAuditLogsAsync_NoAuditLogs_DoesNotCreateStats()
    {
        var factory = CreateFactory(nameof(MigrateFromAuditLogsAsync_NoAuditLogs_DoesNotCreateStats));

        await Build(factory).MigrateFromAuditLogsAsync(1);

        await using var check = factory.CreateDbContext();
        (await check.UserGamificationStats.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task MigrateFromAuditLogsAsync_ContextFactoryThrows_DoesNotThrow()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.MigrateFromAuditLogsAsync(1);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MigrateFromAuditLogsAsync_ComputesCountersAndStreaksFromAuditHistory()
    {
        var factory = CreateFactory(nameof(MigrateFromAuditLogsAsync_ComputesCountersAndStreaksFromAuditHistory));
        var today = DateTime.UtcNow.Date;

        // Two runs of active dates: an older 5-day run (the longest streak) and a trailing
        // 3-day run ending yesterday (the current streak, since "today" itself has no activity).
        var longRun = new[] { -10, -9, -8, -7, -6 }.Select(d => today.AddDays(d)).ToArray();
        var recentRun = new[] { -3, -2, -1 }.Select(d => today.AddDays(d)).ToArray();
        var allDates = longRun.Concat(recentRun).ToArray();

        await using (var db = factory.CreateDbContext())
        {
            foreach (var date in allDates)
            {
                db.AuditLogs.Add(MakeAuditLog(1, "STOCK_MOVEMENT", date.AddHours(8)));
            }
            // Two scans among the stock movements.
            db.AuditLogs.Add(MakeAuditLog(1, "STOCK_MOVEMENT", longRun[0].AddHours(9), details: "Scan via camera"));
            db.AuditLogs.Add(MakeAuditLog(1, "STOCK_MOVEMENT", longRun[1].AddHours(9), details: "Scan via camera"));

            // All other action types recorded on an already-active date so the active-date set is unchanged.
            var d = longRun[0];
            db.AuditLogs.AddRange(
                MakeAuditLog(1, "PRODUCT_CREATED", d),
                MakeAuditLog(1, "PRODUCT_UPDATED", d),
                MakeAuditLog(1, "PRODUCT_DELETED", d),
                MakeAuditLog(1, "CATEGORY_CREATED", d),
                MakeAuditLog(1, "STORAGE_LOCATION_CREATED", d),
                MakeAuditLog(1, "ROOM_CREATED", d),
                MakeAuditLog(1, "IMPORT_SUCCESS", d),
                MakeAuditLog(1, "DATA_IMPORT", d),
                MakeAuditLog(1, "EXPORT", d),
                MakeAuditLog(1, "DATA_EXPORT", d),
                MakeAuditLog(1, "PASSWORD_CHANGED", d),
                MakeAuditLog(1, "PASSWORD_RESET_SUCCESS", d),
                MakeAuditLog(1, "LOGIN_SUCCESS", d),
                MakeAuditLog(1, "PASSKEY_LOGIN_SUCCESS", d),
                MakeAuditLog(1, "MAGIC_LINK_LOGIN", d),
                MakeAuditLog(1, "2FA_ENABLED", d),
                MakeAuditLog(1, "2FA_DISABLED", d),
                MakeAuditLog(1, "EMAIL_OTP_ENABLED", d),
                MakeAuditLog(1, "EMAIL_OTP_DISABLED", d));

            // Audit log for a different user must not leak into user 1's stats.
            db.AuditLogs.Add(MakeAuditLog(2, "STOCK_MOVEMENT", today));
            await db.SaveChangesAsync();
        }

        await Build(factory).MigrateFromAuditLogsAsync(1);

        await using var check = factory.CreateDbContext();
        var stats = await check.UserGamificationStats.SingleAsync(s => s.UserId == 1);

        stats.TotalMovements.Should().Be(10, "8 baseline movements plus 2 extra scan movements added on top");
        stats.TotalScans.Should().Be(2);
        stats.ProductsCreated.Should().Be(1);
        stats.ProductsUpdated.Should().Be(1);
        stats.ProductsDeleted.Should().Be(1);
        stats.CategoriesCreated.Should().Be(1);
        stats.StorageLocationsCreated.Should().Be(1);
        stats.RoomsCreated.Should().Be(1);
        stats.ImportsCompleted.Should().Be(2);
        stats.ExportsCompleted.Should().Be(2);
        stats.PasswordChanges.Should().Be(2);
        stats.TotalLogins.Should().Be(3);
        stats.TwoFactorToggles.Should().Be(4);
        stats.TotalActiveDays.Should().Be(8);
        stats.LongestStreak.Should().Be(5, "the 5-day run in the past is the longest observed run");
        stats.CurrentStreak.Should().Be(3, "the trailing run ending yesterday is 3 days long");
        stats.LastActiveDate.Should().Be(recentRun[^1]);
    }

    [Fact]
    public async Task MigrateFromAuditLogsAsync_ActiveToday_AnchorsCurrentStreakOnToday()
    {
        var factory = CreateFactory(nameof(MigrateFromAuditLogsAsync_ActiveToday_AnchorsCurrentStreakOnToday));
        var today = DateTime.UtcNow.Date;

        await using (var db = factory.CreateDbContext())
        {
            // Three consecutive active days including today itself, exercising the
            // "activeDates.Contains(today)" branch of the streak-anchor calculation.
            db.AuditLogs.AddRange(
                MakeAuditLog(1, "LOGIN_SUCCESS", today.AddDays(-2).AddHours(8)),
                MakeAuditLog(1, "LOGIN_SUCCESS", today.AddDays(-1).AddHours(8)),
                MakeAuditLog(1, "LOGIN_SUCCESS", today.AddHours(8)));
            await db.SaveChangesAsync();
        }

        await Build(factory).MigrateFromAuditLogsAsync(1);

        await using var check = factory.CreateDbContext();
        var stats = await check.UserGamificationStats.SingleAsync(s => s.UserId == 1);
        stats.CurrentStreak.Should().Be(3);
        stats.LongestStreak.Should().Be(3);
        stats.LastActiveDate.Should().Be(today);
    }

    // ---- GetUserProfileAsync -----------------------------------------------

    [Fact]
    public async Task GetUserProfileAsync_NoExistingDataAtAll_ReturnsZeroedProfile()
    {
        var factory = CreateFactory(nameof(GetUserProfileAsync_NoExistingDataAtAll_ReturnsZeroedProfile));

        var profile = await Build(factory).GetUserProfileAsync(1);

        profile.UserId.Should().Be(1);
        profile.TotalXP.Should().Be(0);
        profile.Level.Should().Be(1);
        profile.XPForNextLevel.Should().Be(100);
        profile.XPInCurrentLevel.Should().Be(0);
        profile.MemberSince.Should().Be(default(DateTime), "no matching User row exists");
    }

    [Fact]
    public async Task GetUserProfileAsync_ExactLevelBoundary_BumpsToNextLevelWithZeroRemainder()
    {
        var factory = CreateFactory(nameof(GetUserProfileAsync_ExactLevelBoundary_BumpsToNextLevelWithZeroRemainder));
        await using (var db = factory.CreateDbContext())
        {
            // TotalActiveDays * 20 == 100 XP exactly, which is the level-1 threshold.
            db.UserGamificationStats.Add(new UserGamificationStats { UserId = 1, TotalActiveDays = 5 });
            await db.SaveChangesAsync();
        }

        var profile = await Build(factory).GetUserProfileAsync(1);

        profile.TotalXP.Should().Be(100);
        profile.Level.Should().Be(2);
        profile.XPForNextLevel.Should().Be(200);
        profile.XPInCurrentLevel.Should().Be(0);
    }

    [Fact]
    public async Task GetUserProfileAsync_ComputesXpFromAllCountersAndReadsMemberSince()
    {
        var factory = CreateFactory(nameof(GetUserProfileAsync_ComputesXpFromAllCountersAndReadsMemberSince));
        var memberSince = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(new User { Id = 1, Username = "u1", Email = "u1@x.local", PasswordHash = "x", CreatedAt = memberSince });
            db.UserGamificationStats.Add(new UserGamificationStats
            {
                UserId = 1,
                TotalMovements = 2,   // 2*10=20
                TotalScans = 1,       // 1*15=15
                ProductsCreated = 1,  // 1*25=25
            });
            // One movement clearly within "this month and this week" (now itself).
            db.AuditLogs.Add(MakeAuditLog(1, "STOCK_MOVEMENT", DateTime.UtcNow));
            // One movement clearly before this month, excluded from both windows.
            db.AuditLogs.Add(MakeAuditLog(1, "STOCK_MOVEMENT", DateTime.UtcNow.AddMonths(-2)));
            await db.SaveChangesAsync();
        }

        var profile = await Build(factory).GetUserProfileAsync(1);

        profile.TotalXP.Should().Be(20 + 15 + 25);
        profile.MemberSince.Should().Be(memberSince);
        profile.MonthlyMovements.Should().Be(1);
        profile.WeeklyMovements.Should().Be(1);
    }

    // ---- GetLeaderboardAsync ------------------------------------------------

    [Fact]
    public async Task GetLeaderboardAsync_ExcludesInactiveDeletedAndZeroXpUsers_OrdersByXpDescending()
    {
        var factory = CreateFactory(nameof(GetLeaderboardAsync_ExcludesInactiveDeletedAndZeroXpUsers_OrdersByXpDescending));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(new Warehouse { Id = 1, Name = "WH1", Code = "W001", Address = "a" });
            db.Users.AddRange(
                MakeUser(1, warehouseId: 1),                    // active, has XP -> included
                MakeUser(2, warehouseId: 1),                    // active, zero XP -> excluded
                MakeUser(3, warehouseId: 1, isActive: false),   // inactive -> excluded
                MakeUser(4, warehouseId: 1, isDeleted: true));  // deleted -> excluded
            db.UserGamificationStats.AddRange(
                new UserGamificationStats { UserId = 1, TotalMovements = 5 },  // 50 XP
                new UserGamificationStats { UserId = 2, TotalMovements = 0 },  // 0 XP
                new UserGamificationStats { UserId = 3, TotalMovements = 5 },
                new UserGamificationStats { UserId = 4, TotalMovements = 5 });
            await db.SaveChangesAsync();
        }

        var leaderboard = await Build(factory).GetLeaderboardAsync(warehouseId: null);

        leaderboard.Should().ContainSingle().Which.UserId.Should().Be(1);
    }

    [Fact]
    public async Task GetLeaderboardAsync_FiltersByWarehouseAndOrdersDescending()
    {
        var factory = CreateFactory(nameof(GetLeaderboardAsync_FiltersByWarehouseAndOrdersDescending));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.AddRange(
                new Warehouse { Id = 1, Name = "WH1", Code = "W001", Address = "a" },
                new Warehouse { Id = 2, Name = "WH2", Code = "W002", Address = "b" });
            db.Users.AddRange(
                MakeUser(1, warehouseId: 1),
                MakeUser(2, warehouseId: 1),
                MakeUser(3, warehouseId: 2));
            db.UserGamificationStats.AddRange(
                new UserGamificationStats { UserId = 1, TotalMovements = 2 },  // 20 XP
                new UserGamificationStats { UserId = 2, TotalMovements = 10 }, // 100 XP
                new UserGamificationStats { UserId = 3, TotalMovements = 50 }); // other warehouse
            await db.SaveChangesAsync();
        }

        var leaderboard = await Build(factory).GetLeaderboardAsync(warehouseId: 1);

        leaderboard.Should().HaveCount(2);
        leaderboard[0].UserId.Should().Be(2, "higher XP should be ranked first");
        leaderboard[1].UserId.Should().Be(1);
    }

    // ---- GetAchievementsAsync -----------------------------------------------

    [Fact]
    public async Task GetAchievementsAsync_ReturnsFullCatalogueWithCorrectUnlockState()
    {
        var factory = CreateFactory(nameof(GetAchievementsAsync_ReturnsFullCatalogueWithCorrectUnlockState));
        await using (var db = factory.CreateDbContext())
        {
            db.UserGamificationStats.Add(new UserGamificationStats { UserId = 1, TotalMovements = 1 });
            await db.SaveChangesAsync();
        }

        var achievements = await Build(factory).GetAchievementsAsync(1);

        achievements.Should().HaveCount(49, "this mirrors the hard-coded achievement catalogue in GamificationService");
        var firstSteps = achievements.Single(a => a.Name == "Erste Schritte");
        firstSteps.IsUnlocked.Should().BeTrue();
        firstSteps.Progress.Should().Be(1.0);

        var fifty = achievements.Single(a => a.Name == "Fleißig");
        fifty.IsUnlocked.Should().BeFalse();
        fifty.Progress.Should().BeApproximately(1.0 / 50, 0.0001);
    }

    // ---- GetStreakInfoAsync --------------------------------------------------

    [Fact]
    public async Task GetStreakInfoAsync_LastActiveToday_IsActiveTodayTrue()
    {
        var factory = CreateFactory(nameof(GetStreakInfoAsync_LastActiveToday_IsActiveTodayTrue));
        await using (var db = factory.CreateDbContext())
        {
            db.UserGamificationStats.Add(new UserGamificationStats
            {
                UserId = 1,
                CurrentStreak = 3,
                LongestStreak = 5,
                TotalActiveDays = 10,
                LastActiveDate = DateTime.UtcNow.Date
            });
            await db.SaveChangesAsync();
        }

        var info = await Build(factory).GetStreakInfoAsync(1);

        info.IsActiveToday.Should().BeTrue();
        info.CurrentStreak.Should().Be(3);
        info.LongestStreak.Should().Be(5);
        info.TotalActiveDays.Should().Be(10);
    }

    [Fact]
    public async Task GetStreakInfoAsync_LastActiveYesterday_IsActiveTodayFalse()
    {
        var factory = CreateFactory(nameof(GetStreakInfoAsync_LastActiveYesterday_IsActiveTodayFalse));
        await using (var db = factory.CreateDbContext())
        {
            db.UserGamificationStats.Add(new UserGamificationStats
            {
                UserId = 1,
                LastActiveDate = DateTime.UtcNow.Date.AddDays(-1)
            });
            await db.SaveChangesAsync();
        }

        var info = await Build(factory).GetStreakInfoAsync(1);

        info.IsActiveToday.Should().BeFalse();
    }

    [Fact]
    public async Task GetStreakInfoAsync_NoExistingStats_CreatesRowAndReturnsZeroed()
    {
        var factory = CreateFactory(nameof(GetStreakInfoAsync_NoExistingStats_CreatesRowAndReturnsZeroed));

        var info = await Build(factory).GetStreakInfoAsync(1);

        info.CurrentStreak.Should().Be(0);
        info.LongestStreak.Should().Be(0);
        info.TotalActiveDays.Should().Be(0);
        info.IsActiveToday.Should().BeFalse();

        await using var check = factory.CreateDbContext();
        (await check.UserGamificationStats.CountAsync()).Should().Be(1, "GetOrCreateStatsAsync persists a new row on first access");
    }
}
