using LagersystemLVHome.Data;
using LagersystemLVHome.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.UnitTests.Data.Repositories;

public class ProductRepositoryTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static (ProductRepository sut, IDbContextFactory<InventoryDbContext> factory) CreateSut(string dbName)
    {
        var factory = new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(dbName).Options);
        return (new ProductRepository(factory), factory);
    }

    private static async Task<Category> SeedCategoryAsync(IDbContextFactory<InventoryDbContext> factory, int warehouseId = 1)
    {
        await using var db = factory.CreateDbContext();
        var category = new Category { Name = "Cat", WarehouseId = warehouseId };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    [Fact]
    public async Task GetAllAsync_FiltersByWarehouseAndIncludesCategory()
    {
        var (sut, factory) = CreateSut(nameof(GetAllAsync_FiltersByWarehouseAndIncludesCategory));
        var category = await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.AddRange(
                new Product { Name = "A", WarehouseId = 1, CategoryId = category.Id, Price = 1m },
                new Product { Name = "B", WarehouseId = 2, CategoryId = category.Id, Price = 1m });
            await db.SaveChangesAsync();
        }

        var results = (await sut.GetAllAsync(1)).ToList();

        results.Should().ContainSingle();
        results[0].Category.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenWarehouseMismatch()
    {
        var (sut, factory) = CreateSut(nameof(GetByIdAsync_ReturnsNull_WhenWarehouseMismatch));
        var category = await SeedCategoryAsync(factory);
        int id;
        await using (var db = factory.CreateDbContext())
        {
            var p = new Product { Name = "A", WarehouseId = 1, CategoryId = category.Id, Price = 1m };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            id = p.Id;
        }

        (await sut.GetByIdAsync(id, 1)).Should().NotBeNull();
        (await sut.GetByIdAsync(id, 2)).Should().BeNull();
    }

    [Fact]
    public async Task GetByBarcodeAsync_MatchesWithinWarehouse()
    {
        var (sut, factory) = CreateSut(nameof(GetByBarcodeAsync_MatchesWithinWarehouse));
        var category = await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.Add(new Product { Name = "A", WarehouseId = 1, CategoryId = category.Id, Price = 1m, Barcode = "12345" });
            await db.SaveChangesAsync();
        }

        (await sut.GetByBarcodeAsync("12345", 1)).Should().NotBeNull();
        (await sut.GetByBarcodeAsync("12345", 2)).Should().BeNull();
        (await sut.GetByBarcodeAsync("nope", 1)).Should().BeNull();
    }

    [Fact]
    public async Task GetByCategoryAsync_FiltersByCategoryAndWarehouse()
    {
        var (sut, factory) = CreateSut(nameof(GetByCategoryAsync_FiltersByCategoryAndWarehouse));
        var category1 = await SeedCategoryAsync(factory);
        Category category2;
        await using (var db = factory.CreateDbContext())
        {
            category2 = new Category { Name = "Cat2", WarehouseId = 1 };
            db.Categories.Add(category2);
            await db.SaveChangesAsync();

            db.Products.AddRange(
                new Product { Name = "A", WarehouseId = 1, CategoryId = category1.Id, Price = 1m },
                new Product { Name = "B", WarehouseId = 1, CategoryId = category2.Id, Price = 1m },
                new Product { Name = "C", WarehouseId = 2, CategoryId = category1.Id, Price = 1m });
            await db.SaveChangesAsync();
        }

        var results = (await sut.GetByCategoryAsync(category1.Id, 1)).ToList();

        results.Should().ContainSingle().Which.Name.Should().Be("A");
    }

    [Fact]
    public async Task GetLowStockAsync_ReturnsOnlyProductsAtOrBelowMinQuantity_OrderedAscending()
    {
        var (sut, factory) = CreateSut(nameof(GetLowStockAsync_ReturnsOnlyProductsAtOrBelowMinQuantity_OrderedAscending));
        var category = await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.AddRange(
                new Product { Name = "Low2", WarehouseId = 1, CategoryId = category.Id, Price = 1m, Quantity = 2, MinQuantity = 5 },
                new Product { Name = "Low1", WarehouseId = 1, CategoryId = category.Id, Price = 1m, Quantity = 1, MinQuantity = 5 },
                new Product { Name = "Ok", WarehouseId = 1, CategoryId = category.Id, Price = 1m, Quantity = 10, MinQuantity = 5 },
                new Product { Name = "OtherWarehouse", WarehouseId = 2, CategoryId = category.Id, Price = 1m, Quantity = 0, MinQuantity = 5 });
            await db.SaveChangesAsync();
        }

        var results = (await sut.GetLowStockAsync(1)).ToList();

        results.Select(p => p.Name).Should().ContainInOrder("Low1", "Low2");
    }

    [Fact]
    public async Task CreateAsync_ValidCategory_PersistsAndSetsTimestamps()
    {
        var (sut, factory) = CreateSut(nameof(CreateAsync_ValidCategory_PersistsAndSetsTimestamps));
        var category = await SeedCategoryAsync(factory);
        var product = new Product { Name = "New", WarehouseId = 1, CategoryId = category.Id, Price = 5m };

        var created = await sut.CreateAsync(product);

        created.CreatedAt.Should().NotBe(default);
        created.UpdatedAt.Should().NotBe(default);

        await using var db = factory.CreateDbContext();
        (await db.Products.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_UnknownCategory_Throws()
    {
        var (sut, _) = CreateSut(nameof(CreateAsync_UnknownCategory_Throws));
        var product = new Product { Name = "New", WarehouseId = 1, CategoryId = 999, Price = 5m };

        var act = () => sut.CreateAsync(product);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Category with ID 999*");
    }

    [Fact]
    public async Task CreateAsync_NoCategoryId_Throws()
    {
        var (sut, _) = CreateSut(nameof(CreateAsync_NoCategoryId_Throws));
        var product = new Product { Name = "New", WarehouseId = 1, CategoryId = 0, Price = 5m };

        var act = () => sut.CreateAsync(product);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*valid CategoryId*");
    }

    [Fact]
    public async Task UpdateAsync_UpdatesTimestampAndPersists()
    {
        var (sut, factory) = CreateSut(nameof(UpdateAsync_UpdatesTimestampAndPersists));
        var category = await SeedCategoryAsync(factory);
        var product = await sut.CreateAsync(new Product { Name = "Orig", WarehouseId = 1, CategoryId = category.Id, Price = 1m });

        product.Name = "Renamed";
        var updated = await sut.UpdateAsync(product);

        updated.Name.Should().Be("Renamed");

        await using var db = factory.CreateDbContext();
        (await db.Products.FindAsync(product.Id))!.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task DeleteAsync_RemovesExistingProduct()
    {
        var (sut, factory) = CreateSut(nameof(DeleteAsync_RemovesExistingProduct));
        var category = await SeedCategoryAsync(factory);
        var product = await sut.CreateAsync(new Product { Name = "A", WarehouseId = 1, CategoryId = category.Id, Price = 1m });

        await sut.DeleteAsync(product.Id);

        await using var db = factory.CreateDbContext();
        (await db.Products.FindAsync(product.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_DoesNotThrow()
    {
        var (sut, _) = CreateSut(nameof(DeleteAsync_UnknownId_DoesNotThrow));

        var act = () => sut.DeleteAsync(999);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetTotalCountAsync_CountsAcrossAllWarehouses()
    {
        var (sut, factory) = CreateSut(nameof(GetTotalCountAsync_CountsAcrossAllWarehouses));
        var category = await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.AddRange(
                new Product { Name = "A", WarehouseId = 1, CategoryId = category.Id, Price = 1m },
                new Product { Name = "B", WarehouseId = 2, CategoryId = category.Id, Price = 1m });
            await db.SaveChangesAsync();
        }

        (await sut.GetTotalCountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task GetLowStockCountAsync_CountsAcrossAllWarehouses()
    {
        var (sut, factory) = CreateSut(nameof(GetLowStockCountAsync_CountsAcrossAllWarehouses));
        var category = await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.AddRange(
                new Product { Name = "Low", WarehouseId = 1, CategoryId = category.Id, Price = 1m, Quantity = 1, MinQuantity = 5 },
                new Product { Name = "Ok", WarehouseId = 1, CategoryId = category.Id, Price = 1m, Quantity = 10, MinQuantity = 5 });
            await db.SaveChangesAsync();
        }

        (await sut.GetLowStockCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_BlankTerm_ReturnsEmpty()
    {
        var (sut, _) = CreateSut(nameof(SearchAsync_BlankTerm_ReturnsEmpty));

        (await sut.SearchAsync("   ", 1)).Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_MatchesNameBarcodeOrDescription_CaseInsensitive_OrderedByName()
    {
        var (sut, factory) = CreateSut(nameof(SearchAsync_MatchesNameBarcodeOrDescription_CaseInsensitive_OrderedByName));
        var category = await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.AddRange(
                new Product { Name = "Zebra Widget", WarehouseId = 1, CategoryId = category.Id, Price = 1m, Barcode = "", Description = "" },
                new Product { Name = "Apple Gadget", WarehouseId = 1, CategoryId = category.Id, Price = 1m, Barcode = "WIDGET-001", Description = "" },
                new Product { Name = "Something", WarehouseId = 1, CategoryId = category.Id, Price = 1m, Barcode = "", Description = "contains widget in text" },
                new Product { Name = "NoMatch", WarehouseId = 1, CategoryId = category.Id, Price = 1m, Barcode = "", Description = "" },
                new Product { Name = "Widget OtherWarehouse", WarehouseId = 2, CategoryId = category.Id, Price = 1m, Barcode = "", Description = "" });
            await db.SaveChangesAsync();
        }

        var results = (await sut.SearchAsync("WIDGET", 1)).ToList();

        results.Select(p => p.Name).Should().ContainInOrder("Apple Gadget", "Something", "Zebra Widget");
    }
}
