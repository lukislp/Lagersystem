using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Inventory;

public class RoomServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static (RoomService sut, IDbContextFactory<InventoryDbContext> factory) CreateSut(string dbName)
    {
        var factory = new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(dbName).Options);
        return (new RoomService(factory, NullLogger<RoomService>.Instance), factory);
    }

    [Fact]
    public async Task CreateRoomAsync_Valid_Succeeds()
    {
        var (sut, _) = CreateSut(nameof(CreateRoomAsync_Valid_Succeeds));

        var r = await sut.CreateRoomAsync(new Room { Name = "Hall A", Code = "HA", WarehouseId = 1 });

        r.IsSuccess.Should().BeTrue();
        r.Value!.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CreateRoomAsync_MissingFields_ReturnsInvalid()
    {
        var (sut, _) = CreateSut(nameof(CreateRoomAsync_MissingFields_ReturnsInvalid));

        var r = await sut.CreateRoomAsync(new Room { Name = "", Code = "", WarehouseId = 1 });

        r.ErrorCode.Should().Be("room.invalid");
    }

    [Fact]
    public async Task CreateRoomAsync_DuplicateCodeInSameWarehouse_Fails()
    {
        var (sut, _) = CreateSut(nameof(CreateRoomAsync_DuplicateCodeInSameWarehouse_Fails));
        await sut.CreateRoomAsync(new Room { Name = "A", Code = "X", WarehouseId = 1 });

        var r = await sut.CreateRoomAsync(new Room { Name = "B", Code = "X", WarehouseId = 1 });

        r.ErrorCode.Should().Be("room.codeexists");
    }

    [Fact]
    public async Task CreateRoomAsync_SameCodeInDifferentWarehouse_Succeeds()
    {
        var (sut, _) = CreateSut(nameof(CreateRoomAsync_SameCodeInDifferentWarehouse_Succeeds));
        await sut.CreateRoomAsync(new Room { Name = "A", Code = "X", WarehouseId = 1 });

        var r = await sut.CreateRoomAsync(new Room { Name = "B", Code = "X", WarehouseId = 2 });

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RoomCodeExistsAsync_BlankCode_ReturnsFalse()
    {
        var (sut, _) = CreateSut(nameof(RoomCodeExistsAsync_BlankCode_ReturnsFalse));
        (await sut.RoomCodeExistsAsync(1, "")).Should().BeFalse();
    }

    [Fact]
    public async Task RoomCodeExistsAsync_ReturnsTrueWhenPresent()
    {
        var (sut, _) = CreateSut(nameof(RoomCodeExistsAsync_ReturnsTrueWhenPresent));
        await sut.CreateRoomAsync(new Room { Name = "A", Code = "Z", WarehouseId = 1 });

        (await sut.RoomCodeExistsAsync(1, "Z")).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateRoomAsync_UpdatesFields()
    {
        var (sut, factory) = CreateSut(nameof(UpdateRoomAsync_UpdatesFields));
        var created = (await sut.CreateRoomAsync(new Room { Name = "A", Code = "X", WarehouseId = 1 })).Value!;

        var r = await sut.UpdateRoomAsync(new Room
        {
            Id = created.Id,
            Name = "A'",
            Code = "X",
            WarehouseId = 1,
            Capacity = 42,
            IsActive = true
        });

        r.IsSuccess.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        (await db.Rooms.FindAsync(created.Id))!.Capacity.Should().Be(42);
    }

    [Fact]
    public async Task UpdateRoomAsync_UnknownId_ReturnsNotFound()
    {
        var (sut, _) = CreateSut(nameof(UpdateRoomAsync_UnknownId_ReturnsNotFound));

        var r = await sut.UpdateRoomAsync(new Room { Id = 999, Name = "x", Code = "y", WarehouseId = 1 });

        r.ErrorCode.Should().Be("room.notfound");
    }

    [Fact]
    public async Task SetRoomActiveAsync_Toggles()
    {
        var (sut, factory) = CreateSut(nameof(SetRoomActiveAsync_Toggles));
        var created = (await sut.CreateRoomAsync(new Room { Name = "A", Code = "X", WarehouseId = 1, IsActive = true })).Value!;

        (await sut.SetRoomActiveAsync(created.Id, false)).IsSuccess.Should().BeTrue();

        await using var db = factory.CreateDbContext();
        (await db.Rooms.FindAsync(created.Id))!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteRoomAsync_RemovesRoom()
    {
        var (sut, factory) = CreateSut(nameof(DeleteRoomAsync_RemovesRoom));
        var created = (await sut.CreateRoomAsync(new Room { Name = "A", Code = "X", WarehouseId = 1 })).Value!;

        (await sut.DeleteRoomAsync(created.Id)).IsSuccess.Should().BeTrue();

        await using var db = factory.CreateDbContext();
        (await db.Rooms.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task GetAdminViewAsync_ReturnsFilteredRoomsForWarehouse()
    {
        var (sut, _) = CreateSut(nameof(GetAdminViewAsync_ReturnsFilteredRoomsForWarehouse));
        await sut.CreateRoomAsync(new Room { Name = "W1-A", Code = "A", WarehouseId = 1 });
        await sut.CreateRoomAsync(new Room { Name = "W1-B", Code = "B", WarehouseId = 1 });
        await sut.CreateRoomAsync(new Room { Name = "W2-A", Code = "A", WarehouseId = 2 });

        var view = await sut.GetAdminViewAsync(1);

        view.Rooms.Should().HaveCount(2);
        view.Rooms.Select(r => r.Code).Should().BeEquivalentTo(["A", "B"]);
    }
}
