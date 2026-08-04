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
}
