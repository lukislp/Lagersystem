using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Auth;

public class UserRegistrationServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static (UserRegistrationService sut, IDbContextFactory<InventoryDbContext> factory, IAuditService audit)
        CreateSut(string dbName)
    {
        var factory = CreateFactory(dbName);
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        var audit = Substitute.For<IAuditService>();

        var sut = new UserRegistrationService(
            factory,
            NullLogger<UserRegistrationService>.Instance,
            httpContextAccessor,
            audit);

        return (sut, factory, audit);
    }

    private static async Task SeedWarehouseAsync(
        IDbContextFactory<InventoryDbContext> factory,
        int id = 1,
        int maxUsers = 5,
        bool isActive = true)
    {
        await using var db = factory.CreateDbContext();
        if (await db.Warehouses.AnyAsync(w => w.Id == id)) return;
        db.Warehouses.Add(new Warehouse
        {
            Id = id,
            Name = "Test Warehouse",
            Code = "TEST",
            MaxUsers = maxUsers,
            IsActive = isActive
        });
        await db.SaveChangesAsync();
    }

    private static async Task<User> SeedUserAsync(
        IDbContextFactory<InventoryDbContext> factory,
        Action<User>? mutate = null)
    {
        await using var db = factory.CreateDbContext();
        var u = new User
        {
            Username = "alice",
            Email = "alice@test.local",
            DisplayName = "Alice",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Correct!1"),
            IsActive = true,
            ApprovalStatus = UserApprovalStatus.Approved,
            WarehouseId = 1,
            Role = UserRole.User
        };
        mutate?.Invoke(u);
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    [Fact]
    public async Task RegisterAsync_HappyPath_CreatesPendingUserAndAudits()
    {
        var (sut, factory, audit) = CreateSut(nameof(RegisterAsync_HappyPath_CreatesPendingUserAndAudits));
        await SeedWarehouseAsync(factory);

        var user = await sut.RegisterAsync("bob", "bob@test.local", "Secret!1", "Bob", 1);

        user.Should().NotBeNull();
        user!.ApprovalStatus.Should().Be(UserApprovalStatus.Pending);
        user.Role.Should().Be(UserRole.User);
        BCrypt.Net.BCrypt.Verify("Secret!1", user.PasswordHash).Should().BeTrue();
        await audit.Received(1).LogAsync(
            "REGISTER_SUCCESS", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Info);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateUsername_ReturnsNull()
    {
        var (sut, factory, audit) = CreateSut(nameof(RegisterAsync_DuplicateUsername_ReturnsNull));
        await SeedWarehouseAsync(factory);
        await SeedUserAsync(factory);

        var result = await sut.RegisterAsync("alice", "different@test.local", "x", "x", 1);

        result.Should().BeNull();
        await audit.Received().LogAsync(
            "REGISTER_FAILED", "User", null, Arg.Any<object?>(), AuditSeverity.Warning);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ReturnsNull()
    {
        var (sut, factory, _) = CreateSut(nameof(RegisterAsync_DuplicateEmail_ReturnsNull));
        await SeedWarehouseAsync(factory);
        await SeedUserAsync(factory);

        var result = await sut.RegisterAsync("other", "alice@test.local", "x", "x", 1);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RegisterAsync_InactiveWarehouse_ReturnsNull()
    {
        var (sut, factory, _) = CreateSut(nameof(RegisterAsync_InactiveWarehouse_ReturnsNull));
        await SeedWarehouseAsync(factory, isActive: false);

        var result = await sut.RegisterAsync("bob", "bob@test.local", "x", "Bob", 1);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RegisterAsync_UnknownWarehouse_ReturnsNull()
    {
        var (sut, _, _) = CreateSut(nameof(RegisterAsync_UnknownWarehouse_ReturnsNull));

        var result = await sut.RegisterAsync("bob", "bob@test.local", "x", "Bob", 999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RegisterAsync_QuotaReached_ReturnsNull()
    {
        var (sut, factory, _) = CreateSut(nameof(RegisterAsync_QuotaReached_ReturnsNull));
        await SeedWarehouseAsync(factory, maxUsers: 1);
        await SeedUserAsync(factory);

        var result = await sut.RegisterAsync("bob", "bob@test.local", "x", "Bob", 1);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPendingUsersAsync_ReturnsOnlyPendingInWarehouse()
    {
        var (sut, factory, _) = CreateSut(nameof(GetPendingUsersAsync_ReturnsOnlyPendingInWarehouse));
        await SeedWarehouseAsync(factory);
        await SeedWarehouseAsync(factory, id: 2);
        await SeedUserAsync(factory, u =>
        {
            u.Username = "p1";
            u.Email = "p1@t.local";
            u.ApprovalStatus = UserApprovalStatus.Pending;
        });
        await SeedUserAsync(factory, u =>
        {
            u.Username = "p2";
            u.Email = "p2@t.local";
            u.ApprovalStatus = UserApprovalStatus.Approved;
        });
        await SeedUserAsync(factory, u =>
        {
            u.Username = "p3";
            u.Email = "p3@t.local";
            u.WarehouseId = 2;
            u.ApprovalStatus = UserApprovalStatus.Pending;
        });

        var pending = await sut.GetPendingUsersAsync(1);

        pending.Should().ContainSingle().Which.Username.Should().Be("p1");
    }

    [Fact]
    public async Task ApproveUserAsync_WhenPending_ApprovesAndAudits()
    {
        var (sut, factory, audit) = CreateSut(nameof(ApproveUserAsync_WhenPending_ApprovesAndAudits));
        await SeedWarehouseAsync(factory);
        var user = await SeedUserAsync(factory, u => u.ApprovalStatus = UserApprovalStatus.Pending);

        var ok = await sut.ApproveUserAsync(user.Id, approvedByUserId: 42, notes: "welcome");

        ok.Should().BeTrue();
        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(user.Id))!;
        refreshed.ApprovalStatus.Should().Be(UserApprovalStatus.Approved);
        refreshed.ApprovedByUserId.Should().Be(42);
        refreshed.ApprovalNotes.Should().Be("welcome");
        await audit.Received(1).LogAsync(
            "USER_APPROVED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Info);
    }

    [Fact]
    public async Task ApproveUserAsync_WhenNotPending_ReturnsFalse()
    {
        var (sut, factory, _) = CreateSut(nameof(ApproveUserAsync_WhenNotPending_ReturnsFalse));
        await SeedWarehouseAsync(factory);
        var user = await SeedUserAsync(factory); // Approved

        var ok = await sut.ApproveUserAsync(user.Id, 1);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task RejectUserAsync_WhenPending_RejectsDeactivatesAndAudits()
    {
        var (sut, factory, audit) = CreateSut(nameof(RejectUserAsync_WhenPending_RejectsDeactivatesAndAudits));
        await SeedWarehouseAsync(factory);
        var user = await SeedUserAsync(factory, u => u.ApprovalStatus = UserApprovalStatus.Pending);

        var ok = await sut.RejectUserAsync(user.Id, rejectedByUserId: 7, notes: "spam");

        ok.Should().BeTrue();
        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(user.Id))!;
        refreshed.ApprovalStatus.Should().Be(UserApprovalStatus.Rejected);
        refreshed.IsActive.Should().BeFalse();
        await audit.Received(1).LogAsync(
            "USER_REJECTED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Warning);
    }

    [Fact]
    public async Task ChangeUserRoleAsync_DeniesAssigningSuperAdmin()
    {
        var (sut, factory, _) = CreateSut(nameof(ChangeUserRoleAsync_DeniesAssigningSuperAdmin));
        await SeedWarehouseAsync(factory);
        var target = await SeedUserAsync(factory);
        var admin = await SeedUserAsync(factory, u =>
        {
            u.Username = "root";
            u.Email = "root@t.local";
            u.Role = UserRole.SuperAdmin;
        });

        var ok = await sut.ChangeUserRoleAsync(target.Id, UserRole.SuperAdmin, admin.Id);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ChangeUserRoleAsync_DeniesChangingSuperAdmin()
    {
        var (sut, factory, _) = CreateSut(nameof(ChangeUserRoleAsync_DeniesChangingSuperAdmin));
        await SeedWarehouseAsync(factory);
        var target = await SeedUserAsync(factory, u => u.Role = UserRole.SuperAdmin);
        var admin = await SeedUserAsync(factory, u =>
        {
            u.Username = "root";
            u.Email = "root@t.local";
            u.Role = UserRole.SuperAdmin;
        });

        var ok = await sut.ChangeUserRoleAsync(target.Id, UserRole.User, admin.Id);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ChangeUserRoleAsync_AdminCanPromoteBelowAdmin()
    {
        var (sut, factory, audit) = CreateSut(nameof(ChangeUserRoleAsync_AdminCanPromoteBelowAdmin));
        await SeedWarehouseAsync(factory);
        var target = await SeedUserAsync(factory, u => u.Role = UserRole.User);
        var admin = await SeedUserAsync(factory, u =>
        {
            u.Username = "adm";
            u.Email = "adm@t.local";
            u.Role = UserRole.Admin;
        });

        var ok = await sut.ChangeUserRoleAsync(target.Id, UserRole.User, admin.Id);

        ok.Should().BeTrue();
        await audit.Received(1).LogAsync(
            "ROLE_CHANGED", "User", target.Id, Arg.Any<object?>(), AuditSeverity.Info);
    }

    [Fact]
    public async Task ChangeUserRoleAsync_AdminCannotAssignAdminRole()
    {
        var (sut, factory, _) = CreateSut(nameof(ChangeUserRoleAsync_AdminCannotAssignAdminRole));
        await SeedWarehouseAsync(factory);
        var target = await SeedUserAsync(factory, u => u.Role = UserRole.User);
        var admin = await SeedUserAsync(factory, u =>
        {
            u.Username = "adm";
            u.Email = "adm@t.local";
            u.Role = UserRole.Admin;
        });

        var ok = await sut.ChangeUserRoleAsync(target.Id, UserRole.Admin, admin.Id);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ChangeUserRoleAsync_BelowAdminCannotChangeRoles()
    {
        var (sut, factory, _) = CreateSut(nameof(ChangeUserRoleAsync_BelowAdminCannotChangeRoles));
        await SeedWarehouseAsync(factory);
        var target = await SeedUserAsync(factory);
        var regular = await SeedUserAsync(factory, u =>
        {
            u.Username = "joe";
            u.Email = "joe@t.local";
            u.Role = UserRole.User;
        });

        var ok = await sut.ChangeUserRoleAsync(target.Id, UserRole.User, regular.Id);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ChangeUserRoleAsync_UnknownUser_ReturnsFalse()
    {
        var (sut, factory, _) = CreateSut(nameof(ChangeUserRoleAsync_UnknownUser_ReturnsFalse));
        await SeedWarehouseAsync(factory);

        var ok = await sut.ChangeUserRoleAsync(999, UserRole.User, 888);

        ok.Should().BeFalse();
    }
}
