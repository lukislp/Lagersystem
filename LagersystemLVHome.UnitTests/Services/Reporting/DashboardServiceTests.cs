using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Reporting;

public class DashboardServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    /// <summary>
    /// A context factory that fails from the Nth call onwards, used to exercise the
    /// try/catch fallback paths that are otherwise unreachable with a healthy InMemory provider.
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

    /// <summary>
    /// Cache stub that always executes the factory delegate directly (no real caching),
    /// so tests observe fresh data on every call.
    /// </summary>
    private static ICacheService CreatePassthroughCache()
    {
        var cache = Substitute.For<ICacheService>();
        cache.GetOrCreateAsync<DashboardData>(
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Func<Task<DashboardData>>>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<DashboardData>>>()());
        return cache;
    }

    private static DashboardService Build(
        IDbContextFactory<InventoryDbContext> factory,
        ICacheService? cache = null,
        DashboardSettings? settings = null)
        => new(factory, cache ?? CreatePassthroughCache(), settings ?? new DashboardSettings(), NullLogger<DashboardService>.Instance);

    // ---- Entity builders -------------------------------------------------

    private static Warehouse MakeWarehouse(int id, string name = "WH") => new()
    {
        Id = id,
        Name = name,
        Code = $"W{id:000}",
        Address = "addr",
        IsActive = true
    };

    private static Category MakeCategory(int id, int warehouseId, string? name = null) => new()
    {
        Id = id,
        Name = name ?? $"Cat{id}",
        WarehouseId = warehouseId
    };

    private static Product MakeProduct(int id, int warehouseId, int categoryId, int quantity, int minQuantity, decimal price) => new()
    {
        Id = id,
        Name = $"Product{id}",
        WarehouseId = warehouseId,
        CategoryId = categoryId,
        Quantity = quantity,
        MinQuantity = minQuantity,
        Price = price
    };

    private static StorageLocation MakeLocation(int id, int warehouseId, int? maxCapacity = null) => new()
    {
        Id = id,
        Code = $"L{id}",
        Name = $"Location{id}",
        WarehouseId = warehouseId,
        MaxCapacity = maxCapacity
    };

    private static ProductStorageLocation MakeLink(int productId, int storageLocationId, int quantity) => new()
    {
        ProductId = productId,
        StorageLocationId = storageLocationId,
        Quantity = quantity
    };

    private static StockMovement MakeMovement(int productId, int warehouseId, int quantityChange, MovementType type, DateTime timestamp) => new()
    {
        ProductId = productId,
        WarehouseId = warehouseId,
        QuantityChange = quantityChange,
        Type = type,
        Timestamp = timestamp
    };

    private static ProductBatch MakeBatch(int productId, int warehouseId, int quantity, DateTime? expiryDate) => new()
    {
        ProductId = productId,
        WarehouseId = warehouseId,
        BatchNumber = $"B-{productId}-{Guid.NewGuid():N}",
        Quantity = quantity,
        ExpiryDate = expiryDate
    };

    private static ProductPrice MakePrice(int productId, int warehouseId, decimal price, DateTime validFrom) => new()
    {
        ProductId = productId,
        WarehouseId = warehouseId,
        Price = price,
        ValidFrom = validFrom
    };

    // ---- GetDashboardDataAsync(warehouseId, from, to, ct) -----------------

    [Fact]
    public async Task GetDashboardDataAsync_EmptyDatabase_ReturnsZeroedDefaults()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_EmptyDatabase_ReturnsZeroedDefaults));
        var sut = Build(factory);

        var data = await sut.GetDashboardDataAsync(warehouseId: null);

        data.TotalProducts.Should().Be(0);
        data.TotalStockValue.Should().Be(0);
        data.AverageProductValue.Should().Be(0, "AverageProductValue is only set when TotalProducts > 0");
        data.InventoryHealthScore.Should().Be(0, "no products means the health score short-circuits to 0");
        data.AbcAnalysis.TotalValue.Should().Be(0);
        data.ExpiryAnalytics.TotalAtRisk.Should().Be(0);
        data.StorageUtilization.TotalLocations.Should().Be(0);
        data.RecentMovements.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDashboardDataAsync_NoWarehouseFilter_AggregatesKpisAcrossWarehouses()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_NoWarehouseFilter_AggregatesKpisAcrossWarehouses));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.AddRange(MakeWarehouse(1, "WH1"), MakeWarehouse(2, "WH2"));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.AddRange(
                MakeProduct(1, 1, 1, quantity: 10, minQuantity: 5, price: 20), // healthy stock
                MakeProduct(2, 1, 1, quantity: 2, minQuantity: 5, price: 15),  // low stock
                MakeProduct(3, 2, 1, quantity: 0, minQuantity: 5, price: 30)); // out of stock
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var data = await sut.GetDashboardDataAsync(warehouseId: null);

        data.TotalProducts.Should().Be(3);
        data.TotalCategories.Should().Be(1);
        data.TotalWarehouses.Should().Be(2);
        data.TotalStockQuantity.Should().Be(12);
        data.LowStockCount.Should().Be(2, "products 2 and 3 have Quantity <= MinQuantity");
        // No ScanAdd movements exist -> FIFO falls back to Quantity * Price for every product.
        data.TotalStockValue.Should().Be(10 * 20 + 2 * 15 + 0 * 30);
        data.AverageProductValue.Should().Be(data.TotalStockValue / 3);
    }

    [Fact]
    public async Task GetDashboardDataAsync_WithWarehouseFilter_OnlyIncludesProductsInThatWarehouse()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_WithWarehouseFilter_OnlyIncludesProductsInThatWarehouse));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.AddRange(MakeWarehouse(1), MakeWarehouse(2));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.AddRange(
                MakeProduct(1, 1, 1, 5, 1, 10),
                MakeProduct(2, 2, 1, 5, 1, 10));
            db.StorageLocations.AddRange(MakeLocation(10, 1), MakeLocation(20, 2));
            db.ProductStorageLocations.AddRange(
                MakeLink(1, 10, 5),
                MakeLink(2, 20, 5));
            db.StockMovements.AddRange(
                MakeMovement(1, 1, 1, MovementType.ScanAdd, DateTime.UtcNow.AddDays(-1)),
                MakeMovement(2, 2, 1, MovementType.ScanAdd, DateTime.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var data = await sut.GetDashboardDataAsync(warehouseId: 1);

        data.TotalProducts.Should().Be(1);
        data.RecentMovements.Should().ContainSingle().Which.ProductId.Should().Be(1);
    }

    [Fact]
    public async Task GetDashboardDataAsync_RecentMovements_LimitedToTwentyMostRecent()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_RecentMovements_LimitedToTwentyMostRecent));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.Add(MakeProduct(1, 1, 1, 100, 1, 1));
            for (var i = 0; i < 25; i++)
            {
                db.StockMovements.Add(MakeMovement(1, 1, 1, MovementType.ManualAdd, DateTime.UtcNow.AddMinutes(-i)));
            }
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var data = await sut.GetDashboardDataAsync(warehouseId: null);

        data.RecentMovements.Should().HaveCount(20);
        data.RecentMovements.Should().BeInDescendingOrder(m => m.Timestamp);
    }

    [Fact]
    public async Task GetDashboardDataAsync_ContextFactoryThrows_ReturnsDefaultDashboardData()
    {
        var sut = Build(new ThrowingContextFactory());

        var data = await sut.GetDashboardDataAsync(warehouseId: null);

        data.Should().NotBeNull();
        data.TotalProducts.Should().Be(0);
    }

    // ---- GetDashboardDataAsync(ct) -- current-user overload ---------------

    [Fact]
    public async Task GetDashboardDataAsync_CtOverload_NoUsers_ReturnsDefaultDashboardData()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_CtOverload_NoUsers_ReturnsDefaultDashboardData));
        var sut = Build(factory);

        var data = await sut.GetDashboardDataAsync(CancellationToken.None);

        data.Should().NotBeNull();
        data.TotalProducts.Should().Be(0);
    }

    [Fact]
    public async Task GetDashboardDataAsync_CtOverload_UserWithWarehouse_FiltersByUsersWarehouse()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_CtOverload_UserWithWarehouse_FiltersByUsersWarehouse));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.AddRange(MakeWarehouse(1), MakeWarehouse(2));
            db.Categories.Add(MakeCategory(1, 1));
            db.Users.Add(new User { Id = 1, Username = "u1", Email = "u1@x.local", PasswordHash = "x", WarehouseId = 1 });
            db.Products.AddRange(
                MakeProduct(1, 1, 1, 5, 1, 10),
                MakeProduct(2, 2, 1, 5, 1, 10));
            db.StorageLocations.AddRange(MakeLocation(10, 1), MakeLocation(20, 2));
            db.ProductStorageLocations.AddRange(MakeLink(1, 10, 5), MakeLink(2, 20, 5));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var data = await sut.GetDashboardDataAsync(CancellationToken.None);

        data.TotalProducts.Should().Be(1, "only the product reachable via the user's warehouse should be counted");
    }

    [Fact]
    public async Task GetDashboardDataAsync_CtOverload_UserWithZeroWarehouseId_DoesNotFilter()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_CtOverload_UserWithZeroWarehouseId_DoesNotFilter));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.AddRange(MakeWarehouse(1), MakeWarehouse(2));
            db.Categories.Add(MakeCategory(1, 1));
            db.Users.Add(new User { Id = 1, Username = "u1", Email = "u1@x.local", PasswordHash = "x", WarehouseId = 0 });
            db.Products.AddRange(
                MakeProduct(1, 1, 1, 5, 1, 10),
                MakeProduct(2, 2, 1, 5, 1, 10));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var data = await sut.GetDashboardDataAsync(CancellationToken.None);

        data.TotalProducts.Should().Be(2, "WarehouseId 0 is falsy so no warehouse filter is applied");
    }

    // ---- Inventory health score (via public entry point) ------------------

    [Fact]
    public async Task GetDashboardDataAsync_HealthyBalancedStock_ScoreClampedAtHundred()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_HealthyBalancedStock_ScoreClampedAtHundred));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.Add(MakeProduct(1, 1, 1, quantity: 100, minQuantity: 5, price: 1));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var data = await sut.GetDashboardDataAsync(warehouseId: null);

        // lowStockRatio=0, outOfStockRatio=0, expiredRatio=0, balancedStock bonus=+10 -> clamped to 100.
        data.InventoryHealthScore.Should().Be(100);
    }

    [Fact]
    public async Task GetDashboardDataAsync_OutOfStockProduct_ReducesHealthScore()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_OutOfStockProduct_ReducesHealthScore));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.Add(MakeProduct(1, 1, 1, quantity: 0, minQuantity: 5, price: 1));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var data = await sut.GetDashboardDataAsync(warehouseId: null);

        // lowStockRatio=1 (-30), outOfStockRatio=1 (-40), no expired, no balanced bonus -> 100-30-40=30.
        data.InventoryHealthScore.Should().Be(30);
    }

    [Fact]
    public async Task GetDashboardDataAsync_ExpiredBatch_ReducesHealthScore()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_ExpiredBatch_ReducesHealthScore));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.Add(MakeProduct(1, 1, 1, quantity: 100, minQuantity: 5, price: 1));
            db.ProductBatches.Add(MakeBatch(1, 1, quantity: 1, expiryDate: DateTime.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var data = await sut.GetDashboardDataAsync(warehouseId: null);

        // Balanced-stock bonus (+10) offset by expired-batch penalty (expiredRatio=1 -> -20) -> 100-20+10=90.
        data.InventoryHealthScore.Should().Be(90);
    }

    // ---- Stock turnover rate -----------------------------------------------

    [Fact]
    public async Task GetDashboardDataAsync_StockTurnoverRate_ComputedFromLast30DaysOutbound()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_StockTurnoverRate_ComputedFromLast30DaysOutbound));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.Add(MakeProduct(1, 1, 1, quantity: 10, minQuantity: 1, price: 1));
            db.StockMovements.AddRange(
                MakeMovement(1, 1, -4, MovementType.ScanRemove, DateTime.UtcNow.AddDays(-2)), // in window
                MakeMovement(1, 1, -100, MovementType.ScanRemove, DateTime.UtcNow.AddDays(-40))); // outside window
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var data = await sut.GetDashboardDataAsync(warehouseId: null);

        // soldUnits=4, avgInventory=10 -> rate=0.4
        data.StockTurnoverRate.Should().Be(0.4);
    }

    [Fact]
    public async Task GetDashboardDataAsync_NoProducts_TurnoverRateIsZero()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_NoProducts_TurnoverRateIsZero));
        var sut = Build(factory);

        var data = await sut.GetDashboardDataAsync(warehouseId: null);

        data.StockTurnoverRate.Should().Be(0);
    }

    // ---- ABC analysis --------------------------------------------------------

    [Fact]
    public async Task GetDashboardDataAsync_AbcAnalysis_ClassifiesByCumulativeValuePercentage()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_AbcAnalysis_ClassifiesByCumulativeValuePercentage));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            // Values chosen so cumulative percentages land exactly on the 80% / 95% boundaries.
            db.Products.AddRange(
                MakeProduct(1, 1, 1, quantity: 1, minQuantity: 1, price: 800), // 80% cumulative -> class A
                MakeProduct(2, 1, 1, quantity: 1, minQuantity: 1, price: 150), // 95% cumulative -> class B
                MakeProduct(3, 1, 1, quantity: 1, minQuantity: 1, price: 50)); // 100% cumulative -> class C
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var data = await sut.GetDashboardDataAsync(warehouseId: null);

        data.AbcAnalysis.TotalValue.Should().Be(1000);
        data.AbcAnalysis.ClassACount.Should().Be(1);
        data.AbcAnalysis.ClassAValue.Should().Be(800);
        data.AbcAnalysis.ClassBCount.Should().Be(1);
        data.AbcAnalysis.ClassBValue.Should().Be(150);
        data.AbcAnalysis.ClassCCount.Should().Be(1);
        data.AbcAnalysis.ClassCValue.Should().Be(50);
    }

    [Fact]
    public async Task GetDashboardDataAsync_AbcAnalysis_TotalValueZero_AllProductsClassC()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_AbcAnalysis_TotalValueZero_AllProductsClassC));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.AddRange(
                MakeProduct(1, 1, 1, quantity: 0, minQuantity: 1, price: 10),
                MakeProduct(2, 1, 1, quantity: 0, minQuantity: 1, price: 20));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var data = await sut.GetDashboardDataAsync(warehouseId: null);

        data.AbcAnalysis.TotalValue.Should().Be(0);
        data.AbcAnalysis.ClassCCount.Should().Be(2);
        data.AbcAnalysis.ClassACount.Should().Be(0);
        data.AbcAnalysis.ClassBCount.Should().Be(0);
    }

    // ---- Expiry analytics ------------------------------------------------------

    [Fact]
    public async Task GetDashboardDataAsync_ExpiryAnalytics_BucketsBatchesByExpiryWindow()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_ExpiryAnalytics_BucketsBatchesByExpiryWindow));
        var today = DateTime.UtcNow.Date;
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.Add(MakeProduct(1, 1, 1, quantity: 100, minQuantity: 1, price: 10));
            db.ProductBatches.AddRange(
                MakeBatch(1, 1, quantity: 1, expiryDate: today.AddDays(-1)),  // expired
                MakeBatch(1, 1, quantity: 2, expiryDate: today.AddDays(3)),   // expiring soon (<=7)
                MakeBatch(1, 1, quantity: 3, expiryDate: today.AddDays(20)),  // expiring this month (8-30)
                MakeBatch(1, 1, quantity: 4, expiryDate: today.AddDays(60))); // beyond 30 days -> ignored
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var data = await sut.GetDashboardDataAsync(warehouseId: null);

        data.ExpiryAnalytics.ExpiredCount.Should().Be(1);
        data.ExpiryAnalytics.ExpiredValue.Should().Be(1 * 10);
        data.ExpiryAnalytics.ExpiringSoonCount.Should().Be(1);
        data.ExpiryAnalytics.ExpiringSoonValue.Should().Be(2 * 10);
        data.ExpiryAnalytics.ExpiringThisMonthCount.Should().Be(1);
        data.ExpiryAnalytics.ExpiringThisMonthValue.Should().Be(3 * 10);
        data.ExpiryAnalytics.TotalAtRisk.Should().Be(3, "the 60-day-out batch is beyond all three buckets");
    }

    // ---- Storage utilization -----------------------------------------------

    [Fact]
    public async Task GetDashboardDataAsync_StorageUtilization_NoLocationsWithCapacity_ReturnsBasicCounts()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_StorageUtilization_NoLocationsWithCapacity_ReturnsBasicCounts));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.StorageLocations.Add(MakeLocation(1, 1, maxCapacity: null));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var data = await sut.GetDashboardDataAsync(warehouseId: null);

        data.StorageUtilization.TotalLocations.Should().Be(1);
        data.StorageUtilization.OccupiedLocations.Should().Be(0);
        data.StorageUtilization.LocationsWithCapacity.Should().Be(0);
        data.StorageUtilization.AverageUtilization.Should().Be(0);
    }

    [Fact]
    public async Task GetDashboardDataAsync_StorageUtilization_ComputesOccupancyAndFullLocations()
    {
        var factory = CreateFactory(nameof(GetDashboardDataAsync_StorageUtilization_ComputesOccupancyAndFullLocations));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.Add(MakeProduct(1, 1, 1, quantity: 100, minQuantity: 1, price: 1));
            db.StorageLocations.AddRange(
                MakeLocation(1, 1, maxCapacity: 100), // 12% utilized
                MakeLocation(2, 1, maxCapacity: 10),  // 90% utilized -> "full"
                MakeLocation(3, 1, maxCapacity: 0),   // zero-capacity guard -> utilization 0, not a div/0
                MakeLocation(4, 1, maxCapacity: null)); // excluded from utilization stats entirely
            db.ProductStorageLocations.AddRange(
                MakeLink(1, 1, 12),
                MakeLink(1, 2, 9));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var data = await sut.GetDashboardDataAsync(warehouseId: null);

        data.StorageUtilization.TotalLocations.Should().Be(4);
        data.StorageUtilization.OccupiedLocations.Should().Be(2);
        data.StorageUtilization.EmptyLocations.Should().Be(2);
        data.StorageUtilization.LocationsWithCapacity.Should().Be(3);
        data.StorageUtilization.FullLocations.Should().Be(1);
        data.StorageUtilization.AverageUtilization.Should().BeApproximately((12 + 90 + 0) / 3.0, 0.001);
    }

    // ---- GetStockTrendsAsync ------------------------------------------------

    [Fact]
    public async Task GetStockTrendsAsync_ReturnsOneEntryPerDayWithInOutTotals()
    {
        var factory = CreateFactory(nameof(GetStockTrendsAsync_ReturnsOneEntryPerDayWithInOutTotals));
        var today = DateTime.UtcNow.Date;
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.Add(MakeProduct(1, 1, 1, quantity: 10, minQuantity: 1, price: 5));
            db.StockMovements.AddRange(
                MakeMovement(1, 1, 10, MovementType.ScanAdd, today.AddDays(-1)),
                MakeMovement(1, 1, -4, MovementType.ScanRemove, today.AddDays(-1)),
                MakeMovement(1, 1, 3, MovementType.ScanAdd, today.AddDays(-10))); // outside 3-day window
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var trends = await sut.GetStockTrendsAsync(days: 3);

        trends.Should().HaveCount(3);
        var dayWithMovements = trends.Single(t => t.Date == today.AddDays(-1));
        dayWithMovements.StockIn.Should().Be(10);
        dayWithMovements.StockOut.Should().Be(4);
        dayWithMovements.TotalStock.Should().Be(6);
        dayWithMovements.Value.Should().Be((10 + 4) * 5);
        trends.Where(t => t.Date != today.AddDays(-1)).Should().OnlyContain(t => t.StockIn == 0 && t.StockOut == 0);
    }

    [Fact]
    public async Task GetStockTrendsAsync_ContextFactoryThrows_ReturnsEmptyList()
    {
        var sut = Build(new ThrowingContextFactory());

        var trends = await sut.GetStockTrendsAsync(days: 5);

        trends.Should().BeEmpty();
    }

    // ---- GetTopMoversAsync ----------------------------------------------------

    [Fact]
    public async Task GetTopMoversAsync_OrdersByMovementCountDescendingAndRespectsWindow()
    {
        // Note: Product.CategoryId is a required (non-nullable) FK with OnDelete(Restrict), so a
        // Product can never legitimately reference a missing Category in production. The
        // EF Core InMemory provider also silently drops rows with an unmatched required FK when
        // using .Include(), so the "Ohne Kategorie" fallback branch cannot be exercised here and
        // is effectively unreachable/defensive-only code; both products use a real category instead.
        var factory = CreateFactory(nameof(GetTopMoversAsync_OrdersByMovementCountDescendingAndRespectsWindow));
        var recent = DateTime.UtcNow.AddDays(-1);
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1, "CatA"));
            db.Products.AddRange(
                MakeProduct(1, 1, 1, 10, 1, 2),
                MakeProduct(2, 1, 1, 10, 1, 3));
            db.StockMovements.AddRange(
                MakeMovement(1, 1, 1, MovementType.ManualAdd, recent),
                MakeMovement(1, 1, 1, MovementType.ManualAdd, recent),
                MakeMovement(1, 1, 1, MovementType.ManualAdd, recent),
                MakeMovement(2, 1, 1, MovementType.ManualAdd, recent),
                MakeMovement(2, 1, -1, MovementType.ManualRemove, DateTime.UtcNow.AddDays(-40))); // outside window
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var topMovers = await sut.GetTopMoversAsync(count: 10);

        topMovers.Should().HaveCount(2);
        topMovers[0].ProductId.Should().Be(1);
        topMovers[0].MovementCount.Should().Be(3);
        topMovers[0].CategoryName.Should().Be("CatA");
        topMovers[1].ProductId.Should().Be(2);
        topMovers[1].MovementCount.Should().Be(1, "the movement 40 days ago is outside the 30-day window");
    }

    [Fact]
    public async Task GetTopMoversAsync_RespectsCountLimit()
    {
        var factory = CreateFactory(nameof(GetTopMoversAsync_RespectsCountLimit));
        var recent = DateTime.UtcNow.AddDays(-1);
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.AddRange(
                MakeProduct(1, 1, 1, 10, 1, 1),
                MakeProduct(2, 1, 1, 10, 1, 1),
                MakeProduct(3, 1, 1, 10, 1, 1));
            db.StockMovements.AddRange(
                MakeMovement(1, 1, 1, MovementType.ManualAdd, recent),
                MakeMovement(2, 1, 1, MovementType.ManualAdd, recent),
                MakeMovement(3, 1, 1, MovementType.ManualAdd, recent));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var topMovers = await sut.GetTopMoversAsync(count: 2);

        topMovers.Should().HaveCount(2);
    }

    // ---- GetCategoryValuesAsync -------------------------------------------------

    [Fact]
    public async Task GetCategoryValuesAsync_GroupsByCategoryAndSortsDescendingByValue()
    {
        // Note: see the comment on GetTopMoversAsync_OrdersByMovementCountDescendingAndRespectsWindow
        // regarding why the "Ohne Kategorie" fallback (missing category) branch cannot be exercised
        // through the InMemory provider: Product.CategoryId is a required FK and Include() silently
        // drops rows whose FK doesn't resolve, which does not reflect real (FK-enforced) production data.
        var factory = CreateFactory(nameof(GetCategoryValuesAsync_GroupsByCategoryAndSortsDescendingByValue));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.AddRange(MakeCategory(1, 1, "CatA"), MakeCategory(2, 1, "CatB"));
            db.Products.AddRange(
                MakeProduct(1, 1, 1, quantity: 2, minQuantity: 1, price: 10),
                MakeProduct(2, 1, 1, quantity: 3, minQuantity: 1, price: 10),
                MakeProduct(3, 1, 2, quantity: 1, minQuantity: 1, price: 100));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var categories = await sut.GetCategoryValuesAsync();

        categories.Should().HaveCount(2);
        var catA = categories.Single(c => c.CategoryName == "CatA");
        catA.ProductCount.Should().Be(2);
        catA.TotalQuantity.Should().Be(5);
        catA.TotalValue.Should().Be(2 * 10 + 3 * 10);

        var catB = categories.Single(c => c.CategoryName == "CatB");
        catB.TotalValue.Should().Be(100);

        // Sorted descending by value: CatB (100) before CatA (50).
        categories[0].CategoryName.Should().Be("CatB");
    }

    [Fact]
    public async Task GetCategoryValuesAsync_ContextFactoryThrows_ReturnsEmptyList()
    {
        var sut = Build(new ThrowingContextFactory());

        var categories = await sut.GetCategoryValuesAsync();

        categories.Should().BeEmpty();
    }

    // ---- GetWarehouseDistributionAsync -------------------------------------------

    [Fact]
    public async Task GetWarehouseDistributionAsync_ComputesPerWarehouseCountsAndFifoValue()
    {
        var factory = CreateFactory(nameof(GetWarehouseDistributionAsync_ComputesPerWarehouseCountsAndFifoValue));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1, "Main"));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.AddRange(
                MakeProduct(1, 1, 1, quantity: 2, minQuantity: 1, price: 10),
                MakeProduct(2, 1, 1, quantity: 1, minQuantity: 1, price: 5));
            db.StorageLocations.AddRange(MakeLocation(1, 1), MakeLocation(2, 1));
            db.ProductStorageLocations.AddRange(
                MakeLink(1, 1, 2),
                MakeLink(2, 2, 1));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var distribution = await sut.GetWarehouseDistributionAsync();

        distribution.Should().ContainSingle();
        var wh = distribution[0];
        wh.WarehouseName.Should().Be("Main");
        wh.StorageLocationCount.Should().Be(2);
        wh.ProductCount.Should().Be(2);
        wh.TotalValue.Should().Be(2 * 10 + 1 * 5);
    }

    [Fact]
    public async Task GetWarehouseDistributionAsync_ContextFactoryThrows_ReturnsEmptyList()
    {
        var sut = Build(new ThrowingContextFactory());

        var distribution = await sut.GetWarehouseDistributionAsync();

        distribution.Should().BeEmpty();
    }

    // ---- FIFO chronological ordering (CalculateProductFIFOValueAsync internals) ---

    [Fact]
    public async Task GetCategoryValuesAsync_FifoValue_UsesChronologicalPriceHistory()
    {
        var factory = CreateFactory(nameof(GetCategoryValuesAsync_FifoValue_UsesChronologicalPriceHistory));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            // Quantity=15: fully consumed by two ScanAdd batches (2 + 10) plus 3 units with no matching
            // stock-in, which must fall back to the oldest ProductPrice entry.
            db.Products.Add(MakeProduct(1, 1, 1, quantity: 15, minQuantity: 1, price: 999));
            db.ProductPrices.AddRange(
                MakePrice(1, 1, price: 10, validFrom: now.AddDays(-30)),
                MakePrice(1, 1, price: 12, validFrom: now.AddDays(-5)));
            db.StockMovements.AddRange(
                // Before any ProductPrice.ValidFrom -> GetPriceAtTimestampAsync falls back to the initial price (10).
                MakeMovement(1, 1, 2, MovementType.ScanAdd, now.AddDays(-40)),
                // Only the -30d price entry qualifies here (10).
                MakeMovement(1, 1, 10, MovementType.ScanAdd, now.AddDays(-20)),
                // Both entries qualify; the most recent one (-5d, price 12) wins.
                MakeMovement(1, 1, 3, MovementType.ScanAdd, now.AddDays(-3)),
                // Not a ScanAdd -> ignored by the FIFO calculation entirely.
                MakeMovement(1, 1, 1000, MovementType.ManualAdd, now.AddDays(-1)));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var categories = await sut.GetCategoryValuesAsync();

        // 2*10 (fallback) + 10*10 (exact match) + 3*12 (most recent match) = 156
        categories.Single().TotalValue.Should().Be(156);
    }
}
