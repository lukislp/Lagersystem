using LagersystemLVHome.Data;
using LagersystemLVHome.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.UnitTests.Data.Repositories;

public class CategoryRepositoryTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static (CategoryRepository sut, IDbContextFactory<InventoryDbContext> factory) CreateSut(string dbName)
    {
        var factory = new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(dbName).Options);
        return (new CategoryRepository(factory), factory);
    }

    [Fact]
    public async Task GetAllAsync_FiltersByWarehouseAndOrdersByName()
    {
        var (sut, factory) = CreateSut(nameof(GetAllAsync_FiltersByWarehouseAndOrdersByName));
        await using (var db = factory.CreateDbContext())
        {
            db.Categories.AddRange(
                new Category { Name = "Zeta", WarehouseId = 1 },
                new Category { Name = "Alpha", WarehouseId = 1 },
                new Category { Name = "Other", WarehouseId = 2 });
            await db.SaveChangesAsync();
        }

        var results = (await sut.GetAllAsync(1)).ToList();

        results.Select(c => c.Name).Should().ContainInOrder("Alpha", "Zeta");
    }

    [Fact]
    public async Task GetAllAsync_IncludesProducts()
    {
        var (sut, factory) = CreateSut(nameof(GetAllAsync_IncludesProducts));
        await using (var db = factory.CreateDbContext())
        {
            var category = new Category { Name = "Cat", WarehouseId = 1 };
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            db.Products.Add(new Product { Name = "P", WarehouseId = 1, CategoryId = category.Id, Price = 1m });
            await db.SaveChangesAsync();
        }

        var results = await sut.GetAllAsync(1);

        results.Should().ContainSingle().Which.Products.Should().ContainSingle();
    }

    [Fact]
    public async Task GetActiveAsync_FiltersInactiveAndOtherWarehouses()
    {
        var (sut, factory) = CreateSut(nameof(GetActiveAsync_FiltersInactiveAndOtherWarehouses));
        await using (var db = factory.CreateDbContext())
        {
            db.Categories.AddRange(
                new Category { Name = "Active1", WarehouseId = 1, IsActive = true },
                new Category { Name = "Inactive", WarehouseId = 1, IsActive = false },
                new Category { Name = "OtherWarehouse", WarehouseId = 2, IsActive = true });
            await db.SaveChangesAsync();
        }

        var results = await sut.GetActiveAsync(1);

        results.Should().ContainSingle().Which.Name.Should().Be("Active1");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenWarehouseMismatch()
    {
        var (sut, factory) = CreateSut(nameof(GetByIdAsync_ReturnsNull_WhenWarehouseMismatch));
        int id;
        await using (var db = factory.CreateDbContext())
        {
            var category = new Category { Name = "Cat", WarehouseId = 1 };
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            id = category.Id;
        }

        (await sut.GetByIdAsync(id, 1)).Should().NotBeNull();
        (await sut.GetByIdAsync(id, 2)).Should().BeNull();
    }

    [Fact]
    public async Task GetByNameAsync_IsCaseInsensitive()
    {
        var (sut, factory) = CreateSut(nameof(GetByNameAsync_IsCaseInsensitive));
        await using (var db = factory.CreateDbContext())
        {
            db.Categories.Add(new Category { Name = "Groceries", WarehouseId = 1 });
            await db.SaveChangesAsync();
        }

        (await sut.GetByNameAsync("groceries")).Should().NotBeNull();
        (await sut.GetByNameAsync("GROCERIES")).Should().NotBeNull();
        (await sut.GetByNameAsync("nonexistent")).Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_SetsCreatedAtAndPersists()
    {
        var (sut, factory) = CreateSut(nameof(CreateAsync_SetsCreatedAtAndPersists));
        var category = new Category { Name = "New", WarehouseId = 1 };

        var created = await sut.CreateAsync(category);

        created.CreatedAt.Should().NotBe(default);

        await using var db = factory.CreateDbContext();
        (await db.Categories.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var (sut, factory) = CreateSut(nameof(UpdateAsync_PersistsChanges));
        var category = await sut.CreateAsync(new Category { Name = "Orig", WarehouseId = 1 });

        category.Name = "Renamed";
        await sut.UpdateAsync(category);

        await using var db = factory.CreateDbContext();
        (await db.Categories.FindAsync(category.Id))!.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task DeleteAsync_RemovesExistingCategory()
    {
        var (sut, factory) = CreateSut(nameof(DeleteAsync_RemovesExistingCategory));
        var category = await sut.CreateAsync(new Category { Name = "A", WarehouseId = 1 });

        await sut.DeleteAsync(category.Id);

        await using var db = factory.CreateDbContext();
        (await db.Categories.FindAsync(category.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_DoesNotThrow()
    {
        var (sut, _) = CreateSut(nameof(DeleteAsync_UnknownId_DoesNotThrow));

        var act = () => sut.DeleteAsync(999);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetProductCountAsync_CountsProductsInCategory()
    {
        var (sut, factory) = CreateSut(nameof(GetProductCountAsync_CountsProductsInCategory));
        await using (var db = factory.CreateDbContext())
        {
            var category1 = new Category { Name = "C1", WarehouseId = 1 };
            var category2 = new Category { Name = "C2", WarehouseId = 1 };
            db.Categories.AddRange(category1, category2);
            await db.SaveChangesAsync();

            db.Products.AddRange(
                new Product { Name = "P1", WarehouseId = 1, CategoryId = category1.Id, Price = 1m },
                new Product { Name = "P2", WarehouseId = 1, CategoryId = category1.Id, Price = 1m },
                new Product { Name = "P3", WarehouseId = 1, CategoryId = category2.Id, Price = 1m });
            await db.SaveChangesAsync();

            (await sut.GetProductCountAsync(category1.Id)).Should().Be(2);
            (await sut.GetProductCountAsync(category2.Id)).Should().Be(1);
            (await sut.GetProductCountAsync(999)).Should().Be(0);
        }
    }
}
