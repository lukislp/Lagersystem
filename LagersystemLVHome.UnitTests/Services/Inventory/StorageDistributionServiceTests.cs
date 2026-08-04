using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.UnitTests.Services.Inventory;

public class StorageDistributionServiceTests
{
    private static InventoryDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    [Fact]
    public async Task GetDistributionDataAsync_ReturnsActiveLocationsOrderedByCode()
    {
        await using var db = CreateDb(nameof(GetDistributionDataAsync_ReturnsActiveLocationsOrderedByCode));
        db.StorageLocations.AddRange(
            new StorageLocation { Code = "B", Name = "B", WarehouseId = 1, IsActive = true },
            new StorageLocation { Code = "A", Name = "A", WarehouseId = 1, IsActive = true },
            new StorageLocation { Code = "C", Name = "C", WarehouseId = 1, IsActive = false });
        await db.SaveChangesAsync();

        var sut = new StorageDistributionService(db);
        var data = await sut.GetDistributionDataAsync(0, 1);

        data.Locations.Select(l => l.Code).Should().ContainInOrder("A", "B");
        data.ExistingAssignments.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDistributionDataAsync_WithProductId_LoadsAssignments()
    {
        await using var db = CreateDb(nameof(GetDistributionDataAsync_WithProductId_LoadsAssignments));
        var loc = new StorageLocation { Code = "A", Name = "A", WarehouseId = 1, IsActive = true };
        db.StorageLocations.Add(loc);
        await db.SaveChangesAsync();
        db.ProductStorageLocations.Add(new ProductStorageLocation
        {
            ProductId = 42,
            StorageLocationId = loc.Id,
            Quantity = 7
        });
        await db.SaveChangesAsync();

        var sut = new StorageDistributionService(db);
        var data = await sut.GetDistributionDataAsync(42, 1);

        data.ExistingAssignments.Should().ContainKey(loc.Id).WhoseValue.Should().Be(7);
    }

    [Fact]
    public async Task GetDistributionDataAsync_FiltersByWarehouse()
    {
        await using var db = CreateDb(nameof(GetDistributionDataAsync_FiltersByWarehouse));
        db.StorageLocations.AddRange(
            new StorageLocation { Code = "W1", Name = "W1", WarehouseId = 1, IsActive = true },
            new StorageLocation { Code = "W2", Name = "W2", WarehouseId = 2, IsActive = true });
        await db.SaveChangesAsync();

        var sut = new StorageDistributionService(db);
        var data = await sut.GetDistributionDataAsync(0, 2);

        data.Locations.Should().ContainSingle().Which.Code.Should().Be("W2");
    }
}
