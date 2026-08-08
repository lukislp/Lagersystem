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
        => CreateSut(factory, out _, out _);

    private static PriceHistoryService CreateSut(
        IDbContextFactory<InventoryDbContext> factory, out IAuthService auth, out IAuditService audit)
    {
        auth = Substitute.For<IAuthService>();
        auth.GetCurrentWarehouseId().Returns(1);
        audit = Substitute.For<IAuditService>();
        return new PriceHistoryService(factory, auth, audit, NullLogger<PriceHistoryService>.Instance);
    }

    private static async Task<int> SeedProductAsync(IDbContextFactory<InventoryDbContext> factory, decimal price = 10m, int warehouseId = 1)
    {
        await using var db = factory.CreateDbContext();
        var category = new Category { Name = "Cat", WarehouseId = warehouseId };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        var product = new Product { Name = "P", CategoryId = category.Id, WarehouseId = warehouseId, Price = price };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
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

    // --- AddPriceAsync ---

    [Fact]
    public async Task AddPriceAsync_ZeroOrNegativePrice_Throws()
    {
        var factory = CreateFactory(nameof(AddPriceAsync_ZeroOrNegativePrice_Throws));
        var sut = CreateSut(factory);

        var act = () => sut.AddPriceAsync(1, 0m, DateTime.UtcNow, null);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("price");
    }

    [Fact]
    public async Task AddPriceAsync_ValidToBeforeValidFrom_Throws()
    {
        var factory = CreateFactory(nameof(AddPriceAsync_ValidToBeforeValidFrom_Throws));
        var sut = CreateSut(factory);
        var now = DateTime.UtcNow;

        var act = () => sut.AddPriceAsync(1, 10m, now, now.AddDays(-1));

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("validTo");
    }

    [Fact]
    public async Task AddPriceAsync_UnknownProduct_Throws()
    {
        var factory = CreateFactory(nameof(AddPriceAsync_UnknownProduct_Throws));
        var sut = CreateSut(factory);

        var act = () => sut.AddPriceAsync(999, 10m, DateTime.UtcNow, null);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*999*");
    }

    [Fact]
    public async Task AddPriceAsync_PersistsPriceEntry_AndLogsAudit()
    {
        var factory = CreateFactory(nameof(AddPriceAsync_PersistsPriceEntry_AndLogsAudit));
        var productId = await SeedProductAsync(factory, price: 1m);
        var sut = CreateSut(factory, out var auth, out var audit);
        auth.GetCurrentUserAsync().Returns((User?)null);
        var now = DateTime.UtcNow;

        var result = await sut.AddPriceAsync(productId, 25m, now, null, reason: "restock", createdBy: "tester");

        result.Price.Should().Be(25m);
        result.CreatedBy.Should().Be("tester");
        result.WarehouseId.Should().Be(1);

        await using var db = factory.CreateDbContext();
        (await db.ProductPrices.SingleAsync(p => p.ProductId == productId)).Price.Should().Be(25m);

        await audit.Received(1).LogAsync(
            "PRODUCT_PRICE_CREATED", "ProductPrice", Arg.Any<int?>(), Arg.Any<object>(), AuditSeverity.Info, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddPriceAsync_FirstPriceForProduct_SyncsProductPrice()
    {
        var factory = CreateFactory(nameof(AddPriceAsync_FirstPriceForProduct_SyncsProductPrice));
        var productId = await SeedProductAsync(factory, price: 1m);
        var sut = CreateSut(factory);

        await sut.AddPriceAsync(productId, 25m, DateTime.UtcNow, null, createdBy: "tester");

        await using var db = factory.CreateDbContext();
        (await db.Products.FindAsync(productId))!.Price.Should().Be(25m);
    }

    [Fact]
    public async Task AddPriceAsync_SecondPriceForProduct_SyncsProductPriceToNewValue()
    {
        var factory = CreateFactory(nameof(AddPriceAsync_SecondPriceForProduct_SyncsProductPriceToNewValue));
        var productId = await SeedProductAsync(factory, price: 1m);
        var sut = CreateSut(factory);
        var now = DateTime.UtcNow;

        await sut.AddPriceAsync(productId, 25m, now.AddDays(-5), null, createdBy: "a");
        await sut.AddPriceAsync(productId, 30m, now, null, createdBy: "b");

        await using var db = factory.CreateDbContext();
        (await db.Products.FindAsync(productId))!.Price.Should().Be(30m);
    }

    [Fact]
    public async Task AddPriceAsync_ScheduledFuturePrice_DoesNotSyncProductPriceYet()
    {
        var factory = CreateFactory(nameof(AddPriceAsync_ScheduledFuturePrice_DoesNotSyncProductPriceYet));
        var productId = await SeedProductAsync(factory, price: 1m);
        var sut = CreateSut(factory);

        await sut.AddPriceAsync(productId, 99m, DateTime.UtcNow.AddDays(10), null, createdBy: "future");

        await using var db = factory.CreateDbContext();
        (await db.Products.FindAsync(productId))!.Price.Should().Be(1m);
    }

    [Fact]
    public async Task AddPriceAsync_NoCreatedBy_FallsBackToCurrentUserOrSystem()
    {
        var factory = CreateFactory(nameof(AddPriceAsync_NoCreatedBy_FallsBackToCurrentUserOrSystem));
        var productId = await SeedProductAsync(factory);
        var sut = CreateSut(factory, out var auth, out _);
        auth.GetCurrentUserAsync().Returns((User?)null);

        var result = await sut.AddPriceAsync(productId, 5m, DateTime.UtcNow, null);

        result.CreatedBy.Should().Be("System");
    }

    [Fact]
    public async Task AddPriceAsync_OverlappingOpenEndedEntry_ClosesOldEntry()
    {
        var factory = CreateFactory(nameof(AddPriceAsync_OverlappingOpenEndedEntry_ClosesOldEntry));
        var productId = await SeedProductAsync(factory);
        var sut = CreateSut(factory);
        var now = DateTime.UtcNow;

        await sut.AddPriceAsync(productId, 10m, now.AddDays(-5), null, createdBy: "a");
        await sut.AddPriceAsync(productId, 20m, now, null, createdBy: "b");

        await using var db = factory.CreateDbContext();
        var entries = await db.ProductPrices.Where(p => p.ProductId == productId).OrderBy(p => p.ValidFrom).ToListAsync();
        entries.Should().HaveCount(2);
        entries[0].ValidTo.Should().NotBeNull();
        entries[1].ValidTo.Should().BeNull();
    }

    [Fact]
    public async Task AddPriceAsync_OverlappingEntryStartingAfterNewValidFrom_IsRemoved()
    {
        var factory = CreateFactory(nameof(AddPriceAsync_OverlappingEntryStartingAfterNewValidFrom_IsRemoved));
        var productId = await SeedProductAsync(factory);
        var sut = CreateSut(factory);
        var now = DateTime.UtcNow;

        // A future-scheduled price...
        await sut.AddPriceAsync(productId, 30m, now.AddDays(10), null, createdBy: "future");
        // ...gets removed when a new entry's window overlaps its ValidFrom.
        await sut.AddPriceAsync(productId, 15m, now, now.AddDays(20), createdBy: "overrider");

        await using var db = factory.CreateDbContext();
        var entries = await db.ProductPrices.Where(p => p.ProductId == productId).ToListAsync();
        entries.Should().ContainSingle(e => e.Price == 15m);
        entries.Should().NotContain(e => e.Price == 30m);
    }

    // --- UpdateCurrentPriceAsync ---

    [Fact]
    public async Task UpdateCurrentPriceAsync_NoCurrentPrice_CreatesNewOpenEndedEntry()
    {
        var factory = CreateFactory(nameof(UpdateCurrentPriceAsync_NoCurrentPrice_CreatesNewOpenEndedEntry));
        var productId = await SeedProductAsync(factory);
        var sut = CreateSut(factory);

        var result = await sut.UpdateCurrentPriceAsync(productId, 42m, createdBy: "u");

        result.Price.Should().Be(42m);
        result.ValidTo.Should().BeNull();
        result.Notes.Should().Contain("N/A");
    }

    [Fact]
    public async Task UpdateCurrentPriceAsync_HasCurrentPrice_ClosesItAndAddsNew()
    {
        var factory = CreateFactory(nameof(UpdateCurrentPriceAsync_HasCurrentPrice_ClosesItAndAddsNew));
        var productId = await SeedProductAsync(factory);
        var sut = CreateSut(factory);

        await sut.CreateInitialPriceAsync(productId, 1, 10m, "EUR", "sys");
        var result = await sut.UpdateCurrentPriceAsync(productId, 15m, createdBy: "u");

        result.Price.Should().Be(15m);
        result.Notes.Should().Contain("15");

        await using var db = factory.CreateDbContext();
        var entries = await db.ProductPrices.Where(p => p.ProductId == productId).OrderBy(p => p.ValidFrom).ToListAsync();
        entries.Should().HaveCount(2);
        entries[0].ValidTo.Should().NotBeNull();
    }

    // --- ScheduleFuturePriceAsync ---

    [Fact]
    public async Task ScheduleFuturePriceAsync_ValidFromInPastOrNow_Throws()
    {
        var factory = CreateFactory(nameof(ScheduleFuturePriceAsync_ValidFromInPastOrNow_Throws));
        var sut = CreateSut(factory);

        var act = () => sut.ScheduleFuturePriceAsync(1, 10m, DateTime.UtcNow.AddDays(-1), null);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("validFrom");
    }

    [Fact]
    public async Task ScheduleFuturePriceAsync_FutureDate_PersistsEntry()
    {
        var factory = CreateFactory(nameof(ScheduleFuturePriceAsync_FutureDate_PersistsEntry));
        var productId = await SeedProductAsync(factory);
        var sut = CreateSut(factory);
        var futureDate = DateTime.UtcNow.AddDays(30);

        var result = await sut.ScheduleFuturePriceAsync(productId, 99m, futureDate, null, reason: "planned hike");

        result.Price.Should().Be(99m);
        result.ValidFrom.Should().BeCloseTo(futureDate, TimeSpan.FromSeconds(1));
        result.Notes.Should().Contain("Geplante");
    }

    // --- GetMonthlyStatisticsAsync ---

    [Fact]
    public async Task GetMonthlyStatisticsAsync_NoData_ReturnsHasDataFalse()
    {
        var factory = CreateFactory(nameof(GetMonthlyStatisticsAsync_NoData_ReturnsHasDataFalse));
        var sut = CreateSut(factory);

        var stats = await sut.GetMonthlyStatisticsAsync(999);

        stats.HasData.Should().BeFalse();
        stats.ChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task GetMonthlyStatisticsAsync_WithEntryThisMonth_ComputesChangeFromCurrentPrice()
    {
        var factory = CreateFactory(nameof(GetMonthlyStatisticsAsync_WithEntryThisMonth_ComputesChangeFromCurrentPrice));
        var productId = await SeedProductAsync(factory);
        var sut = CreateSut(factory);
        var now = DateTime.UtcNow;

        await sut.AddPriceAsync(productId, 50m, now.AddDays(-1), null, createdBy: "u");

        var stats = await sut.GetMonthlyStatisticsAsync(productId);

        stats.HasData.Should().BeTrue();
        stats.Month.Should().Be(now.Month);
        stats.Year.Should().Be(now.Year);
        stats.EndPrice.Should().Be(50m);
        stats.ChangesCount.Should().BeGreaterThanOrEqualTo(1);
    }

    // --- GetYearlyStatisticsAsync ---

    [Fact]
    public async Task GetYearlyStatisticsAsync_NoData_ReturnsHasDataFalse()
    {
        var factory = CreateFactory(nameof(GetYearlyStatisticsAsync_NoData_ReturnsHasDataFalse));
        var sut = CreateSut(factory);

        var stats = await sut.GetYearlyStatisticsAsync(999);

        stats.HasData.Should().BeFalse();
        stats.ChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task GetYearlyStatisticsAsync_WithEntriesThisYear_ComputesMinMaxAndChange()
    {
        var factory = CreateFactory(nameof(GetYearlyStatisticsAsync_WithEntriesThisYear_ComputesMinMaxAndChange));
        var productId = await SeedProductAsync(factory);
        var sut = CreateSut(factory);
        var now = DateTime.UtcNow;

        await sut.AddPriceAsync(productId, 10m, now.AddMonths(-2) > new DateTime(now.Year, 1, 1) ? now.AddMonths(-2) : now.AddDays(-10), null, createdBy: "u");
        await sut.UpdatePriceAutomaticAsync(productId, 1, 10m, 30m, "EUR", "u2");

        var stats = await sut.GetYearlyStatisticsAsync(productId);

        stats.HasData.Should().BeTrue();
        stats.Year.Should().Be(now.Year);
        stats.EndPrice.Should().Be(30m);
        stats.MaxPrice.Should().BeGreaterThanOrEqualTo(stats.MinPrice);
    }
}
