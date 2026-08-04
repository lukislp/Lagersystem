using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Auth;

public class UserProfileServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static (UserProfileService sut, IDbContextFactory<InventoryDbContext> factory) CreateSut(string dbName)
    {
        var factory = CreateFactory(dbName);
        var sut = new UserProfileService(factory, NullLogger<UserProfileService>.Instance);
        return (sut, factory);
    }

    private static async Task SeedWarehouseAsync(IDbContextFactory<InventoryDbContext> factory, int id = 1)
    {
        await using var db = factory.CreateDbContext();
        if (await db.Warehouses.AnyAsync(w => w.Id == id)) return;
        db.Warehouses.Add(new Warehouse { Id = id, Name = "W", Code = "W", IsActive = true, MaxUsers = 10 });
        await db.SaveChangesAsync();
    }

    private static async Task<User> SeedUserAsync(
        IDbContextFactory<InventoryDbContext> factory, Action<User>? mutate = null)
    {
        await SeedWarehouseAsync(factory);
        await using var db = factory.CreateDbContext();
        var u = new User
        {
            Username = "alice",
            Email = "alice@t.local",
            DisplayName = "Alice",
            PasswordHash = "h",
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
    public async Task AnyUsersExistAsync_ReturnsFalseOnEmpty()
    {
        var (sut, _) = CreateSut(nameof(AnyUsersExistAsync_ReturnsFalseOnEmpty));
        (await sut.AnyUsersExistAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task AnyUsersExistAsync_ReturnsTrueWhenPresent()
    {
        var (sut, factory) = CreateSut(nameof(AnyUsersExistAsync_ReturnsTrueWhenPresent));
        await SeedUserAsync(factory);
        (await sut.AnyUsersExistAsync()).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetActiveUserByUsernameAsync_BlankInput_ReturnsNull(string? value)
    {
        var (sut, _) = CreateSut(nameof(GetActiveUserByUsernameAsync_BlankInput_ReturnsNull) + value);
        (await sut.GetActiveUserByUsernameAsync(value!)).Should().BeNull();
    }

    [Fact]
    public async Task GetActiveUserByUsernameAsync_ReturnsMatch()
    {
        var (sut, factory) = CreateSut(nameof(GetActiveUserByUsernameAsync_ReturnsMatch));
        var u = await SeedUserAsync(factory);
        (await sut.GetActiveUserByUsernameAsync("alice"))!.Id.Should().Be(u.Id);
    }

    [Fact]
    public async Task GetActiveUserByEmailAsync_FiltersInactiveAndDeleted()
    {
        var (sut, factory) = CreateSut(nameof(GetActiveUserByEmailAsync_FiltersInactiveAndDeleted));
        await SeedUserAsync(factory, u => { u.IsActive = false; });

        (await sut.GetActiveUserByEmailAsync("alice@t.local")).Should().BeNull();
    }

    [Fact]
    public async Task GetUserWithWarehouseAsync_IncludesWarehouseNavigation()
    {
        var (sut, factory) = CreateSut(nameof(GetUserWithWarehouseAsync_IncludesWarehouseNavigation));
        var u = await SeedUserAsync(factory);

        var loaded = await sut.GetUserWithWarehouseAsync(u.Id);

        loaded.Should().NotBeNull();
        loaded!.Warehouse.Should().NotBeNull();
        loaded.Warehouse!.Id.Should().Be(1);
    }

    [Fact]
    public async Task CountApprovedActiveUsersInWarehouseAsync_CountsOnlyApprovedActive()
    {
        var (sut, factory) = CreateSut(nameof(CountApprovedActiveUsersInWarehouseAsync_CountsOnlyApprovedActive));
        await SeedUserAsync(factory);
        await SeedUserAsync(factory, u =>
        {
            u.Username = "p"; u.Email = "p@t.local";
            u.ApprovalStatus = UserApprovalStatus.Pending;
        });
        await SeedUserAsync(factory, u =>
        {
            u.Username = "d"; u.Email = "d@t.local";
            u.IsActive = false;
        });

        (await sut.CountApprovedActiveUsersInWarehouseAsync(1)).Should().Be(1);
        (await sut.CountActiveUsersInWarehouseAsync(1)).Should().Be(2);
    }

    [Fact]
    public async Task GetTwoFactorRecoveryCodesAsync_ReturnsStoredValue()
    {
        var (sut, factory) = CreateSut(nameof(GetTwoFactorRecoveryCodesAsync_ReturnsStoredValue));
        var u = await SeedUserAsync(factory, x => x.TwoFactorRecoveryCodes = "[\"a\",\"b\"]");

        (await sut.GetTwoFactorRecoveryCodesAsync(u.Id)).Should().Be("[\"a\",\"b\"]");
    }

    [Fact]
    public async Task UpdateConsentPreferencesAsync_UpdatesFlagsAndTimestamps()
    {
        var (sut, factory) = CreateSut(nameof(UpdateConsentPreferencesAsync_UpdatesFlagsAndTimestamps));
        var u = await SeedUserAsync(factory, x =>
        {
            x.AnalyticsConsent = false;
            x.DeviceFingerprintConsent = false;
        });

        (await sut.UpdateConsentPreferencesAsync(u.Id, true, true)).Should().BeTrue();

        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(u.Id))!;
        refreshed.AnalyticsConsent.Should().BeTrue();
        refreshed.DeviceFingerprintConsent.Should().BeTrue();
        refreshed.AnalyticsConsentDate.Should().NotBeNull();
        refreshed.DeviceFingerprintConsentDate.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateConsentPreferencesAsync_UnknownUser_ReturnsFalse()
    {
        var (sut, factory) = CreateSut(nameof(UpdateConsentPreferencesAsync_UnknownUser_ReturnsFalse));
        await SeedWarehouseAsync(factory);

        (await sut.UpdateConsentPreferencesAsync(999, true, true)).Should().BeFalse();
    }

    [Fact]
    public async Task SetProfileImagePathAsync_StoresPathAndTimestamp()
    {
        var (sut, factory) = CreateSut(nameof(SetProfileImagePathAsync_StoresPathAndTimestamp));
        var u = await SeedUserAsync(factory);

        (await sut.SetProfileImagePathAsync(u.Id, "/img/x.png")).Should().BeTrue();

        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(u.Id))!;
        refreshed.ProfileImagePath.Should().Be("/img/x.png");
        refreshed.ProfileImageUploadedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateProfileImageAsync_ClearsWhenNullOrEmpty()
    {
        var (sut, factory) = CreateSut(nameof(UpdateProfileImageAsync_ClearsWhenNullOrEmpty));
        var u = await SeedUserAsync(factory, x =>
        {
            x.ProfileImagePath = "/old.png";
            x.ProfileImageUploadedAt = DateTime.UtcNow;
        });

        (await sut.UpdateProfileImageAsync(u.Id, null)).Should().BeTrue();

        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(u.Id))!;
        refreshed.ProfileImagePath.Should().BeNull();
        refreshed.ProfileImageUploadedAt.Should().BeNull();
    }

    [Fact]
    public async Task ApproveAsAdminAsync_SetsApprovedAndAdminRole()
    {
        var (sut, factory) = CreateSut(nameof(ApproveAsAdminAsync_SetsApprovedAndAdminRole));
        var u = await SeedUserAsync(factory, x =>
        {
            x.ApprovalStatus = UserApprovalStatus.Pending;
            x.Role = UserRole.User;
        });

        (await sut.ApproveAsAdminAsync(u.Id)).Should().BeTrue();

        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(u.Id))!;
        refreshed.ApprovalStatus.Should().Be(UserApprovalStatus.Approved);
        refreshed.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task RevokeConsentAsync_AnalyticsAlias_ClearsFlags()
    {
        var (sut, factory) = CreateSut(nameof(RevokeConsentAsync_AnalyticsAlias_ClearsFlags));
        var u = await SeedUserAsync(factory, x =>
        {
            x.AnalyticsConsent = true;
            x.AnalyticsConsentDate = DateTime.UtcNow;
        });

        (await sut.RevokeConsentAsync(u.Id, "Analytics & Performance")).Should().BeTrue();

        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(u.Id))!;
        refreshed.AnalyticsConsent.Should().BeFalse();
        refreshed.AnalyticsConsentDate.Should().BeNull();
    }

    [Fact]
    public async Task RevokeConsentAsync_FingerprintAlias_ClearsFlags()
    {
        var (sut, factory) = CreateSut(nameof(RevokeConsentAsync_FingerprintAlias_ClearsFlags));
        var u = await SeedUserAsync(factory, x =>
        {
            x.DeviceFingerprintConsent = true;
            x.DeviceFingerprintConsentDate = DateTime.UtcNow;
        });

        (await sut.RevokeConsentAsync(u.Id, "Device Fingerprinting")).Should().BeTrue();

        await using var verify = factory.CreateDbContext();
        var refreshed = (await verify.Users.FindAsync(u.Id))!;
        refreshed.DeviceFingerprintConsent.Should().BeFalse();
        refreshed.DeviceFingerprintConsentDate.Should().BeNull();
    }

    [Fact]
    public async Task RevokeConsentAsync_UnknownType_ReturnsFalse()
    {
        var (sut, factory) = CreateSut(nameof(RevokeConsentAsync_UnknownType_ReturnsFalse));
        var u = await SeedUserAsync(factory);

        (await sut.RevokeConsentAsync(u.Id, "Nope")).Should().BeFalse();
    }
}
