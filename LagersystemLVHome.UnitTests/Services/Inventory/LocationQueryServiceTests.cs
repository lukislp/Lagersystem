using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.UnitTests.Services.Inventory;

public class LocationQueryServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static (LocationQueryService sut, IDbContextFactory<InventoryDbContext> factory) CreateSut(string dbName)
    {
        var factory = new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(dbName).Options);
        return (new LocationQueryService(factory), factory);
    }

    [Fact]
    public async Task GetActiveRoomsForWarehouseAsync_FiltersInactive()
    {
        var (sut, factory) = CreateSut(nameof(GetActiveRoomsForWarehouseAsync_FiltersInactive));
        await using (var db = factory.CreateDbContext())
        {
            db.Rooms.AddRange(
                new Room { Name = "A", Code = "A", WarehouseId = 1, IsActive = true },
                new Room { Name = "B", Code = "B", WarehouseId = 1, IsActive = false },
                new Room { Name = "C", Code = "C", WarehouseId = 2, IsActive = true });
            await db.SaveChangesAsync();
        }

        var rooms = await sut.GetActiveRoomsForWarehouseAsync(1);

        rooms.Should().ContainSingle().Which.Code.Should().Be("A");
    }

    [Fact]
    public async Task FindActiveStorageLocationByCodeAsync_BlankCode_ReturnsNull()
    {
        var (sut, _) = CreateSut(nameof(FindActiveStorageLocationByCodeAsync_BlankCode_ReturnsNull));

        (await sut.FindActiveStorageLocationByCodeAsync(1, "")).Should().BeNull();
    }

    [Fact]
    public async Task FindActiveStorageLocationByCodeAsync_CaseInsensitive()
    {
        var (sut, factory) = CreateSut(nameof(FindActiveStorageLocationByCodeAsync_CaseInsensitive));
        await using (var db = factory.CreateDbContext())
        {
            db.StorageLocations.Add(new StorageLocation
            {
                Code = "ABC-1",
                Name = "L",
                WarehouseId = 1,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        (await sut.FindActiveStorageLocationByCodeAsync(1, "abc-1")).Should().NotBeNull();
    }

    [Fact]
    public async Task FindActiveStorageLocationByCodeAsync_IgnoresInactive()
    {
        var (sut, factory) = CreateSut(nameof(FindActiveStorageLocationByCodeAsync_IgnoresInactive));
        await using (var db = factory.CreateDbContext())
        {
            db.StorageLocations.Add(new StorageLocation
            {
                Code = "X",
                Name = "L",
                WarehouseId = 1,
                IsActive = false
            });
            await db.SaveChangesAsync();
        }

        (await sut.FindActiveStorageLocationByCodeAsync(1, "X")).Should().BeNull();
    }

    [Fact]
    public async Task GetActiveStorageLocationsAsync_ReturnsAllActive()
    {
        var (sut, factory) = CreateSut(nameof(GetActiveStorageLocationsAsync_ReturnsAllActive));
        await using (var db = factory.CreateDbContext())
        {
            db.StorageLocations.AddRange(
                new StorageLocation { Code = "A", Name = "A", WarehouseId = 1, IsActive = true },
                new StorageLocation { Code = "B", Name = "B", WarehouseId = 2, IsActive = true },
                new StorageLocation { Code = "C", Name = "C", WarehouseId = 1, IsActive = false });
            await db.SaveChangesAsync();
        }

        var list = await sut.GetActiveStorageLocationsAsync();

        list.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetActiveStorageLocationsForWarehouseAsync_FiltersByWarehouse()
    {
        var (sut, factory) = CreateSut(nameof(GetActiveStorageLocationsForWarehouseAsync_FiltersByWarehouse));
        await using (var db = factory.CreateDbContext())
        {
            db.StorageLocations.AddRange(
                new StorageLocation { Code = "A", Name = "A", WarehouseId = 1, IsActive = true },
                new StorageLocation { Code = "B", Name = "B", WarehouseId = 2, IsActive = true });
            await db.SaveChangesAsync();
        }

        var list = await sut.GetActiveStorageLocationsForWarehouseAsync(1);

        list.Should().ContainSingle().Which.Code.Should().Be("A");
    }

    [Fact]
    public async Task GetRoomIdsByNameAsync_EmptyInput_ReturnsEmpty()
    {
        var (sut, _) = CreateSut(nameof(GetRoomIdsByNameAsync_EmptyInput_ReturnsEmpty));

        (await sut.GetRoomIdsByNameAsync([])).Should().BeEmpty();
    }

    [Fact]
    public async Task GetRoomIdsByNameAsync_ReturnsMatchingRoomIds()
    {
        var (sut, factory) = CreateSut(nameof(GetRoomIdsByNameAsync_ReturnsMatchingRoomIds));
        await using (var db = factory.CreateDbContext())
        {
            db.Rooms.AddRange(
                new Room { Name = "Hall A", Code = "HA", WarehouseId = 1, IsActive = true },
                new Room { Name = "Hall B", Code = "HB", WarehouseId = 1, IsActive = true });
            await db.SaveChangesAsync();
        }

        var map = await sut.GetRoomIdsByNameAsync(["Hall A", "Nope"]);

        map.Should().ContainKey("Hall A");
        map.Should().NotContainKey("Nope");
    }

    [Fact]
    public async Task GetRoomContentsAsync_UnknownRoom_ReturnsNull()
    {
        var (sut, _) = CreateSut(nameof(GetRoomContentsAsync_UnknownRoom_ReturnsNull));

        (await sut.GetRoomContentsAsync(999, 1)).Should().BeNull();
    }

    [Fact]
    public async Task GetStorageOverviewAsync_EmptyWarehouse_ReturnsEmptyLocations()
    {
        var (sut, _) = CreateSut(nameof(GetStorageOverviewAsync_EmptyWarehouse_ReturnsEmptyLocations));

        var data = await sut.GetStorageOverviewAsync(1);

        data.Locations.Should().BeEmpty();
        data.AvailableRooms.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStorageOverviewAsync_ComputesPerLocationStatsAndOrdersByQuantityDescending()
    {
        var (sut, factory) = CreateSut(nameof(GetStorageOverviewAsync_ComputesPerLocationStatsAndOrdersByQuantityDescending));
        await using (var db = factory.CreateDbContext())
        {
            var loc1 = new StorageLocation { Code = "L1", Name = "L1", WarehouseId = 1, IsActive = true };
            var loc2 = new StorageLocation { Code = "L2", Name = "L2", WarehouseId = 1, IsActive = true };
            var inactive = new StorageLocation { Code = "L3", Name = "L3", WarehouseId = 1, IsActive = false };
            db.StorageLocations.AddRange(loc1, loc2, inactive);
            db.Rooms.Add(new Room { Name = "Hall", Code = "H", WarehouseId = 1 });
            await db.SaveChangesAsync();

            db.ProductStorageLocations.AddRange(
                new ProductStorageLocation { ProductId = 1, StorageLocationId = loc1.Id, Quantity = 3 },
                new ProductStorageLocation { ProductId = 2, StorageLocationId = loc1.Id, Quantity = 4 },
                new ProductStorageLocation { ProductId = 1, StorageLocationId = loc2.Id, Quantity = 1 });
            await db.SaveChangesAsync();
        }

        var data = await sut.GetStorageOverviewAsync(1);

        data.Locations.Should().HaveCount(2);
        data.AvailableRooms.Should().ContainSingle();
        var first = data.Locations.First();
        first.Location.Code.Should().Be("L1");
        first.DistinctProductCount.Should().Be(2);
        first.TotalQuantity.Should().Be(7);
    }

    [Fact]
    public async Task GetProductsAtLocationAsync_ReturnsProductsAndQuantities()
    {
        var (sut, factory) = CreateSut(nameof(GetProductsAtLocationAsync_ReturnsProductsAndQuantities));
        int locId;
        await using (var db = factory.CreateDbContext())
        {
            var category = new Category { Name = "Cat", WarehouseId = 1 };
            db.Categories.Add(category);
            var loc = new StorageLocation { Code = "L1", Name = "L1", WarehouseId = 1 };
            db.StorageLocations.Add(loc);
            var product = new Product { Name = "P1", WarehouseId = 1, CategoryId = 0, Category = category, Price = 1m };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            db.ProductStorageLocations.Add(new ProductStorageLocation { ProductId = product.Id, StorageLocationId = loc.Id, Quantity = 8 });
            await db.SaveChangesAsync();
            locId = loc.Id;
        }

        var contents = await sut.GetProductsAtLocationAsync(locId);

        contents.Products.Should().ContainSingle().Which.Name.Should().Be("P1");
        contents.Products.Single().Category.Should().NotBeNull();
        contents.QuantityByProductId.Values.Should().ContainSingle().Which.Should().Be(8);
    }

    [Fact]
    public async Task GetProductsAtLocationAsync_UnknownLocation_ReturnsEmpty()
    {
        var (sut, _) = CreateSut(nameof(GetProductsAtLocationAsync_UnknownLocation_ReturnsEmpty));

        var contents = await sut.GetProductsAtLocationAsync(999);

        contents.Products.Should().BeEmpty();
        contents.QuantityByProductId.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRoomsWithStatsAsync_NoActiveRooms_ReturnsEmpty()
    {
        var (sut, _) = CreateSut(nameof(GetRoomsWithStatsAsync_NoActiveRooms_ReturnsEmpty));

        var stats = await sut.GetRoomsWithStatsAsync(1);

        stats.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRoomsWithStatsAsync_ComputesStatsPerRoom_OrderedByDistinctProductCountDescending()
    {
        var (sut, factory) = CreateSut(nameof(GetRoomsWithStatsAsync_ComputesStatsPerRoom_OrderedByDistinctProductCountDescending));
        await using (var db = factory.CreateDbContext())
        {
            db.Rooms.AddRange(
                new Room { Name = "Busy", Code = "B", WarehouseId = 1, IsActive = true },
                new Room { Name = "Quiet", Code = "Q", WarehouseId = 1, IsActive = true },
                new Room { Name = "Inactive", Code = "I", WarehouseId = 1, IsActive = false });

            var busyLoc = new StorageLocation { Code = "BL1", Name = "BL1", WarehouseId = 1, Room = "Busy" };
            var quietLoc = new StorageLocation { Code = "QL1", Name = "QL1", WarehouseId = 1, Room = "Quiet" };
            db.StorageLocations.AddRange(busyLoc, quietLoc);
            await db.SaveChangesAsync();

            db.ProductStorageLocations.AddRange(
                new ProductStorageLocation { ProductId = 1, StorageLocationId = busyLoc.Id, Quantity = 2 },
                new ProductStorageLocation { ProductId = 2, StorageLocationId = busyLoc.Id, Quantity = 3 },
                new ProductStorageLocation { ProductId = 1, StorageLocationId = quietLoc.Id, Quantity = 1 });
            await db.SaveChangesAsync();
        }

        var stats = await sut.GetRoomsWithStatsAsync(1);

        stats.Should().HaveCount(2);
        stats[0].Room.Name.Should().Be("Busy");
        stats[0].DistinctProductCount.Should().Be(2);
        stats[0].TotalQuantity.Should().Be(5);
        stats[1].Room.Name.Should().Be("Quiet");
        stats[1].DistinctProductCount.Should().Be(1);
    }

    [Fact]
    public async Task GetRoomContentsAsync_KnownRoom_ReturnsRoomLocationsAndPlacements()
    {
        var (sut, factory) = CreateSut(nameof(GetRoomContentsAsync_KnownRoom_ReturnsRoomLocationsAndPlacements));
        int roomId;
        await using (var db = factory.CreateDbContext())
        {
            var room = new Room { Name = "Hall A", Code = "HA", WarehouseId = 1 };
            db.Rooms.Add(room);
            var category = new Category { Name = "Cat", WarehouseId = 1 };
            db.Categories.Add(category);
            var loc = new StorageLocation { Code = "L1", Name = "L1", WarehouseId = 1, Room = "Hall A" };
            var otherRoomLoc = new StorageLocation { Code = "L2", Name = "L2", WarehouseId = 1, Room = "Hall B" };
            db.StorageLocations.AddRange(loc, otherRoomLoc);
            var product = new Product { Name = "P1", WarehouseId = 1, CategoryId = 0, Category = category, Price = 1m };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            db.ProductStorageLocations.Add(new ProductStorageLocation { ProductId = product.Id, StorageLocationId = loc.Id, Quantity = 4 });
            await db.SaveChangesAsync();
            roomId = room.Id;
        }

        var contents = await sut.GetRoomContentsAsync(roomId, 1);

        contents.Should().NotBeNull();
        contents!.Room.Name.Should().Be("Hall A");
        contents.StorageLocations.Should().ContainSingle().Which.Code.Should().Be("L1");
        contents.ProductPlacements.Should().ContainSingle();
        contents.ProductPlacements.First().Product!.Category.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRoomContentsAsync_WrongWarehouse_ReturnsNull()
    {
        var (sut, factory) = CreateSut(nameof(GetRoomContentsAsync_WrongWarehouse_ReturnsNull));
        int roomId;
        await using (var db = factory.CreateDbContext())
        {
            var room = new Room { Name = "Hall A", Code = "HA", WarehouseId = 1 };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
            roomId = room.Id;
        }

        (await sut.GetRoomContentsAsync(roomId, 2)).Should().BeNull();
    }
}
