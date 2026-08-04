using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Inventory;

public class WarehouseServiceTests
{
    private sealed class NoOpCategorySeeder : CategorySeederService
    {
        public NoOpCategorySeeder() : base(new ServiceCollection().BuildServiceProvider(),
            NullLogger<CategorySeederService>.Instance) { }
        public override Task SeedCategoriesAsync(int? warehouseId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static (WarehouseService sut, IDbContextFactory<InventoryDbContext> factory) CreateSut(string dbName)
    {
        var factory = new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(dbName).Options);
        var sut = new WarehouseService(factory, new NoOpCategorySeeder(),
            NullLogger<WarehouseService>.Instance);
        return (sut, factory);
    }

    [Fact]
    public async Task CreateWarehouseAsync_Valid_Succeeds()
    {
        var (sut, factory) = CreateSut(nameof(CreateWarehouseAsync_Valid_Succeeds));

        var r = await sut.CreateWarehouseAsync(new Warehouse { Name = "A", Code = "A1", IsActive = true, MaxUsers = 5 });

        r.IsSuccess.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        (await db.Warehouses.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateWarehouseAsync_MissingFields_ReturnsInvalid()
    {
        var (sut, _) = CreateSut(nameof(CreateWarehouseAsync_MissingFields_ReturnsInvalid));

        var r = await sut.CreateWarehouseAsync(new Warehouse { Name = "", Code = "" });

        r.IsFailure.Should().BeTrue();
        r.ErrorCode.Should().Be("warehouse.invalid");
    }

    [Fact]
    public async Task CreateWarehouseAsync_DuplicateCode_Fails()
    {
        var (sut, _) = CreateSut(nameof(CreateWarehouseAsync_DuplicateCode_Fails));
        await sut.CreateWarehouseAsync(new Warehouse { Name = "A", Code = "DUP", MaxUsers = 1 });

        var r = await sut.CreateWarehouseAsync(new Warehouse { Name = "B", Code = "DUP", MaxUsers = 1 });

        r.ErrorCode.Should().Be("warehouse.codeexists");
    }

    [Fact]
    public async Task UpdateWarehouseAsync_UpdatesScalarFields()
    {
        var (sut, factory) = CreateSut(nameof(UpdateWarehouseAsync_UpdatesScalarFields));
        var created = await sut.CreateWarehouseAsync(new Warehouse { Name = "A", Code = "A1", MaxUsers = 1, IsActive = true });
        var id = created.Value!.Id;

        var r = await sut.UpdateWarehouseAsync(new Warehouse
        {
            Id = id,
            Name = "A'",
            Code = "A1",
            MaxUsers = 20,
            IsActive = true
        });

        r.IsSuccess.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        (await db.Warehouses.FindAsync(id))!.MaxUsers.Should().Be(20);
    }

    [Fact]
    public async Task UpdateWarehouseAsync_UnknownId_ReturnsNotFound()
    {
        var (sut, _) = CreateSut(nameof(UpdateWarehouseAsync_UnknownId_ReturnsNotFound));

        var r = await sut.UpdateWarehouseAsync(new Warehouse { Id = 999, Name = "x", Code = "y" });

        r.ErrorCode.Should().Be("warehouse.notfound");
    }

    [Fact]
    public async Task SetWarehouseActiveAsync_TogglesFlag()
    {
        var (sut, factory) = CreateSut(nameof(SetWarehouseActiveAsync_TogglesFlag));
        var created = (await sut.CreateWarehouseAsync(new Warehouse { Name = "A", Code = "A1", IsActive = true })).Value!;

        (await sut.SetWarehouseActiveAsync(created.Id, false)).IsSuccess.Should().BeTrue();

        await using var db = factory.CreateDbContext();
        (await db.Warehouses.FindAsync(created.Id))!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteWarehouseAsync_RemovesEntity()
    {
        var (sut, factory) = CreateSut(nameof(DeleteWarehouseAsync_RemovesEntity));
        var created = (await sut.CreateWarehouseAsync(new Warehouse { Name = "A", Code = "A1" })).Value!;

        (await sut.DeleteWarehouseAsync(created.Id)).IsSuccess.Should().BeTrue();

        await using var db = factory.CreateDbContext();
        (await db.Warehouses.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task GetActiveWarehousesAsync_FiltersInactive()
    {
        var (sut, _) = CreateSut(nameof(GetActiveWarehousesAsync_FiltersInactive));
        await sut.CreateWarehouseAsync(new Warehouse { Name = "Active", Code = "A", IsActive = true });
        await sut.CreateWarehouseAsync(new Warehouse { Name = "Inactive", Code = "B", IsActive = false });

        var list = await sut.GetActiveWarehousesAsync();

        list.Should().ContainSingle().Which.Name.Should().Be("Active");
    }

    [Fact]
    public async Task GetAdminViewAsync_SuperAdminSeesAll_AdminSeesOnlyOwn()
    {
        var (sut, _) = CreateSut(nameof(GetAdminViewAsync_SuperAdminSeesAll_AdminSeesOnlyOwn));
        var w1 = (await sut.CreateWarehouseAsync(new Warehouse { Name = "W1", Code = "W1", IsActive = true })).Value!;
        var w2 = (await sut.CreateWarehouseAsync(new Warehouse { Name = "W2", Code = "W2", IsActive = true })).Value!;

        var superView = await sut.GetAdminViewAsync(new User { Role = UserRole.SuperAdmin, WarehouseId = w1.Id });
        superView.Warehouses.Should().HaveCount(2);

        var adminView = await sut.GetAdminViewAsync(new User { Role = UserRole.Admin, WarehouseId = w2.Id });
        adminView.Warehouses.Should().ContainSingle().Which.Id.Should().Be(w2.Id);
    }
}
