using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Inventory;

public class ExpiryServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static (ExpiryService sut, IDbContextFactory<InventoryDbContext> factory) CreateSut(string dbName)
    {
        var factory = new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(dbName).Options);
        var notifier = Substitute.For<INotificationService>();
        return (new ExpiryService(factory, notifier, NullLogger<ExpiryService>.Instance), factory);
    }

    private static Product MakeProduct(string name, DateTime? expiry, int warehouseId = 1)
        => new()
        {
            Name = name,
            WarehouseId = warehouseId,
            CategoryId = 1,
            TrackExpiryDate = true,
            ExpiryDate = expiry,
            Quantity = 10
        };

    private static async Task SeedCategoryAsync(IDbContextFactory<InventoryDbContext> factory)
    {
        await using var db = factory.CreateDbContext();
        if (await db.Categories.AnyAsync(c => c.Id == 1)) return;
        db.Categories.Add(new Category { Id = 1, Name = "Misc" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetExpiringProductsAsync_ReturnsOnlyUpcomingWithinThreshold()
    {
        var (sut, factory) = CreateSut(nameof(GetExpiringProductsAsync_ReturnsOnlyUpcomingWithinThreshold));
        await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.AddRange(
                MakeProduct("soon", DateTime.UtcNow.AddDays(3)),
                MakeProduct("later", DateTime.UtcNow.AddDays(30)),
                MakeProduct("expired", DateTime.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
        }

        var list = await sut.GetExpiringProductsAsync(1, daysThreshold: 7);

        list.Should().ContainSingle().Which.Name.Should().Be("soon");
    }

    [Fact]
    public async Task GetExpiredProductsAsync_ReturnsPastExpiry()
    {
        var (sut, factory) = CreateSut(nameof(GetExpiredProductsAsync_ReturnsPastExpiry));
        await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.AddRange(
                MakeProduct("expired", DateTime.UtcNow.AddDays(-5)),
                MakeProduct("fresh", DateTime.UtcNow.AddDays(5)));
            await db.SaveChangesAsync();
        }

        var list = await sut.GetExpiredProductsAsync(1);

        list.Should().ContainSingle().Which.Name.Should().Be("expired");
    }

    [Fact]
    public async Task GetExpiredProductsAsync_IgnoresNonTracked()
    {
        var (sut, factory) = CreateSut(nameof(GetExpiredProductsAsync_IgnoresNonTracked));
        await using (var db = factory.CreateDbContext())
        {
            var p = MakeProduct("expired", DateTime.UtcNow.AddDays(-5));
            p.TrackExpiryDate = false;
            db.Products.Add(p);
            await db.SaveChangesAsync();
        }

        (await sut.GetExpiredProductsAsync(1)).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldTrackExpiryForCategoryAsync_RecognizesFoodKeywords()
    {
        var (sut, factory) = CreateSut(nameof(ShouldTrackExpiryForCategoryAsync_RecognizesFoodKeywords));
        await using (var db = factory.CreateDbContext())
        {
            db.Categories.AddRange(
                new Category { Id = 1, Name = "Lebensmittel" },
                new Category { Id = 2, Name = "Food & Drink" },
                new Category { Id = 3, Name = "Electronics" });
            await db.SaveChangesAsync();
        }

        (await sut.ShouldTrackExpiryForCategoryAsync(1)).Should().BeTrue();
        (await sut.ShouldTrackExpiryForCategoryAsync(2)).Should().BeTrue();
        (await sut.ShouldTrackExpiryForCategoryAsync(3)).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldTrackExpiryForCategoryAsync_UnknownCategory_ReturnsFalse()
    {
        var (sut, _) = CreateSut(nameof(ShouldTrackExpiryForCategoryAsync_UnknownCategory_ReturnsFalse));

        (await sut.ShouldTrackExpiryForCategoryAsync(999)).Should().BeFalse();
    }

    [Fact]
    public async Task GetExpiringBatchesCountAsync_CountsWithinThreshold()
    {
        var (sut, factory) = CreateSut(nameof(GetExpiringBatchesCountAsync_CountsWithinThreshold));
        await using (var db = factory.CreateDbContext())
        {
            db.ProductBatches.AddRange(
                new ProductBatch { BatchNumber = "B1", WarehouseId = 1, Quantity = 3, ExpiryDate = DateTime.UtcNow.AddDays(2) },
                new ProductBatch { BatchNumber = "B2", WarehouseId = 1, Quantity = 5, ExpiryDate = DateTime.UtcNow.AddDays(20) },
                new ProductBatch { BatchNumber = "B3", WarehouseId = 1, Quantity = 0, ExpiryDate = DateTime.UtcNow.AddDays(2) },
                new ProductBatch { BatchNumber = "B4", WarehouseId = 2, Quantity = 1, ExpiryDate = DateTime.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }

        (await sut.GetExpiringBatchesCountAsync(1, daysThreshold: 7)).Should().Be(1);
    }

    [Fact]
    public async Task MarkBatchAsDisposedAsync_UnknownBatch_ReturnsNotFound()
    {
        var (sut, _) = CreateSut(nameof(MarkBatchAsDisposedAsync_UnknownBatch_ReturnsNotFound));

        var r = await sut.MarkBatchAsDisposedAsync(999);

        r.ErrorCode.Should().Be("batch.notfound");
    }

    [Fact]
    public async Task MarkBatchAsDisposedAsync_AlreadyEmpty_ReturnsAlreadyDisposed()
    {
        var (sut, factory) = CreateSut(nameof(MarkBatchAsDisposedAsync_AlreadyEmpty_ReturnsAlreadyDisposed));
        ProductBatch batch;
        await using (var db = factory.CreateDbContext())
        {
            batch = new ProductBatch { BatchNumber = "B1", WarehouseId = 1, Quantity = 0, ProductId = 1 };
            db.ProductBatches.Add(batch);
            await db.SaveChangesAsync();
        }

        var r = await sut.MarkBatchAsDisposedAsync(batch.Id);

        r.ErrorCode.Should().Be("batch.alreadydisposed");
    }

    [Fact]
    public async Task MarkBatchAsDisposedAsync_SuccessPath_ZeroesBatchAndCreatesMovement()
    {
        var (sut, factory) = CreateSut(nameof(MarkBatchAsDisposedAsync_SuccessPath_ZeroesBatchAndCreatesMovement));
        ProductBatch batch;
        Product product;
        await using (var db = factory.CreateDbContext())
        {
            product = new Product { Name = "P", WarehouseId = 1, Quantity = 20 };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            batch = new ProductBatch
            {
                BatchNumber = "B1",
                WarehouseId = 1,
                ProductId = product.Id,
                Quantity = 5,
                ExpiryDate = DateTime.UtcNow.AddDays(-1)
            };
            db.ProductBatches.Add(batch);
            await db.SaveChangesAsync();
        }

        var r = await sut.MarkBatchAsDisposedAsync(batch.Id, notes: "expired");

        r.IsSuccess.Should().BeTrue();
        await using var verify = factory.CreateDbContext();
        (await verify.ProductBatches.FindAsync(batch.Id))!.Quantity.Should().Be(0);
        (await verify.Products.FindAsync(product.Id))!.Quantity.Should().Be(15);
        var movement = await verify.StockMovements.SingleAsync();
        movement.QuantityChange.Should().Be(-5);
        movement.Type.Should().Be(MovementType.Disposal);
        movement.Notes.Should().Be("expired");
    }

    [Fact]
    public async Task GetBatchesForProductAsync_OrdersByExpiry()
    {
        var (sut, factory) = CreateSut(nameof(GetBatchesForProductAsync_OrdersByExpiry));
        await using (var db = factory.CreateDbContext())
        {
            db.ProductBatches.AddRange(
                new ProductBatch { BatchNumber = "late", ProductId = 1, WarehouseId = 1, Quantity = 1, ExpiryDate = DateTime.UtcNow.AddDays(10) },
                new ProductBatch { BatchNumber = "soon", ProductId = 1, WarehouseId = 1, Quantity = 1, ExpiryDate = DateTime.UtcNow.AddDays(1) },
                new ProductBatch { BatchNumber = "other", ProductId = 2, WarehouseId = 1, Quantity = 1, ExpiryDate = DateTime.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }

        var list = await sut.GetBatchesForProductAsync(1);

        list.Select(b => b.BatchNumber).Should().ContainInOrder("soon", "late");
    }

    [Fact]
    public async Task GetNextExpiringBatchForProductAsync_ReturnsEarliestNonEmpty()
    {
        var (sut, factory) = CreateSut(nameof(GetNextExpiringBatchForProductAsync_ReturnsEarliestNonEmpty));
        await using (var db = factory.CreateDbContext())
        {
            db.ProductBatches.AddRange(
                new ProductBatch { BatchNumber = "empty", ProductId = 1, WarehouseId = 1, Quantity = 0, ExpiryDate = DateTime.UtcNow.AddDays(1) },
                new ProductBatch { BatchNumber = "earliest", ProductId = 1, WarehouseId = 1, Quantity = 3, ExpiryDate = DateTime.UtcNow.AddDays(2) },
                new ProductBatch { BatchNumber = "later", ProductId = 1, WarehouseId = 1, Quantity = 3, ExpiryDate = DateTime.UtcNow.AddDays(5) });
            await db.SaveChangesAsync();
        }

        var batch = await sut.GetNextExpiringBatchForProductAsync(1);

        batch!.BatchNumber.Should().Be("earliest");
    }
}
