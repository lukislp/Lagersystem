using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Inventory;

public class CategorySeederServiceTests
{
    private static IServiceProvider BuildProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<InventoryDbContext>(opt => opt.UseInMemoryDatabase(dbName));
        return services.BuildServiceProvider();
    }

    private static CategorySeederService BuildSut(IServiceProvider provider)
        => new(provider, NullLogger<CategorySeederService>.Instance);

    [Fact]
    public async Task SeedCategoriesAsync_NoWarehouse_NoCategoriesCreated()
    {
        var sp = BuildProvider(nameof(SeedCategoriesAsync_NoWarehouse_NoCategoriesCreated));
        var sut = BuildSut(sp);

        await sut.SeedCategoriesAsync();

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        (await db.Categories.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SeedCategoriesAsync_UnknownWarehouseId_NoCategoriesCreated()
    {
        var sp = BuildProvider(nameof(SeedCategoriesAsync_UnknownWarehouseId_NoCategoriesCreated));
        var sut = BuildSut(sp);

        await sut.SeedCategoriesAsync(warehouseId: 999);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        (await db.Categories.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SeedCategoriesAsync_WithExistingWarehouse_SeedsAllCategories()
    {
        var sp = BuildProvider(nameof(SeedCategoriesAsync_WithExistingWarehouse_SeedsAllCategories));
        int warehouseId;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var wh = new Warehouse { Name = "Main", CreatedAt = DateTime.UtcNow };
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;
        }

        await BuildSut(sp).SeedCategoriesAsync(warehouseId);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var count = await db.Categories.Where(c => c.WarehouseId == warehouseId).CountAsync();
            count.Should().BeGreaterOrEqualTo(33);
        }
    }

    [Fact]
    public async Task SeedCategoriesAsync_RunTwice_IsIdempotent()
    {
        var sp = BuildProvider(nameof(SeedCategoriesAsync_RunTwice_IsIdempotent));
        int warehouseId;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var wh = new Warehouse { Name = "Main", CreatedAt = DateTime.UtcNow };
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;
        }

        var sut = BuildSut(sp);
        await sut.SeedCategoriesAsync(warehouseId);
        await sut.SeedCategoriesAsync(warehouseId);

        using var verify = sp.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var count = await verifyDb.Categories.Where(c => c.WarehouseId == warehouseId).CountAsync();
        count.Should().BeGreaterOrEqualTo(33);
        count.Should().BeLessThan(70);
    }

    [Fact]
    public async Task SeedCategoriesAsync_WithoutId_UsesMostRecentWarehouse()
    {
        var sp = BuildProvider(nameof(SeedCategoriesAsync_WithoutId_UsesMostRecentWarehouse));
        int newestId;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            db.Warehouses.Add(new Warehouse { Name = "Old", CreatedAt = DateTime.UtcNow.AddDays(-10) });
            var newest = new Warehouse { Name = "New", CreatedAt = DateTime.UtcNow };
            db.Warehouses.Add(newest);
            await db.SaveChangesAsync();
            newestId = newest.Id;
        }

        await BuildSut(sp).SeedCategoriesAsync();

        using var verify = sp.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<InventoryDbContext>();
        (await verifyDb.Categories.Where(c => c.WarehouseId == newestId).CountAsync())
            .Should().BeGreaterOrEqualTo(33);
    }
}
