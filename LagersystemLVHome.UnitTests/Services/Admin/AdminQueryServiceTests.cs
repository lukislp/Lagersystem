using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.UnitTests.Services.Admin;

public class AdminQueryServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task SeedWarehouseAsync(IDbContextFactory<InventoryDbContext> factory, int id = 1)
    {
        await using var db = factory.CreateDbContext();
        if (!await db.Warehouses.AnyAsync(w => w.Id == id))
        {
            db.Warehouses.Add(new Warehouse
            {
                Id = id,
                Name = $"WH{id}",
                Address = "a",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }
    }

    private static User MakeUser(
        int id,
        int warehouseId = 1,
        UserApprovalStatus status = UserApprovalStatus.Approved,
        UserRole role = UserRole.User,
        bool isActive = true,
        bool isDeleted = false) => new()
        {
            Id = id,
            Username = $"u{id}",
            Email = $"u{id}@x.local",
            DisplayName = $"User {id}",
            PasswordHash = "x",
            WarehouseId = warehouseId,
            ApprovalStatus = status,
            Role = role,
            IsActive = isActive,
            IsDeleted = isDeleted
        };

    [Fact]
    public async Task GetDashboardStatsAsync_ReturnsAggregatedCounts()
    {
        var factory = CreateFactory(nameof(GetDashboardStatsAsync_ReturnsAggregatedCounts));
        await SeedWarehouseAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(new Warehouse { Id = 2, Name = "WH2", Address = "b", IsActive = false });
            db.Users.AddRange(
                MakeUser(1, status: UserApprovalStatus.Approved),
                MakeUser(2, status: UserApprovalStatus.Pending),
                MakeUser(3, status: UserApprovalStatus.Pending));
            db.Products.Add(new Product { Name = "p", WarehouseId = 1 });
            db.StockMovements.Add(new StockMovement { WarehouseId = 1, QuantityChange = 1 });
            await db.SaveChangesAsync();
        }

        var sut = new AdminQueryService(factory);

        var stats = await sut.GetDashboardStatsAsync(warehouseId: 1);

        stats.TotalUsers.Should().Be(3);
        stats.PendingUsers.Should().Be(2);
        stats.TotalWarehouses.Should().Be(2);
        stats.ActiveWarehouses.Should().Be(1);
        stats.TotalProducts.Should().Be(1);
        stats.TotalMovements.Should().Be(1);
    }

    [Fact]
    public async Task GetUsersByApprovalStatusAsync_GroupsByStatusAndFiltersByWarehouse()
    {
        var factory = CreateFactory(nameof(GetUsersByApprovalStatusAsync_GroupsByStatusAndFiltersByWarehouse));
        await SeedWarehouseAsync(factory, 1);
        await SeedWarehouseAsync(factory, 2);
        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(
                MakeUser(1, 1, UserApprovalStatus.Pending),
                MakeUser(2, 1, UserApprovalStatus.Approved),
                MakeUser(3, 1, UserApprovalStatus.Rejected),
                MakeUser(4, 2, UserApprovalStatus.Pending));
            await db.SaveChangesAsync();
        }

        var sut = new AdminQueryService(factory);

        var result = await sut.GetUsersByApprovalStatusAsync(warehouseId: 1);

        result.Pending.Should().ContainSingle().Which.Id.Should().Be(1);
        result.Approved.Should().ContainSingle().Which.Id.Should().Be(2);
        result.Rejected.Should().ContainSingle().Which.Id.Should().Be(3);
    }

    [Fact]
    public async Task GetSuperAdminEmailAsync_ReturnsNullWhenNoSuperAdmin()
    {
        var factory = CreateFactory(nameof(GetSuperAdminEmailAsync_ReturnsNullWhenNoSuperAdmin));
        await SeedWarehouseAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1, role: UserRole.Admin));
            await db.SaveChangesAsync();
        }

        var sut = new AdminQueryService(factory);

        (await sut.GetSuperAdminEmailAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetSuperAdminEmailAsync_ReturnsEmailOfActiveSuperAdmin()
    {
        var factory = CreateFactory(nameof(GetSuperAdminEmailAsync_ReturnsEmailOfActiveSuperAdmin));
        await SeedWarehouseAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1, role: UserRole.SuperAdmin, isActive: false));
            db.Users.Add(MakeUser(2, role: UserRole.SuperAdmin, isDeleted: true));
            db.Users.Add(MakeUser(3, role: UserRole.SuperAdmin));
            await db.SaveChangesAsync();
        }

        var sut = new AdminQueryService(factory);

        (await sut.GetSuperAdminEmailAsync()).Should().Be("u3@x.local");
    }

    [Fact]
    public async Task GetActiveUsersWithWarehouseAsync_NullWarehouseId_ReturnsAllActive()
    {
        var factory = CreateFactory(nameof(GetActiveUsersWithWarehouseAsync_NullWarehouseId_ReturnsAllActive));
        await SeedWarehouseAsync(factory, 1);
        await SeedWarehouseAsync(factory, 2);
        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(
                MakeUser(1, 1),
                MakeUser(2, 2),
                MakeUser(3, 1, isActive: false));
            await db.SaveChangesAsync();
        }

        var sut = new AdminQueryService(factory);

        var users = await sut.GetActiveUsersWithWarehouseAsync(null);

        users.Should().HaveCount(2);
        users.Select(u => u.Id).Should().BeEquivalentTo(new[] { 1, 2 });
        users.All(u => u.Warehouse is not null).Should().BeTrue();
    }

    [Fact]
    public async Task GetActiveUsersWithWarehouseAsync_FiltersByWarehouseId()
    {
        var factory = CreateFactory(nameof(GetActiveUsersWithWarehouseAsync_FiltersByWarehouseId));
        await SeedWarehouseAsync(factory, 1);
        await SeedWarehouseAsync(factory, 2);
        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(MakeUser(1, 1), MakeUser(2, 2));
            await db.SaveChangesAsync();
        }

        var sut = new AdminQueryService(factory);

        var users = await sut.GetActiveUsersWithWarehouseAsync(2);

        users.Should().ContainSingle().Which.Id.Should().Be(2);
    }
}
