using LagersystemLVHome.Data;
using LagersystemLVHome.Data.Repositories;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;
using System.Security.Claims;

namespace LagersystemLVHome.UnitTests.Services.Inventory;

public class InventoryServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static IHttpContextAccessor AnonymousAccessor()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        return accessor;
    }

    /// <summary>Authenticated user. Pass a raw string for the WarehouseId claim so
    /// unparsable-claim scenarios can be exercised too.</summary>
    private static IHttpContextAccessor AuthenticatedAccessor(string? warehouseIdClaim = null, string? userName = null)
    {
        var claims = new List<Claim>();
        if (warehouseIdClaim != null) claims.Add(new Claim("WarehouseId", warehouseIdClaim));
        if (userName != null) claims.Add(new Claim(ClaimTypes.Name, userName));
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);
        return accessor;
    }

    private sealed class Fixture
    {
        public required InventoryService Sut { get; init; }
        public required IProductRepository ProductRepo { get; init; }
        public required ICategoryRepository CategoryRepo { get; init; }
        public required IStockMovementRepository MovementRepo { get; init; }
        public required IDbContextFactory<InventoryDbContext> ContextFactory { get; init; }
        public required IPriceHistoryService PriceHistoryService { get; init; }
        public required IAuditService AuditService { get; init; }
    }

    private static Fixture CreateSut(string dbName, IHttpContextAccessor? accessor = null)
    {
        var productRepo = Substitute.For<IProductRepository>();
        var categoryRepo = Substitute.For<ICategoryRepository>();
        var movementRepo = Substitute.For<IStockMovementRepository>();
        var contextFactory = CreateFactory(dbName);
        var priceHistoryService = Substitute.For<IPriceHistoryService>();
        var auditService = Substitute.For<IAuditService>();

        var sut = new InventoryService(
            productRepo, categoryRepo, movementRepo, accessor ?? AnonymousAccessor(),
            contextFactory, priceHistoryService, auditService,
            NullLogger<InventoryService>.Instance);

        return new Fixture
        {
            Sut = sut,
            ProductRepo = productRepo,
            CategoryRepo = categoryRepo,
            MovementRepo = movementRepo,
            ContextFactory = contextFactory,
            PriceHistoryService = priceHistoryService,
            AuditService = auditService
        };
    }

    private static Product MakeProduct(int id, int warehouseId = 1, decimal price = 10m, int quantity = 5, string name = "P")
        => new() { Id = id, Name = name, WarehouseId = warehouseId, Price = price, Quantity = quantity };

    // ---- GetWarehouseId (exercised via the public read methods) ----

    [Fact]
    public async Task GetAllProductsAsync_Unauthenticated_UsesDefaultWarehouse()
    {
        var f = CreateSut(nameof(GetAllProductsAsync_Unauthenticated_UsesDefaultWarehouse));

        await f.Sut.GetAllProductsAsync();

        await f.ProductRepo.Received(1).GetAllAsync(1);
    }

    [Fact]
    public async Task GetAllProductsAsync_AuthenticatedWithClaim_UsesClaimWarehouse()
    {
        var f = CreateSut(
            nameof(GetAllProductsAsync_AuthenticatedWithClaim_UsesClaimWarehouse),
            AuthenticatedAccessor(warehouseIdClaim: "5"));

        await f.Sut.GetAllProductsAsync();

        await f.ProductRepo.Received(1).GetAllAsync(5);
    }

    [Fact]
    public async Task GetAllProductsAsync_AuthenticatedWithoutClaim_FallsBackToDefault()
    {
        var f = CreateSut(
            nameof(GetAllProductsAsync_AuthenticatedWithoutClaim_FallsBackToDefault),
            AuthenticatedAccessor());

        await f.Sut.GetAllProductsAsync();

        await f.ProductRepo.Received(1).GetAllAsync(1);
    }

    [Fact]
    public async Task GetAllProductsAsync_UnparsableClaim_FallsBackToDefault()
    {
        var f = CreateSut(
            nameof(GetAllProductsAsync_UnparsableClaim_FallsBackToDefault),
            AuthenticatedAccessor(warehouseIdClaim: "not-a-number"));

        await f.Sut.GetAllProductsAsync();

        await f.ProductRepo.Received(1).GetAllAsync(1);
    }

    [Fact]
    public async Task GetProductByIdAsync_DelegatesToRepository()
    {
        var f = CreateSut(nameof(GetProductByIdAsync_DelegatesToRepository));
        var product = MakeProduct(1);
        f.ProductRepo.GetByIdAsync(1, 1).Returns(product);

        (await f.Sut.GetProductByIdAsync(1)).Should().BeSameAs(product);
    }

    [Fact]
    public async Task GetProductByBarcodeAsync_DelegatesToRepository()
    {
        var f = CreateSut(nameof(GetProductByBarcodeAsync_DelegatesToRepository));
        var product = MakeProduct(1);
        f.ProductRepo.GetByBarcodeAsync("ABC", 1).Returns(product);

        (await f.Sut.GetProductByBarcodeAsync("ABC")).Should().BeSameAs(product);
    }

    [Fact]
    public async Task GetProductsByCategoryAsync_DelegatesToRepository()
    {
        var f = CreateSut(nameof(GetProductsByCategoryAsync_DelegatesToRepository));
        f.ProductRepo.GetByCategoryAsync(3, 1).Returns(new[] { MakeProduct(1) });

        (await f.Sut.GetProductsByCategoryAsync(3)).Should().ContainSingle();
        await f.ProductRepo.Received(1).GetByCategoryAsync(3, 1);
    }

    [Fact]
    public async Task GetLowStockProductsAsync_DelegatesToRepository()
    {
        var f = CreateSut(nameof(GetLowStockProductsAsync_DelegatesToRepository));
        f.ProductRepo.GetLowStockAsync(1).Returns(new[] { MakeProduct(1) });

        (await f.Sut.GetLowStockProductsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task SearchProductsAsync_DelegatesToRepository()
    {
        var f = CreateSut(nameof(SearchProductsAsync_DelegatesToRepository));
        f.ProductRepo.SearchAsync("term", 1).Returns(new[] { MakeProduct(1) });

        (await f.Sut.SearchProductsAsync("term")).Should().ContainSingle();
    }

    [Fact]
    public async Task GetAllCategoriesAsync_DelegatesToRepository()
    {
        var f = CreateSut(nameof(GetAllCategoriesAsync_DelegatesToRepository));
        f.CategoryRepo.GetAllAsync(1).Returns(new[] { new Category { Id = 1, Name = "C" } });

        (await f.Sut.GetAllCategoriesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task GetActiveCategoriesAsync_DelegatesToRepository()
    {
        var f = CreateSut(nameof(GetActiveCategoriesAsync_DelegatesToRepository));
        f.CategoryRepo.GetActiveAsync(1).Returns(new[] { new Category { Id = 1, Name = "C" } });

        (await f.Sut.GetActiveCategoriesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task GetCategoryByIdAsync_DelegatesToRepository()
    {
        var f = CreateSut(nameof(GetCategoryByIdAsync_DelegatesToRepository));
        var category = new Category { Id = 1, Name = "C" };
        f.CategoryRepo.GetByIdAsync(1, 1).Returns(category);

        (await f.Sut.GetCategoryByIdAsync(1)).Should().BeSameAs(category);
    }

    // ---- Category CRUD ----

    [Fact]
    public async Task CreateCategoryAsync_SetsWarehouseId_AndLogsAudit()
    {
        var f = CreateSut(nameof(CreateCategoryAsync_SetsWarehouseId_AndLogsAudit), AuthenticatedAccessor("7"));
        var category = new Category { Name = "New" };
        f.CategoryRepo.CreateAsync(Arg.Do<Category>(c => c.Id = 42)).Returns(ci => ci.Arg<Category>());

        var created = await f.Sut.CreateCategoryAsync(category);

        created.WarehouseId.Should().Be(7);
        await f.AuditService.Received(1).LogCategoryCreatedAsync(42, "New");
    }

    [Fact]
    public async Task UpdateCategoryAsync_LogsAudit()
    {
        var f = CreateSut(nameof(UpdateCategoryAsync_LogsAudit));
        var category = new Category { Id = 1, Name = "Updated" };
        f.CategoryRepo.UpdateAsync(category).Returns(category);

        var updated = await f.Sut.UpdateCategoryAsync(category);

        updated.Should().BeSameAs(category);
        await f.AuditService.Received(1).LogCategoryUpdatedAsync(1, "Updated");
    }

    [Fact]
    public async Task DeleteCategoryAsync_KnownCategory_LogsWithName()
    {
        var f = CreateSut(nameof(DeleteCategoryAsync_KnownCategory_LogsWithName));
        f.CategoryRepo.GetByIdAsync(1, 1).Returns(new Category { Id = 1, Name = "Gone" });

        await f.Sut.DeleteCategoryAsync(1);

        await f.CategoryRepo.Received(1).DeleteAsync(1);
        await f.AuditService.Received(1).LogCategoryDeletedAsync(1, "Gone");
    }

    [Fact]
    public async Task DeleteCategoryAsync_UnknownCategory_LogsWithFallbackName()
    {
        var f = CreateSut(nameof(DeleteCategoryAsync_UnknownCategory_LogsWithFallbackName));
        f.CategoryRepo.GetByIdAsync(99, 1).Returns((Category?)null);

        await f.Sut.DeleteCategoryAsync(99);

        await f.AuditService.Received(1).LogCategoryDeletedAsync(99, "Category#99");
    }

    // ---- Product CRUD ----

    [Fact]
    public async Task CreateProductAsync_HappyPath_CreatesInitialPriceAndAudits()
    {
        var f = CreateSut(nameof(CreateProductAsync_HappyPath_CreatesInitialPriceAndAudits), AuthenticatedAccessor("3", "alice"));
        var product = new Product { Name = "Widget", Price = 9.99m };
        f.ProductRepo.CreateAsync(Arg.Do<Product>(p => p.Id = 10)).Returns(ci => ci.Arg<Product>());

        var created = await f.Sut.CreateProductAsync(product);

        created.WarehouseId.Should().Be(3);
        await f.PriceHistoryService.Received(1).CreateInitialPriceAsync(10, 3, 9.99m, "EUR", "alice");
        await f.AuditService.Received(1).LogProductCreatedAsync(10, "Widget");
    }

    [Fact]
    public async Task CreateProductAsync_Unauthenticated_UsesSystemAsCreatedBy()
    {
        var f = CreateSut(nameof(CreateProductAsync_Unauthenticated_UsesSystemAsCreatedBy));
        var product = new Product { Name = "Widget", Price = 5m };
        f.ProductRepo.CreateAsync(Arg.Any<Product>()).Returns(ci => ci.Arg<Product>());

        await f.Sut.CreateProductAsync(product);

        await f.PriceHistoryService.Received(1)
            .CreateInitialPriceAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<decimal>(), "EUR", "System");
    }

    [Fact]
    public async Task CreateProductAsync_PriceHistoryThrows_IsSwallowed_ProductStillCreatedAndAudited()
    {
        var f = CreateSut(nameof(CreateProductAsync_PriceHistoryThrows_IsSwallowed_ProductStillCreatedAndAudited));
        var product = new Product { Name = "Widget", Price = 5m };
        f.ProductRepo.CreateAsync(Arg.Any<Product>()).Returns(ci => ci.Arg<Product>());
        f.PriceHistoryService.CreateInitialPriceAsync(default, default, default, default!, default)
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));

        var act = async () => await f.Sut.CreateProductAsync(product);

        await act.Should().NotThrowAsync();
        await f.AuditService.Received(1).LogProductCreatedAsync(Arg.Any<int>(), "Widget");
    }

    [Fact]
    public async Task UpdateProductAsync_UnknownProduct_Throws()
    {
        var f = CreateSut(nameof(UpdateProductAsync_UnknownProduct_Throws));
        f.ProductRepo.GetByIdAsync(1, 1).Returns((Product?)null);

        var act = async () => await f.Sut.UpdateProductAsync(new Product { Id = 1 });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateProductAsync_PriceChanged_UpdatesPriceHistoryAndAudits()
    {
        var f = CreateSut(nameof(UpdateProductAsync_PriceChanged_UpdatesPriceHistoryAndAudits), AuthenticatedAccessor("1", "bob"));
        var existing = MakeProduct(1, price: 10m);
        var updatedInput = MakeProduct(1, price: 15m, name: "Widget");
        f.ProductRepo.GetByIdAsync(1, 1).Returns(existing);
        f.ProductRepo.UpdateAsync(updatedInput).Returns(updatedInput);

        var result = await f.Sut.UpdateProductAsync(updatedInput);

        result.Should().BeSameAs(updatedInput);
        await f.PriceHistoryService.Received(1).UpdatePriceAutomaticAsync(1, updatedInput.WarehouseId, 10m, 15m, "EUR", "bob");
        await f.AuditService.Received(1).LogProductUpdatedAsync(1, "Widget", Arg.Any<object>());
    }

    [Fact]
    public async Task UpdateProductAsync_PriceUnchanged_SkipsPriceHistory()
    {
        var f = CreateSut(nameof(UpdateProductAsync_PriceUnchanged_SkipsPriceHistory));
        var existing = MakeProduct(1, price: 10m);
        var updatedInput = MakeProduct(1, price: 10m);
        f.ProductRepo.GetByIdAsync(1, 1).Returns(existing);
        f.ProductRepo.UpdateAsync(updatedInput).Returns(updatedInput);

        await f.Sut.UpdateProductAsync(updatedInput);

        await f.PriceHistoryService.DidNotReceiveWithAnyArgs()
            .UpdatePriceAutomaticAsync(default, default, default, default, default!, default);
    }

    [Fact]
    public async Task UpdateProductAsync_PriceHistoryThrows_IsSwallowed()
    {
        var f = CreateSut(nameof(UpdateProductAsync_PriceHistoryThrows_IsSwallowed));
        var existing = MakeProduct(1, price: 10m);
        var updatedInput = MakeProduct(1, price: 20m);
        f.ProductRepo.GetByIdAsync(1, 1).Returns(existing);
        f.ProductRepo.UpdateAsync(updatedInput).Returns(updatedInput);
        f.PriceHistoryService.UpdatePriceAutomaticAsync(default, default, default, default, default!, default)
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));

        var act = async () => await f.Sut.UpdateProductAsync(updatedInput);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteProductAsync_KnownProduct_LogsWithName()
    {
        var f = CreateSut(nameof(DeleteProductAsync_KnownProduct_LogsWithName));
        f.ProductRepo.GetByIdAsync(1, 1).Returns(MakeProduct(1, name: "Gone"));

        await f.Sut.DeleteProductAsync(1);

        await f.ProductRepo.Received(1).DeleteAsync(1);
        await f.AuditService.Received(1).LogProductDeletedAsync(1, "Gone");
    }

    [Fact]
    public async Task DeleteProductAsync_UnknownProduct_LogsWithFallbackName()
    {
        var f = CreateSut(nameof(DeleteProductAsync_UnknownProduct_LogsWithFallbackName));
        f.ProductRepo.GetByIdAsync(99, 1).Returns((Product?)null);

        await f.Sut.DeleteProductAsync(99);

        await f.AuditService.Received(1).LogProductDeletedAsync(99, "Product#99");
    }

    // ---- Stock scan operations (exercise the private storage-location/batch helpers) ----

    [Fact]
    public async Task AddStockByScanAsync_UnknownBarcode_ReturnsNull()
    {
        var f = CreateSut(nameof(AddStockByScanAsync_UnknownBarcode_ReturnsNull));
        f.ProductRepo.GetByBarcodeAsync("X", 1).Returns((Product?)null);

        (await f.Sut.AddStockByScanAsync("X")).Should().BeNull();
    }

    [Fact]
    public async Task AddStockByScanAsync_NoStorageLocations_StillUpdatesQuantityAndCreatesMovement()
    {
        var f = CreateSut(nameof(AddStockByScanAsync_NoStorageLocations_StillUpdatesQuantityAndCreatesMovement));
        var product = MakeProduct(1, quantity: 5);
        f.ProductRepo.GetByBarcodeAsync("X", 1).Returns(product);

        var result = await f.Sut.AddStockByScanAsync("X", 3, "note");

        result!.Quantity.Should().Be(8);
        await f.ProductRepo.Received(1).UpdateAsync(Arg.Is<Product>(p => p.Quantity == 8));
        await f.MovementRepo.Received(1).CreateAsync(Arg.Is<StockMovement>(
            m => m.QuantityChange == 3 && m.Type == MovementType.ScanAdd && m.ScannedBarcode == "X" && m.Notes == "note"));
    }

    [Fact]
    public async Task AddStockByScanAsync_DistributesAcrossExistingStorageLocations()
    {
        var f = CreateSut(nameof(AddStockByScanAsync_DistributesAcrossExistingStorageLocations));
        var product = MakeProduct(1, quantity: 5);
        f.ProductRepo.GetByBarcodeAsync("X", 1).Returns(product);
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.ProductStorageLocations.AddRange(
                new ProductStorageLocation { ProductId = 1, StorageLocationId = 1, Quantity = 10 },
                new ProductStorageLocation { ProductId = 1, StorageLocationId = 2, Quantity = 5 },
                new ProductStorageLocation { ProductId = 1, StorageLocationId = 3, Quantity = 1 });
            await db.SaveChangesAsync();
        }

        await f.Sut.AddStockByScanAsync("X", 7);

        await using var verify = f.ContextFactory.CreateDbContext();
        var locations = await verify.ProductStorageLocations.OrderBy(l => l.StorageLocationId).ToListAsync();
        // Ordered ascending by quantity for distribution: loc3(1) gets +3, loc2(5) gets +2, loc1(10) gets +2
        locations.Single(l => l.StorageLocationId == 3).Quantity.Should().Be(4);
        locations.Single(l => l.StorageLocationId == 2).Quantity.Should().Be(7);
        locations.Single(l => l.StorageLocationId == 1).Quantity.Should().Be(12);
    }

    [Fact]
    public async Task RemoveStockByScanAsync_UnknownBarcode_ReturnsNull()
    {
        var f = CreateSut(nameof(RemoveStockByScanAsync_UnknownBarcode_ReturnsNull));
        f.ProductRepo.GetByBarcodeAsync("X", 1).Returns((Product?)null);

        (await f.Sut.RemoveStockByScanAsync("X")).Should().BeNull();
    }

    [Fact]
    public async Task RemoveStockByScanAsync_ClampsQuantityAtZero()
    {
        var f = CreateSut(nameof(RemoveStockByScanAsync_ClampsQuantityAtZero));
        var product = MakeProduct(1, quantity: 2);
        f.ProductRepo.GetByBarcodeAsync("X", 1).Returns(product);

        var result = await f.Sut.RemoveStockByScanAsync("X", 10);

        result!.Quantity.Should().Be(0);
        await f.MovementRepo.Received(1).CreateAsync(Arg.Is<StockMovement>(m => m.QuantityChange == -10 && m.Type == MovementType.ScanRemove));
    }

    [Fact]
    public async Task RemoveStockByScanAsync_ReducesLocationsDescendingAndBatchesFifo()
    {
        var f = CreateSut(nameof(RemoveStockByScanAsync_ReducesLocationsDescendingAndBatchesFifo));
        var product = MakeProduct(1, quantity: 20);
        f.ProductRepo.GetByBarcodeAsync("X", 1).Returns(product);
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.ProductStorageLocations.AddRange(
                new ProductStorageLocation { ProductId = 1, StorageLocationId = 1, Quantity = 3 },
                new ProductStorageLocation { ProductId = 1, StorageLocationId = 2, Quantity = 10 });
            db.ProductBatches.AddRange(
                new ProductBatch { ProductId = 1, BatchNumber = "early", Quantity = 4, ExpiryDate = DateTime.UtcNow.AddDays(1), CreatedAt = DateTime.UtcNow },
                new ProductBatch { ProductId = 1, BatchNumber = "late", Quantity = 4, ExpiryDate = DateTime.UtcNow.AddDays(10), CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        // Remove 8: locations ordered desc by quantity -> loc2(10) first takes 8, loc1(3) untouched.
        // Batches ordered by expiry -> "early" (4) fully consumed, "late" takes remaining 4.
        await f.Sut.RemoveStockByScanAsync("X", 8);

        await using var verify = f.ContextFactory.CreateDbContext();
        (await verify.ProductStorageLocations.SingleAsync(l => l.StorageLocationId == 2)).Quantity.Should().Be(2);
        (await verify.ProductStorageLocations.SingleAsync(l => l.StorageLocationId == 1)).Quantity.Should().Be(3);
        (await verify.ProductBatches.SingleAsync(b => b.BatchNumber == "early")).Quantity.Should().Be(0);
        (await verify.ProductBatches.SingleAsync(b => b.BatchNumber == "late")).Quantity.Should().Be(0);
    }

    [Fact]
    public async Task RemoveStockByScanAsync_NoLocationsOrBatches_StillSucceeds()
    {
        var f = CreateSut(nameof(RemoveStockByScanAsync_NoLocationsOrBatches_StillSucceeds));
        var product = MakeProduct(1, quantity: 5);
        f.ProductRepo.GetByBarcodeAsync("X", 1).Returns(product);

        var result = await f.Sut.RemoveStockByScanAsync("X", 2);

        result!.Quantity.Should().Be(3);
    }

    [Fact]
    public async Task GetRecentMovementsAsync_DelegatesToRepository()
    {
        var f = CreateSut(nameof(GetRecentMovementsAsync_DelegatesToRepository));
        f.MovementRepo.GetRecentAsync(10, 1).Returns(new[] { new StockMovement { Id = 1 } });

        (await f.Sut.GetRecentMovementsAsync(10)).Should().ContainSingle();
    }

    [Fact]
    public async Task GetMovementsByProductAsync_DelegatesToRepository()
    {
        var f = CreateSut(nameof(GetMovementsByProductAsync_DelegatesToRepository));
        f.MovementRepo.GetByProductAsync(1, 1).Returns(new[] { new StockMovement { Id = 1 } });

        (await f.Sut.GetMovementsByProductAsync(1)).Should().ContainSingle();
    }

    [Fact]
    public async Task GetDashboardStatsAsync_AggregatesAndSortsCategoryStats()
    {
        var f = CreateSut(nameof(GetDashboardStatsAsync_AggregatesAndSortsCategoryStats));
        var catA = new Category { Id = 1, Name = "A", Icon = "a" };
        var catB = new Category { Id = 2, Name = "B", Icon = "b" };
        var p1 = MakeProduct(1, price: 2m, quantity: 3);
        p1.CategoryId = 1;
        var p2 = MakeProduct(2, price: 2m, quantity: 1);
        p2.CategoryId = 1;
        var p3 = MakeProduct(3, price: 5m, quantity: 2);
        p3.CategoryId = 2;
        f.ProductRepo.GetAllAsync(1).Returns(new[] { p1, p2, p3 });
        f.CategoryRepo.GetActiveAsync(1).Returns(new[] { catA, catB });
        f.ProductRepo.GetLowStockAsync(1).Returns(new[] { MakeProduct(4) });
        f.MovementRepo.GetTodayMovementsAsync(1).Returns(new[] { new StockMovement { Id = 1 } });

        var stats = await f.Sut.GetDashboardStatsAsync();

        stats.TotalProducts.Should().Be(3);
        stats.TotalCategories.Should().Be(2);
        stats.LowStockCount.Should().Be(1);
        stats.TodayMovements.Should().Be(1);
        stats.TotalStockValue.Should().Be((int)(2m * 3 + 2m * 1 + 5m * 2));
        // Category A has 2 products (higher ProductCount) -> sorted first
        stats.CategoryStats.Select(c => c.Name).Should().ContainInOrder("A", "B");
        stats.CategoryStats[0].TotalQuantity.Should().Be(4);
    }

    [Fact]
    public async Task AdjustStockAsync_UnknownProduct_ReturnsNull()
    {
        var f = CreateSut(nameof(AdjustStockAsync_UnknownProduct_ReturnsNull));
        f.ProductRepo.GetByIdAsync(1, 1).Returns((Product?)null);

        (await f.Sut.AdjustStockAsync(1, 10)).Should().BeNull();
    }

    [Fact]
    public async Task AdjustStockAsync_Increase_DistributesToStorageLocations()
    {
        var f = CreateSut(nameof(AdjustStockAsync_Increase_DistributesToStorageLocations));
        var product = MakeProduct(1, quantity: 5);
        f.ProductRepo.GetByIdAsync(1, 1).Returns(product);
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.ProductStorageLocations.Add(new ProductStorageLocation { ProductId = 1, StorageLocationId = 1, Quantity = 0 });
            await db.SaveChangesAsync();
        }

        var result = await f.Sut.AdjustStockAsync(1, 15, "adjust up");

        result!.Quantity.Should().Be(15);
        await f.MovementRepo.Received(1).CreateAsync(Arg.Is<StockMovement>(m => m.QuantityChange == 10 && m.Type == MovementType.Adjustment && m.Notes == "adjust up"));
        await using var verify = f.ContextFactory.CreateDbContext();
        (await verify.ProductStorageLocations.SingleAsync()).Quantity.Should().Be(10);
    }

    [Fact]
    public async Task AdjustStockAsync_Decrease_ReducesLocationsAndBatches()
    {
        var f = CreateSut(nameof(AdjustStockAsync_Decrease_ReducesLocationsAndBatches));
        var product = MakeProduct(1, quantity: 15);
        f.ProductRepo.GetByIdAsync(1, 1).Returns(product);
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.ProductStorageLocations.Add(new ProductStorageLocation { ProductId = 1, StorageLocationId = 1, Quantity = 15 });
            db.ProductBatches.Add(new ProductBatch { ProductId = 1, BatchNumber = "b1", Quantity = 15, ExpiryDate = DateTime.UtcNow.AddDays(3) });
            await db.SaveChangesAsync();
        }

        var result = await f.Sut.AdjustStockAsync(1, 5);

        result!.Quantity.Should().Be(5);
        await using var verify = f.ContextFactory.CreateDbContext();
        (await verify.ProductStorageLocations.SingleAsync()).Quantity.Should().Be(5);
        (await verify.ProductBatches.SingleAsync()).Quantity.Should().Be(5);
    }

    [Fact]
    public async Task AdjustStockAsync_NoChange_CreatesZeroMovement_DoesNotTouchLocations()
    {
        var f = CreateSut(nameof(AdjustStockAsync_NoChange_CreatesZeroMovement_DoesNotTouchLocations));
        var product = MakeProduct(1, quantity: 5);
        f.ProductRepo.GetByIdAsync(1, 1).Returns(product);
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.ProductStorageLocations.Add(new ProductStorageLocation { ProductId = 1, StorageLocationId = 1, Quantity = 5 });
            await db.SaveChangesAsync();
        }

        await f.Sut.AdjustStockAsync(1, 5);

        await using var verify = f.ContextFactory.CreateDbContext();
        (await verify.ProductStorageLocations.SingleAsync()).Quantity.Should().Be(5);
        await f.MovementRepo.Received(1).CreateAsync(Arg.Is<StockMovement>(m => m.QuantityChange == 0));
    }

    // ---- Storage location queries ----

    [Fact]
    public async Task GetProductStorageLocationsAsync_IncludesStorageLocation()
    {
        var f = CreateSut(nameof(GetProductStorageLocationsAsync_IncludesStorageLocation));
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.StorageLocations.Add(new StorageLocation { Id = 1, Code = "A1", Name = "Shelf" });
            db.ProductStorageLocations.Add(new ProductStorageLocation { ProductId = 1, StorageLocationId = 1, Quantity = 3 });
            await db.SaveChangesAsync();
        }

        var list = await f.Sut.GetProductStorageLocationsAsync(1);

        list.Should().ContainSingle().Which.StorageLocation!.Code.Should().Be("A1");
    }

    [Fact]
    public async Task GetActiveStorageLocationsForProductAsync_FiltersZeroAndOrdersByCode()
    {
        var f = CreateSut(nameof(GetActiveStorageLocationsForProductAsync_FiltersZeroAndOrdersByCode));
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.StorageLocations.AddRange(
                new StorageLocation { Id = 1, Code = "B1", Name = "Shelf B" },
                new StorageLocation { Id = 2, Code = "A1", Name = "Shelf A" });
            db.ProductStorageLocations.AddRange(
                new ProductStorageLocation { ProductId = 1, StorageLocationId = 1, Quantity = 3 },
                new ProductStorageLocation { ProductId = 1, StorageLocationId = 2, Quantity = 1 },
                new ProductStorageLocation { ProductId = 1, StorageLocationId = 3, Quantity = 0 });
            await db.SaveChangesAsync();
        }

        var list = await f.Sut.GetActiveStorageLocationsForProductAsync(1);

        list.Select(l => l.StorageLocation!.Code).Should().ContainInOrder("A1", "B1");
    }

    [Fact]
    public async Task GetStorageLocationCountsForProductsAsync_EmptyIds_ReturnsEmptyDictionary()
    {
        var f = CreateSut(nameof(GetStorageLocationCountsForProductsAsync_EmptyIds_ReturnsEmptyDictionary));

        (await f.Sut.GetStorageLocationCountsForProductsAsync(Array.Empty<int>())).Should().BeEmpty();
    }

    [Fact]
    public async Task GetStorageLocationCountsForProductsAsync_CountsPerProduct()
    {
        var f = CreateSut(nameof(GetStorageLocationCountsForProductsAsync_CountsPerProduct));
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.ProductStorageLocations.AddRange(
                new ProductStorageLocation { ProductId = 1, StorageLocationId = 1, Quantity = 1 },
                new ProductStorageLocation { ProductId = 1, StorageLocationId = 2, Quantity = 1 },
                new ProductStorageLocation { ProductId = 2, StorageLocationId = 3, Quantity = 1 });
            await db.SaveChangesAsync();
        }

        var counts = await f.Sut.GetStorageLocationCountsForProductsAsync(new[] { 1, 2 });

        counts[1].Should().Be(2);
        counts[2].Should().Be(1);
    }

    [Fact]
    public async Task ReplaceProductStorageLocationsAsync_ReplacesExistingAndFiltersZeroQuantity()
    {
        var f = CreateSut(nameof(ReplaceProductStorageLocationsAsync_ReplacesExistingAndFiltersZeroQuantity));
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.ProductStorageLocations.Add(new ProductStorageLocation { ProductId = 1, StorageLocationId = 1, Quantity = 99 });
            await db.SaveChangesAsync();
        }

        await f.Sut.ReplaceProductStorageLocationsAsync(1, new[]
        {
            new ProductStorageLocationAssignment(2, 5),
            new ProductStorageLocationAssignment(3, 0) // filtered out
        });

        await using var verify = f.ContextFactory.CreateDbContext();
        var remaining = await verify.ProductStorageLocations.ToListAsync();
        remaining.Should().ContainSingle().Which.StorageLocationId.Should().Be(2);
    }

    [Fact]
    public async Task GetProductBatchesAsync_OrdersByExpiryWithNullsLast()
    {
        var f = CreateSut(nameof(GetProductBatchesAsync_OrdersByExpiryWithNullsLast));
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.ProductBatches.AddRange(
                new ProductBatch { ProductId = 1, BatchNumber = "none", Quantity = 1, ExpiryDate = null },
                new ProductBatch { ProductId = 1, BatchNumber = "soon", Quantity = 1, ExpiryDate = DateTime.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }

        var list = await f.Sut.GetProductBatchesAsync(1);

        list.Select(b => b.BatchNumber).Should().ContainInOrder("soon", "none");
    }

    [Fact]
    public async Task GetActiveBatchesForProductAsync_FiltersZeroAndOrdersByCreatedDescending()
    {
        var f = CreateSut(nameof(GetActiveBatchesForProductAsync_FiltersZeroAndOrdersByCreatedDescending));
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.ProductBatches.AddRange(
                new ProductBatch { ProductId = 1, BatchNumber = "old", Quantity = 1, CreatedAt = DateTime.UtcNow.AddDays(-2) },
                new ProductBatch { ProductId = 1, BatchNumber = "new", Quantity = 1, CreatedAt = DateTime.UtcNow },
                new ProductBatch { ProductId = 1, BatchNumber = "empty", Quantity = 0, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var list = await f.Sut.GetActiveBatchesForProductAsync(1);

        list.Select(b => b.BatchNumber).Should().ContainInOrder("new", "old");
    }

    [Fact]
    public async Task ReplaceProductBatchesAsync_ReplacesExistingAndFiltersZeroQuantity()
    {
        var f = CreateSut(nameof(ReplaceProductBatchesAsync_ReplacesExistingAndFiltersZeroQuantity));
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.ProductBatches.Add(new ProductBatch { ProductId = 1, BatchNumber = "old", Quantity = 5 });
            await db.SaveChangesAsync();
        }

        await f.Sut.ReplaceProductBatchesAsync(1, 2, new[]
        {
            new ProductBatch { BatchNumber = "kept", Quantity = 3, Notes = "n" },
            new ProductBatch { BatchNumber = "dropped", Quantity = 0 }
        });

        await using var verify = f.ContextFactory.CreateDbContext();
        var remaining = await verify.ProductBatches.ToListAsync();
        remaining.Should().ContainSingle();
        remaining[0].BatchNumber.Should().Be("kept");
        remaining[0].WarehouseId.Should().Be(2);
    }

    // ---- ProcessScannerMovementAsync ----

    private static ScannerMovementCommand MakeCommand(
        int productId = 1,
        bool isAdd = true,
        int quantity = 5,
        IReadOnlyList<ProductStorageLocationAssignment>? distribution = null,
        bool processBatch = false,
        int? selectedBatchId = null,
        string? batchNumber = null,
        DateTime? batchExpiryDate = null,
        DateTime? batchManufactureDate = null,
        string? batchNotes = null)
        => new(
            productId, isAdd, quantity, "BC1", 1,
            distribution ?? Array.Empty<ProductStorageLocationAssignment>(),
            processBatch, selectedBatchId, batchNumber, batchExpiryDate, batchManufactureDate, batchNotes,
            "movement notes");

    [Fact]
    public async Task ProcessScannerMovementAsync_UnknownProduct_ReturnsFailure()
    {
        var f = CreateSut(nameof(ProcessScannerMovementAsync_UnknownProduct_ReturnsFailure));

        var result = await f.Sut.ProcessScannerMovementAsync(MakeCommand(productId: 999));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("scanner.productnotfound");
    }

    [Fact]
    public async Task ProcessScannerMovementAsync_Add_CreatesNewLocation_AndNewBatchWithDefaultNotes()
    {
        var f = CreateSut(nameof(ProcessScannerMovementAsync_Add_CreatesNewLocation_AndNewBatchWithDefaultNotes));
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.Products.Add(MakeProduct(1, quantity: 5));
            await db.SaveChangesAsync();
        }

        var command = MakeCommand(
            isAdd: true, quantity: 4,
            distribution: new[] { new ProductStorageLocationAssignment(1, 4) },
            processBatch: true, batchNumber: "NB1");

        var result = await f.Sut.ProcessScannerMovementAsync(command);

        result.IsSuccess.Should().BeTrue();
        await using var verify = f.ContextFactory.CreateDbContext();
        (await verify.Products.FindAsync(1))!.Quantity.Should().Be(9);
        (await verify.ProductStorageLocations.SingleAsync()).Quantity.Should().Be(4);
        var batch = await verify.ProductBatches.SingleAsync();
        batch.BatchNumber.Should().Be("NB1");
        batch.Quantity.Should().Be(4);
        batch.Notes.Should().StartWith("Scanner-Eingang am");
        var movement = await verify.StockMovements.SingleAsync();
        movement.Type.Should().Be(MovementType.ScanAdd);
        movement.QuantityChange.Should().Be(4);
    }

    [Fact]
    public async Task ProcessScannerMovementAsync_Add_UpdatesExistingLocationAndExistingBatch()
    {
        var f = CreateSut(nameof(ProcessScannerMovementAsync_Add_UpdatesExistingLocationAndExistingBatch));
        ProductBatch batch;
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.Products.Add(MakeProduct(1, quantity: 5));
            db.ProductStorageLocations.Add(new ProductStorageLocation { ProductId = 1, StorageLocationId = 1, Quantity = 2 });
            batch = new ProductBatch { ProductId = 1, BatchNumber = "EX1", Quantity = 3, WarehouseId = 1 };
            db.ProductBatches.Add(batch);
            await db.SaveChangesAsync();
        }

        var command = MakeCommand(
            isAdd: true, quantity: 4,
            distribution: new[] { new ProductStorageLocationAssignment(1, 4) },
            processBatch: true, selectedBatchId: batch.Id, batchNotes: "custom note");

        await f.Sut.ProcessScannerMovementAsync(command);

        await using var verify = f.ContextFactory.CreateDbContext();
        (await verify.ProductStorageLocations.SingleAsync()).Quantity.Should().Be(6);
        (await verify.ProductBatches.SingleAsync()).Quantity.Should().Be(7);
    }

    [Fact]
    public async Task ProcessScannerMovementAsync_Add_ProcessBatchWithoutSelectionOrNumber_SkipsBatchHandling()
    {
        var f = CreateSut(nameof(ProcessScannerMovementAsync_Add_ProcessBatchWithoutSelectionOrNumber_SkipsBatchHandling));
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.Products.Add(MakeProduct(1, quantity: 5));
            await db.SaveChangesAsync();
        }

        var command = MakeCommand(isAdd: true, quantity: 2, processBatch: true);

        var result = await f.Sut.ProcessScannerMovementAsync(command);

        result.IsSuccess.Should().BeTrue();
        await using var verify = f.ContextFactory.CreateDbContext();
        (await verify.ProductBatches.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessScannerMovementAsync_Remove_ClampsProductQuantityAtZero_UpdatesLocation()
    {
        var f = CreateSut(nameof(ProcessScannerMovementAsync_Remove_ClampsProductQuantityAtZero_UpdatesLocation));
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.Products.Add(MakeProduct(1, quantity: 3));
            db.ProductStorageLocations.Add(new ProductStorageLocation { ProductId = 1, StorageLocationId = 1, Quantity = 5 });
            await db.SaveChangesAsync();
        }

        var command = MakeCommand(isAdd: false, quantity: 10, distribution: new[] { new ProductStorageLocationAssignment(1, 5) });

        await f.Sut.ProcessScannerMovementAsync(command);

        await using var verify = f.ContextFactory.CreateDbContext();
        (await verify.Products.FindAsync(1))!.Quantity.Should().Be(0);
        (await verify.ProductStorageLocations.SingleAsync()).Quantity.Should().Be(0);
        var movement = await verify.StockMovements.SingleAsync();
        movement.Type.Should().Be(MovementType.ScanRemove);
        movement.QuantityChange.Should().Be(-10);
    }

    [Fact]
    public async Task ProcessScannerMovementAsync_Remove_FifoAcrossBatches_RemovesEmptiedBatches()
    {
        var f = CreateSut(nameof(ProcessScannerMovementAsync_Remove_FifoAcrossBatches_RemovesEmptiedBatches));
        ProductBatch selected, early, late;
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.Products.Add(MakeProduct(1, quantity: 20));
            selected = new ProductBatch { ProductId = 1, BatchNumber = "selected", Quantity = 2, WarehouseId = 1, ExpiryDate = DateTime.UtcNow.AddDays(5) };
            early = new ProductBatch { ProductId = 1, BatchNumber = "early", Quantity = 3, WarehouseId = 1, ExpiryDate = DateTime.UtcNow.AddDays(1) };
            late = new ProductBatch { ProductId = 1, BatchNumber = "late", Quantity = 10, WarehouseId = 1, ExpiryDate = DateTime.UtcNow.AddDays(10) };
            db.ProductBatches.AddRange(selected, early, late);
            await db.SaveChangesAsync();
        }

        // Remove 8: selectedBatch (2) drained first & removed, then FIFO by expiry across the rest:
        // "early" (3) drained & removed, "late" takes remaining 3 of 6 -> 7 left.
        var command = MakeCommand(isAdd: false, quantity: 8, processBatch: true, selectedBatchId: selected.Id);

        await f.Sut.ProcessScannerMovementAsync(command);

        await using var verify = f.ContextFactory.CreateDbContext();
        var remainingBatches = await verify.ProductBatches.ToListAsync();
        remainingBatches.Select(b => b.BatchNumber).Should().BeEquivalentTo(new[] { "late" });
        remainingBatches.Single().Quantity.Should().Be(7);
    }

    [Fact]
    public async Task ProcessScannerMovementAsync_Remove_ProcessBatchFalse_LeavesBatchesUntouched()
    {
        var f = CreateSut(nameof(ProcessScannerMovementAsync_Remove_ProcessBatchFalse_LeavesBatchesUntouched));
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.Products.Add(MakeProduct(1, quantity: 20));
            db.ProductBatches.Add(new ProductBatch { ProductId = 1, BatchNumber = "b1", Quantity = 10, WarehouseId = 1 });
            await db.SaveChangesAsync();
        }

        var command = MakeCommand(isAdd: false, quantity: 8, processBatch: false);

        await f.Sut.ProcessScannerMovementAsync(command);

        await using var verify = f.ContextFactory.CreateDbContext();
        (await verify.ProductBatches.SingleAsync()).Quantity.Should().Be(10);
    }

    [Fact]
    public async Task ProcessScannerMovementAsync_Remove_UnknownStorageLocation_DoesNotCreateOne()
    {
        var f = CreateSut(nameof(ProcessScannerMovementAsync_Remove_UnknownStorageLocation_DoesNotCreateOne));
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.Products.Add(MakeProduct(1, quantity: 20));
            await db.SaveChangesAsync();
        }

        var command = MakeCommand(isAdd: false, quantity: 5, distribution: new[] { new ProductStorageLocationAssignment(99, 5) });

        await f.Sut.ProcessScannerMovementAsync(command);

        await using var verify = f.ContextFactory.CreateDbContext();
        (await verify.ProductStorageLocations.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessScannerMovementAsync_ExceptionDuringProcessing_ReturnsFailure()
    {
        var f = CreateSut(nameof(ProcessScannerMovementAsync_ExceptionDuringProcessing_ReturnsFailure));
        // Product must exist so the flow reaches SaveChangesAsync(cancellationToken), which is
        // the point where EF Core reliably observes an already-canceled token and throws
        // (CreateDbContextAsync itself does not check cancellation for the InMemory provider).
        await using (var db = f.ContextFactory.CreateDbContext())
        {
            db.Products.Add(MakeProduct(1));
            await db.SaveChangesAsync();
        }
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await f.Sut.ProcessScannerMovementAsync(MakeCommand(), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("scanner.movementfailed");
    }
}
