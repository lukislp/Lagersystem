using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Inventory;

public class PriceHistoryServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static PriceHistoryService CreateSut(IDbContextFactory<InventoryDbContext> factory)
    {
        var auth = Substitute.For<IAuthService>();
        auth.GetCurrentWarehouseId().Returns(1);
        var audit = Substitute.For<IAuditService>();
        return new PriceHistoryService(factory, auth, audit, NullLogger<PriceHistoryService>.Instance);
    }

    private static async Task SeedPricesAsync(
        IDbContextFactory<InventoryDbContext> factory, params ProductPrice[] prices)
    {
        await using var db = factory.CreateDbContext();
        db.ProductPrices.AddRange(prices);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetCurrentPriceAsync_ReturnsEntryCoveringNow()
    {
        var factory = CreateFactory(nameof(GetCurrentPriceAsync_ReturnsEntryCoveringNow));
        var now = DateTime.UtcNow;
        await SeedPricesAsync(factory,
            new ProductPrice { ProductId = 1, Price = 10m, ValidFrom = now.AddDays(-10), ValidTo = now.AddDays(-5) },
            new ProductPrice { ProductId = 1, Price = 12m, ValidFrom = now.AddDays(-4), ValidTo = null });
        var sut = CreateSut(factory);

        var current = await sut.GetCurrentPriceAsync(1);

        current.Should().NotBeNull();
        current!.Price.Should().Be(12m);
    }

    [Fact]
    public async Task GetCurrentPriceAsync_ReturnsNullWhenNothingActive()
    {
        var factory = CreateFactory(nameof(GetCurrentPriceAsync_ReturnsNullWhenNothingActive));
        var now = DateTime.UtcNow;
        await SeedPricesAsync(factory,
            new ProductPrice { ProductId = 2, Price = 5m, ValidFrom = now.AddDays(-10), ValidTo = now.AddDays(-5) });
        var sut = CreateSut(factory);

        (await sut.GetCurrentPriceAsync(2)).Should().BeNull();
    }

    [Fact]
    public async Task GetPriceAtDateAsync_PicksEntryValidAtThatDate()
    {
        var factory = CreateFactory(nameof(GetPriceAtDateAsync_PicksEntryValidAtThatDate));
        var now = DateTime.UtcNow;
        await SeedPricesAsync(factory,
            new ProductPrice { ProductId = 1, Price = 10m, ValidFrom = now.AddDays(-20), ValidTo = now.AddDays(-10) },
            new ProductPrice { ProductId = 1, Price = 20m, ValidFrom = now.AddDays(-9), ValidTo = null });
        var sut = CreateSut(factory);

        (await sut.GetPriceAtDateAsync(1, now.AddDays(-15)))!.Price.Should().Be(10m);
        (await sut.GetPriceAtDateAsync(1, now))!.Price.Should().Be(20m);
    }

    [Fact]
    public async Task GetPriceHistoryAsync_ReturnsAllOrderedDescending()
    {
        var factory = CreateFactory(nameof(GetPriceHistoryAsync_ReturnsAllOrderedDescending));
        var now = DateTime.UtcNow;
        await SeedPricesAsync(factory,
            new ProductPrice { ProductId = 1, Price = 1m, ValidFrom = now.AddDays(-3) },
            new ProductPrice { ProductId = 1, Price = 2m, ValidFrom = now.AddDays(-1) },
            new ProductPrice { ProductId = 1, Price = 3m, ValidFrom = now.AddDays(-2) },
            new ProductPrice { ProductId = 2, Price = 99m, ValidFrom = now });
        var sut = CreateSut(factory);

        var history = await sut.GetPriceHistoryAsync(1);

        history.Should().HaveCount(3);
        history.Select(p => p.Price).Should().ContainInOrder(2m, 3m, 1m);
    }

    [Fact]
    public async Task HasPriceHistoryAsync_ReturnsTrueOnlyWhenEntriesExist()
    {
        var factory = CreateFactory(nameof(HasPriceHistoryAsync_ReturnsTrueOnlyWhenEntriesExist));
        await SeedPricesAsync(factory,
            new ProductPrice { ProductId = 1, Price = 1m, ValidFrom = DateTime.UtcNow });
        var sut = CreateSut(factory);

        (await sut.HasPriceHistoryAsync(1)).Should().BeTrue();
        (await sut.HasPriceHistoryAsync(999)).Should().BeFalse();
    }

    [Fact]
    public async Task GetPriceChangeCountAsync_CountsAllEntriesForProduct()
    {
        var factory = CreateFactory(nameof(GetPriceChangeCountAsync_CountsAllEntriesForProduct));
        var now = DateTime.UtcNow;
        await SeedPricesAsync(factory,
            new ProductPrice { ProductId = 1, Price = 1m, ValidFrom = now.AddDays(-3) },
            new ProductPrice { ProductId = 1, Price = 2m, ValidFrom = now.AddDays(-2) },
            new ProductPrice { ProductId = 2, Price = 9m, ValidFrom = now });
        var sut = CreateSut(factory);

        (await sut.GetPriceChangeCountAsync(1)).Should().Be(2);
        (await sut.GetPriceChangeCountAsync(2)).Should().Be(1);
        (await sut.GetPriceChangeCountAsync(999)).Should().Be(0);
    }

    [Fact]
    public async Task CreateInitialPriceAsync_PersistsOpenEndedEntry()
    {
        var factory = CreateFactory(nameof(CreateInitialPriceAsync_PersistsOpenEndedEntry));
        var sut = CreateSut(factory);

        await sut.CreateInitialPriceAsync(productId: 7, warehouseId: 1, price: 19.99m, currency: "EUR", createdBy: "tester");

        await using var db = factory.CreateDbContext();
        var stored = await db.ProductPrices.SingleAsync();
        stored.ProductId.Should().Be(7);
        stored.Price.Should().Be(19.99m);
        stored.ValidTo.Should().BeNull();
        stored.CreatedBy.Should().Be("tester");
        stored.Reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UpdatePriceAutomaticAsync_ClosesCurrentAndAddsNew()
    {
        var factory = CreateFactory(nameof(UpdatePriceAutomaticAsync_ClosesCurrentAndAddsNew));
        var sut = CreateSut(factory);

        await sut.CreateInitialPriceAsync(5, 1, 10m, "EUR", "sys");
        await sut.UpdatePriceAutomaticAsync(productId: 5, warehouseId: 1, oldPrice: 10m, newPrice: 15m, currency: "EUR", updatedBy: "u");

        await using var db = factory.CreateDbContext();
        var entries = await db.ProductPrices.Where(p => p.ProductId == 5).OrderBy(p => p.ValidFrom).ToListAsync();
        entries.Should().HaveCount(2);
        entries[0].ValidTo.Should().NotBeNull();
        entries[1].Price.Should().Be(15m);
        entries[1].ValidTo.Should().BeNull();
    }

    [Fact]
    public async Task GetPriceStatisticsAsync_ReturnsEmptyStatsWhenNoEntries()
    {
        var factory = CreateFactory(nameof(GetPriceStatisticsAsync_ReturnsEmptyStatsWhenNoEntries));
        var sut = CreateSut(factory);

        var stats = await sut.GetPriceStatisticsAsync(999);

        stats.Should().NotBeNull();
        stats.TotalChanges.Should().Be(0);
        stats.CurrentPrice.Should().BeNull();
    }

    [Fact]
    public async Task GetPriceStatisticsAsync_ComputesMinMaxAverageAndChange()
    {
        var factory = CreateFactory(nameof(GetPriceStatisticsAsync_ComputesMinMaxAverageAndChange));
        var now = DateTime.UtcNow;
        await SeedPricesAsync(factory,
            new ProductPrice { ProductId = 1, Price = 10m, ValidFrom = now.AddDays(-10), ValidTo = now.AddDays(-5) },
            new ProductPrice { ProductId = 1, Price = 20m, ValidFrom = now.AddDays(-4), ValidTo = null });
        var sut = CreateSut(factory);

        var stats = await sut.GetPriceStatisticsAsync(1);

        stats.TotalChanges.Should().Be(2);
        stats.MinPrice.Should().Be(10m);
        stats.MaxPrice.Should().Be(20m);
        stats.AveragePrice.Should().Be(15m);
        stats.CurrentPrice.Should().Be(20m);
        stats.PriceChange.Should().Be(10m);
    }
}
