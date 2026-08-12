using LagersystemLVHome.Data;
using LagersystemLVHome.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.UnitTests.Data.Repositories;

public class StorageLocationRepositoryTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
        public Task<InventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    private static (StorageLocationRepository sut, IDbContextFactory<InventoryDbContext> factory) CreateSut(string dbName)
    {
        var factory = new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(dbName).Options);
        return (new StorageLocationRepository(factory), factory);
    }

    [Fact]
    public async Task GetAllAsync_FiltersByWarehouseAndOrdersByRoomAisleRackShelf()
    {
        var (sut, factory) = CreateSut(nameof(GetAllAsync_FiltersByWarehouseAndOrdersByRoomAisleRackShelf));
        await using (var db = factory.CreateDbContext())
        {
            db.StorageLocations.AddRange(
                new StorageLocation { Code = "B", Name = "B", WarehouseId = 1, Room = "Z", Aisle = "A", Rack = "1", Shelf = "1" },
                new StorageLocation { Code = "A", Name = "A", WarehouseId = 1, Room = "A", Aisle = "A", Rack = "1", Shelf = "1" },
                new StorageLocation { Code = "C", Name = "C", WarehouseId = 2, Room = "A", Aisle = "A", Rack = "1", Shelf = "1" });
            await db.SaveChangesAsync();
        }

        var results = (await sut.GetAllAsync(1)).ToList();

        results.Should().HaveCount(2);
        results.Select(r => r.Code).Should().ContainInOrder("A", "B");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenWarehouseDoesNotMatch()
    {
        var (sut, factory) = CreateSut(nameof(GetByIdAsync_ReturnsNull_WhenWarehouseDoesNotMatch));
        int id;
        await using (var db = factory.CreateDbContext())
        {
            var loc = new StorageLocation { Code = "A", Name = "A", WarehouseId = 1 };
            db.StorageLocations.Add(loc);
            await db.SaveChangesAsync();
            id = loc.Id;
        }

        (await sut.GetByIdAsync(id, 1)).Should().NotBeNull();
        (await sut.GetByIdAsync(id, 2)).Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_IncludesProductsAndCategory()
    {
        var (sut, factory) = CreateSut(nameof(GetByIdAsync_IncludesProductsAndCategory));
        int locId;
        await using (var db = factory.CreateDbContext())
        {
            var category = new Category { Name = "Cat", WarehouseId = 1 };
            var product = new Product { Name = "Widget", CategoryId = 0, WarehouseId = 1, Price = 1m, Category = category };
            var loc = new StorageLocation { Code = "A", Name = "A", WarehouseId = 1 };
            db.Categories.Add(category);
            db.StorageLocations.Add(loc);
            db.Products.Add(product);
            await db.SaveChangesAsync();
            db.ProductStorageLocations.Add(new ProductStorageLocation { ProductId = product.Id, StorageLocationId = loc.Id, Quantity = 3 });
            await db.SaveChangesAsync();
            locId = loc.Id;
        }

        var result = await sut.GetByIdAsync(locId, 1);

        result.Should().NotBeNull();
        result!.ProductStorageLocations.Should().ContainSingle();
        result.ProductStorageLocations.First().Product.Category.Should().NotBeNull();
        result.ProductStorageLocations.First().Product.Category!.Name.Should().Be("Cat");
    }

    [Fact]
    public async Task GetByCodeAsync_MatchesExactCodeWithinWarehouse()
    {
        var (sut, factory) = CreateSut(nameof(GetByCodeAsync_MatchesExactCodeWithinWarehouse));
        await using (var db = factory.CreateDbContext())
        {
            db.StorageLocations.AddRange(
                new StorageLocation { Code = "X1", Name = "A", WarehouseId = 1 },
                new StorageLocation { Code = "X1", Name = "B", WarehouseId = 2 });
            await db.SaveChangesAsync();
        }

        var result = await sut.GetByCodeAsync("X1", 2);

        result.Should().NotBeNull();
        result!.Name.Should().Be("B");
    }

    [Fact]
    public async Task GetByCodeAsync_UnknownCode_ReturnsNull()
    {
        var (sut, _) = CreateSut(nameof(GetByCodeAsync_UnknownCode_ReturnsNull));

        (await sut.GetByCodeAsync("nope", 1)).Should().BeNull();
    }

    [Fact]
    public async Task GetByQRCodeAsync_MatchesQrCodeWithinWarehouse()
    {
        var (sut, factory) = CreateSut(nameof(GetByQRCodeAsync_MatchesQrCodeWithinWarehouse));
        await using (var db = factory.CreateDbContext())
        {
            db.StorageLocations.Add(new StorageLocation { Code = "A", Name = "A", WarehouseId = 1, QRCode = "QR-123" });
            await db.SaveChangesAsync();
        }

        (await sut.GetByQRCodeAsync("QR-123", 1)).Should().NotBeNull();
        (await sut.GetByQRCodeAsync("QR-123", 2)).Should().BeNull();
        (await sut.GetByQRCodeAsync("unknown", 1)).Should().BeNull();
    }

    [Fact]
    public async Task GetByAisleAsync_FiltersAndOrdersByRackThenShelf()
    {
        var (sut, factory) = CreateSut(nameof(GetByAisleAsync_FiltersAndOrdersByRackThenShelf));
        await using (var db = factory.CreateDbContext())
        {
            db.StorageLocations.AddRange(
                new StorageLocation { Code = "1", Name = "1", WarehouseId = 1, Aisle = "A", Rack = "2", Shelf = "1" },
                new StorageLocation { Code = "2", Name = "2", WarehouseId = 1, Aisle = "A", Rack = "1", Shelf = "2" },
                new StorageLocation { Code = "3", Name = "3", WarehouseId = 1, Aisle = "A", Rack = "1", Shelf = "1" },
                new StorageLocation { Code = "4", Name = "4", WarehouseId = 1, Aisle = "B", Rack = "1", Shelf = "1" });
            await db.SaveChangesAsync();
        }

        var results = (await sut.GetByAisleAsync("A", 1)).ToList();

        results.Should().HaveCount(3);
        results.Select(r => r.Code).Should().ContainInOrder("3", "2", "1");
    }

    [Fact]
    public async Task GetByRoomAsync_FiltersAndOrdersByAisleRackShelf()
    {
        var (sut, factory) = CreateSut(nameof(GetByRoomAsync_FiltersAndOrdersByAisleRackShelf));
        await using (var db = factory.CreateDbContext())
        {
            db.StorageLocations.AddRange(
                new StorageLocation { Code = "1", Name = "1", WarehouseId = 1, Room = "Hall A", Aisle = "B" },
                new StorageLocation { Code = "2", Name = "2", WarehouseId = 1, Room = "Hall A", Aisle = "A" },
                new StorageLocation { Code = "3", Name = "3", WarehouseId = 1, Room = "Hall B", Aisle = "A" });
            await db.SaveChangesAsync();
        }

        var results = (await sut.GetByRoomAsync("Hall A", 1)).ToList();

        results.Should().HaveCount(2);
        results.Select(r => r.Code).Should().ContainInOrder("2", "1");
    }

    [Fact]
    public async Task GetAllRoomsAsync_ReturnsDistinctSortedNonEmptyRooms()
    {
        var (sut, factory) = CreateSut(nameof(GetAllRoomsAsync_ReturnsDistinctSortedNonEmptyRooms));
        await using (var db = factory.CreateDbContext())
        {
            db.StorageLocations.AddRange(
                new StorageLocation { Code = "1", Name = "1", WarehouseId = 1, Room = "Zeta" },
                new StorageLocation { Code = "2", Name = "2", WarehouseId = 1, Room = "Alpha" },
                new StorageLocation { Code = "3", Name = "3", WarehouseId = 1, Room = "Alpha" },
                new StorageLocation { Code = "4", Name = "4", WarehouseId = 1, Room = null },
                new StorageLocation { Code = "5", Name = "5", WarehouseId = 1, Room = "" },
                new StorageLocation { Code = "6", Name = "6", WarehouseId = 2, Room = "Other" });
            await db.SaveChangesAsync();
        }

        var rooms = (await sut.GetAllRoomsAsync(1)).ToList();

        rooms.Should().Equal("Alpha", "Zeta");
    }

    [Fact]
    public async Task GetProductsByLocationAsync_ReturnsOnlyProductsPlacedThere()
    {
        var (sut, factory) = CreateSut(nameof(GetProductsByLocationAsync_ReturnsOnlyProductsPlacedThere));
        int locId;
        await using (var db = factory.CreateDbContext())
        {
            // GetProductsByLocationAsync includes Product.Category, and Product.CategoryId is a
            // required FK - EF Core's InMemory provider silently excludes rows from Include()
            // results when a required FK doesn't resolve to an existing row, so a real Category
            // is needed here for the products to actually come back.
            var category = new Category { Name = "Cat", WarehouseId = 1 };
            db.Categories.Add(category);
            await db.SaveChangesAsync();

            var loc1 = new StorageLocation { Code = "L1", Name = "L1", WarehouseId = 1 };
            var loc2 = new StorageLocation { Code = "L2", Name = "L2", WarehouseId = 1 };
            var product1 = new Product { Name = "P1", WarehouseId = 1, CategoryId = category.Id, Price = 1m };
            var product2 = new Product { Name = "P2", WarehouseId = 1, CategoryId = category.Id, Price = 1m };
            db.StorageLocations.AddRange(loc1, loc2);
            db.Products.AddRange(product1, product2);
            await db.SaveChangesAsync();
            db.ProductStorageLocations.Add(new ProductStorageLocation { ProductId = product1.Id, StorageLocationId = loc1.Id, Quantity = 1 });
            db.ProductStorageLocations.Add(new ProductStorageLocation { ProductId = product2.Id, StorageLocationId = loc2.Id, Quantity = 1 });
            await db.SaveChangesAsync();
            locId = loc1.Id;
        }

        var products = (await sut.GetProductsByLocationAsync(locId, 1)).ToList();

        products.Should().ContainSingle().Which.Name.Should().Be("P1");
    }

    [Fact]
    public async Task GetProductsByLocationAsync_FiltersByWarehouse()
    {
        var (sut, factory) = CreateSut(nameof(GetProductsByLocationAsync_FiltersByWarehouse));
        int locId;
        await using (var db = factory.CreateDbContext())
        {
            var category = new Category { Name = "Cat", WarehouseId = 2 };
            db.Categories.Add(category);
            await db.SaveChangesAsync();

            var loc = new StorageLocation { Code = "L1", Name = "L1", WarehouseId = 1 };
            var product = new Product { Name = "P1", WarehouseId = 2, CategoryId = category.Id, Price = 1m };
            db.StorageLocations.Add(loc);
            db.Products.Add(product);
            await db.SaveChangesAsync();
            db.ProductStorageLocations.Add(new ProductStorageLocation { ProductId = product.Id, StorageLocationId = loc.Id, Quantity = 1 });
            await db.SaveChangesAsync();
            locId = loc.Id;
        }

        var products = await sut.GetProductsByLocationAsync(locId, 1);

        products.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_SetsTimestampsAndPersists()
    {
        var (sut, factory) = CreateSut(nameof(CreateAsync_SetsTimestampsAndPersists));
        var location = new StorageLocation { Code = "NEW", Name = "New", WarehouseId = 1 };

        var created = await sut.CreateAsync(location);

        created.CreatedAt.Should().NotBe(default);
        created.UpdatedAt.Should().NotBe(default);

        await using var db = factory.CreateDbContext();
        (await db.StorageLocations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesTimestampAndPersistsChanges()
    {
        var (sut, factory) = CreateSut(nameof(UpdateAsync_UpdatesTimestampAndPersistsChanges));
        var location = await sut.CreateAsync(new StorageLocation { Code = "A", Name = "Original", WarehouseId = 1 });
        var originalUpdatedAt = location.UpdatedAt;

        location.Name = "Renamed";
        var updated = await sut.UpdateAsync(location);

        updated.Name.Should().Be("Renamed");
        updated.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);

        await using var db = factory.CreateDbContext();
        (await db.StorageLocations.FindAsync(location.Id))!.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task GenerateQRCodeAsync_SetsQrCodeAndTimestamp()
    {
        var (sut, factory) = CreateSut(nameof(GenerateQRCodeAsync_SetsQrCodeAndTimestamp));
        var location = await sut.CreateAsync(new StorageLocation { Code = "A", Name = "A", WarehouseId = 1 });

        var result = await sut.GenerateQRCodeAsync(location.Id, "QR-CONTENT");

        result.QRCode.Should().Be("QR-CONTENT");
        result.QRCodeGeneratedAt.Should().NotBeNull();

        await using var db = factory.CreateDbContext();
        (await db.StorageLocations.FindAsync(location.Id))!.QRCode.Should().Be("QR-CONTENT");
    }

    [Fact]
    public async Task GenerateQRCodeAsync_UnknownId_Throws()
    {
        var (sut, _) = CreateSut(nameof(GenerateQRCodeAsync_UnknownId_Throws));

        var act = () => sut.GenerateQRCodeAsync(999, "QR");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_RemovesExistingLocation()
    {
        var (sut, factory) = CreateSut(nameof(DeleteAsync_RemovesExistingLocation));
        var location = await sut.CreateAsync(new StorageLocation { Code = "A", Name = "A", WarehouseId = 1 });

        await sut.DeleteAsync(location.Id);

        await using var db = factory.CreateDbContext();
        (await db.StorageLocations.FindAsync(location.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_DoesNotThrow()
    {
        var (sut, _) = CreateSut(nameof(DeleteAsync_UnknownId_DoesNotThrow));

        var act = () => sut.DeleteAsync(999);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CodeExistsAsync_TrueWhenCodeUsedInWarehouse()
    {
        var (sut, factory) = CreateSut(nameof(CodeExistsAsync_TrueWhenCodeUsedInWarehouse));
        await using (var db = factory.CreateDbContext())
        {
            db.StorageLocations.Add(new StorageLocation { Code = "DUP", Name = "A", WarehouseId = 1 });
            await db.SaveChangesAsync();
        }

        (await sut.CodeExistsAsync("DUP", 1)).Should().BeTrue();
        (await sut.CodeExistsAsync("DUP", 2)).Should().BeFalse();
        (await sut.CodeExistsAsync("OTHER", 1)).Should().BeFalse();
    }

    [Fact]
    public async Task CodeExistsAsync_ExcludeId_IgnoresThatRecord()
    {
        var (sut, factory) = CreateSut(nameof(CodeExistsAsync_ExcludeId_IgnoresThatRecord));
        int id;
        await using (var db = factory.CreateDbContext())
        {
            var loc = new StorageLocation { Code = "DUP", Name = "A", WarehouseId = 1 };
            db.StorageLocations.Add(loc);
            await db.SaveChangesAsync();
            id = loc.Id;
        }

        (await sut.CodeExistsAsync("DUP", 1, id)).Should().BeFalse();
        (await sut.CodeExistsAsync("DUP", 1, id + 999)).Should().BeTrue();
    }
}
