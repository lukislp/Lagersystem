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

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static GamificationService Build(IDbContextFactory<InventoryDbContext> factory)
        => new(factory, NullLogger<GamificationService>.Instance);

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
}
