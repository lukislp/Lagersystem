using LagersystemLVHome.Data;
using LagersystemLVHome.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.UnitTests.Data.Repositories;

public class StockMovementRepositoryTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static (StockMovementRepository sut, IDbContextFactory<InventoryDbContext> factory) CreateSut(string dbName)
    {
        var factory = new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(dbName).Options);
        return (new StockMovementRepository(factory), factory);
    }

    // GetAllAsync/GetByProductAsync/GetRecentAsync all Include(sm => sm.Product), and
    // StockMovement.ProductId is a required FK - EF Core's InMemory provider silently drops rows
    // from Include() results when the FK doesn't resolve to an existing row. So any test
    // exercising those methods needs a real, persisted Product to point ProductId at.
    private static async Task<int> SeedProductAsync(IDbContextFactory<InventoryDbContext> factory, int warehouseId = 1)
    {
        await using var db = factory.CreateDbContext();
        var category = new Category { Name = "Cat", WarehouseId = warehouseId };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        var product = new Product { Name = "P", WarehouseId = warehouseId, CategoryId = category.Id, Price = 1m };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    [Fact]
    public async Task GetAllAsync_FiltersByWarehouseAndOrdersDescendingByTimestamp()
    {
        var (sut, factory) = CreateSut(nameof(GetAllAsync_FiltersByWarehouseAndOrdersDescendingByTimestamp));
        var productId = await SeedProductAsync(factory);
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.StockMovements.AddRange(
                new StockMovement { ProductId = productId, WarehouseId = 1, Timestamp = now.AddMinutes(-10), Type = MovementType.ManualAdd },
                new StockMovement { ProductId = productId, WarehouseId = 1, Timestamp = now, Type = MovementType.ManualAdd },
                new StockMovement { ProductId = productId, WarehouseId = 2, Timestamp = now, Type = MovementType.ManualAdd });
            await db.SaveChangesAsync();
        }

        var results = (await sut.GetAllAsync(1)).ToList();

        results.Should().HaveCount(2);
        results[0].Timestamp.Should().BeAfter(results[1].Timestamp);
    }

    [Fact]
    public async Task GetAllAsync_IncludesProductAndCategory()
    {
        var (sut, factory) = CreateSut(nameof(GetAllAsync_IncludesProductAndCategory));
        await using (var db = factory.CreateDbContext())
        {
            var category = new Category { Name = "Cat", WarehouseId = 1 };
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            var product = new Product { Name = "P", WarehouseId = 1, CategoryId = category.Id, Price = 1m };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            db.StockMovements.Add(new StockMovement { ProductId = product.Id, WarehouseId = 1, Type = MovementType.Initial });
            await db.SaveChangesAsync();
        }

        var result = (await sut.GetAllAsync(1)).Single();

        result.Product.Should().NotBeNull();
        result.Product!.Category.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByProductAsync_FiltersByProductAndWarehouse_OrderedDescending()
    {
        var (sut, factory) = CreateSut(nameof(GetByProductAsync_FiltersByProductAndWarehouse_OrderedDescending));
        var product1Id = await SeedProductAsync(factory);
        var product2Id = await SeedProductAsync(factory);
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.StockMovements.AddRange(
                new StockMovement { ProductId = product1Id, WarehouseId = 1, Timestamp = now.AddMinutes(-5), Type = MovementType.ManualAdd },
                new StockMovement { ProductId = product1Id, WarehouseId = 1, Timestamp = now, Type = MovementType.ManualRemove },
                new StockMovement { ProductId = product2Id, WarehouseId = 1, Timestamp = now, Type = MovementType.ManualAdd },
                new StockMovement { ProductId = product1Id, WarehouseId = 2, Timestamp = now, Type = MovementType.ManualAdd });
            await db.SaveChangesAsync();
        }

        var results = (await sut.GetByProductAsync(product1Id, 1)).ToList();

        results.Should().HaveCount(2);
        results[0].Type.Should().Be(MovementType.ManualRemove);
        results[1].Type.Should().Be(MovementType.ManualAdd);
    }

    [Fact]
    public async Task GetRecentAsync_LimitsCountAndOrdersDescending()
    {
        var (sut, factory) = CreateSut(nameof(GetRecentAsync_LimitsCountAndOrdersDescending));
        var productId = await SeedProductAsync(factory);
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            for (var i = 0; i < 5; i++)
            {
                db.StockMovements.Add(new StockMovement
                {
                    ProductId = productId,
                    WarehouseId = 1,
                    Timestamp = now.AddMinutes(-i),
                    Type = MovementType.ManualAdd
                });
            }
            await db.SaveChangesAsync();
        }

        var results = (await sut.GetRecentAsync(2, 1)).ToList();

        results.Should().HaveCount(2);
        results[0].Timestamp.Should().BeAfter(results[1].Timestamp);
    }

    [Fact]
    public async Task GetTodayMovementsAsync_ReturnsOnlyTodaysMovementsForWarehouse()
    {
        var (sut, factory) = CreateSut(nameof(GetTodayMovementsAsync_ReturnsOnlyTodaysMovementsForWarehouse));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.StockMovements.AddRange(
                new StockMovement { ProductId = 1, WarehouseId = 1, Timestamp = now, Type = MovementType.ManualAdd },
                new StockMovement { ProductId = 1, WarehouseId = 1, Timestamp = now.AddDays(-2), Type = MovementType.ManualAdd },
                new StockMovement { ProductId = 1, WarehouseId = 2, Timestamp = now, Type = MovementType.ManualAdd });
            await db.SaveChangesAsync();
        }

        var results = await sut.GetTodayMovementsAsync(1);

        results.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateAsync_SetsTimestampAndPersists()
    {
        var (sut, factory) = CreateSut(nameof(CreateAsync_SetsTimestampAndPersists));
        var movement = new StockMovement { ProductId = 1, WarehouseId = 1, Type = MovementType.ManualAdd, QuantityChange = 5 };

        var created = await sut.CreateAsync(movement);

        created.Timestamp.Should().NotBe(default);

        await using var db = factory.CreateDbContext();
        (await db.StockMovements.CountAsync()).Should().Be(1);
    }
}
