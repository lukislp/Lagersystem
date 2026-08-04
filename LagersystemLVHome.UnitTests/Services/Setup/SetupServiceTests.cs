using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Setup;

/// <summary>
/// <see cref="SetupService"/> bootstraps the first warehouse and the
/// SuperAdmin user. We stub <see cref="CategorySeederService"/> with a
/// no-op subclass to avoid dragging its IServiceProvider dependency into
/// these tests.
/// </summary>
public class SetupServiceTests
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

    private static (SetupService sut, IDbContextFactory<InventoryDbContext> factory)
        CreateSut(string dbName)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(dbName).Options;
        var factory = new InMemoryContextFactory(options);
        var sut = new SetupService(factory, new NoOpCategorySeeder(),
            NullLogger<SetupService>.Instance);
        return (sut, factory);
    }

    private static InitialSetupRequest ValidRequest() => new(
        WarehouseName: "Main",
        WarehouseCode: "MAIN",
        WarehouseAddress: "Somewhere 1",
        MaxUsers: 10,
        Username: "root",
        Email: "root@test.local",
        DisplayName: "Root",
        Password: "StrongPass1!");

    [Fact]
    public async Task IsInitialSetupCompletedAsync_WithEmptyDb_ReturnsFalse()
    {
        var (sut, _) = CreateSut(nameof(IsInitialSetupCompletedAsync_WithEmptyDb_ReturnsFalse));

        (await sut.IsInitialSetupCompletedAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task IsInitialSetupCompletedAsync_WithUsers_ReturnsTrue()
    {
        var (sut, factory) = CreateSut(nameof(IsInitialSetupCompletedAsync_WithUsers_ReturnsTrue));

        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(new Warehouse { Id = 1, Name = "X", Code = "X", IsActive = true });
            db.Users.Add(new User
            {
                Username = "existing", Email = "e@e", DisplayName = "E",
                PasswordHash = "-", WarehouseId = 1,
                ApprovalStatus = UserApprovalStatus.Approved, IsActive = true
            });
            await db.SaveChangesAsync();
        }

        (await sut.IsInitialSetupCompletedAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task CompleteInitialSetupAsync_OnEmptyDb_CreatesWarehouseAndSuperAdmin()
    {
        var (sut, factory) = CreateSut(nameof(CompleteInitialSetupAsync_OnEmptyDb_CreatesWarehouseAndSuperAdmin));

        var result = await sut.CompleteInitialSetupAsync(ValidRequest());

        result.IsSuccess.Should().BeTrue(
            $"expected success but got code='{result.ErrorCode}', message='{result.ErrorMessage}'");

        await using var db = factory.CreateDbContext();
        var warehouse = await db.Warehouses.SingleAsync();
        warehouse.Name.Should().Be("Main");
        warehouse.Code.Should().Be("MAIN");
        warehouse.MaxUsers.Should().Be(10);
        warehouse.IsActive.Should().BeTrue();

        var user = await db.Users.SingleAsync();
        user.Username.Should().Be("root");
        user.Role.Should().Be(UserRole.SuperAdmin);
        user.ApprovalStatus.Should().Be(UserApprovalStatus.Approved);
        user.WarehouseId.Should().Be(warehouse.Id);
        BCrypt.Net.BCrypt.Verify("StrongPass1!", user.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task CompleteInitialSetupAsync_WhenAlreadySetUp_ReturnsAlreadyComplete()
    {
        var (sut, factory) = CreateSut(nameof(CompleteInitialSetupAsync_WhenAlreadySetUp_ReturnsAlreadyComplete));

        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(new Warehouse { Id = 1, Name = "X", Code = "X", IsActive = true });
            db.Users.Add(new User
            {
                Username = "existing", Email = "e@e", DisplayName = "E",
                PasswordHash = "-", WarehouseId = 1,
                ApprovalStatus = UserApprovalStatus.Approved, IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var result = await sut.CompleteInitialSetupAsync(ValidRequest());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("setup.alreadycomplete");
    }
}
