using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Auth;

public class ApiKeyServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task SeedUserAsync(IDbContextFactory<InventoryDbContext> factory, int userId = 1, int warehouseId = 1)
    {
        await using var db = factory.CreateDbContext();
        if (!await db.Warehouses.AnyAsync(w => w.Id == warehouseId))
        {
            db.Warehouses.Add(new Warehouse
            {
                Id = warehouseId,
                Name = $"WH{warehouseId}",
                Address = "addr",
                IsActive = true
            });
        }
        if (!await db.Users.AnyAsync(u => u.Id == userId))
        {
            db.Users.Add(new User
            {
                Id = userId,
                Username = $"user{userId}",
                Email = $"user{userId}@x.local",
                DisplayName = $"User {userId}",
                PasswordHash = "x",
                WarehouseId = warehouseId,
                ApprovalStatus = UserApprovalStatus.Approved,
                Role = UserRole.User,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();
    }

    private static ApiKeyService CreateSut(IDbContextFactory<InventoryDbContext> factory, IAuditService? audit = null)
        => new(factory, NullLogger<ApiKeyService>.Instance, audit ?? Substitute.For<IAuditService>());

    [Fact]
    public async Task GenerateApiKeyAsync_PersistsKeyAndReturnsClearText()
    {
        var factory = CreateFactory(nameof(GenerateApiKeyAsync_PersistsKeyAndReturnsClearText));
        await SeedUserAsync(factory);
        var audit = Substitute.For<IAuditService>();
        var sut = CreateSut(factory, audit);

        var (clearKey, persisted) = await sut.GenerateApiKeyAsync(
            userId: 1,
            name: "Home Assistant",
            permissions: ["products.read", "products.write"]);

        clearKey.Should().NotBeNullOrEmpty();
        clearKey.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
        persisted.Id.Should().BeGreaterThan(0);
        persisted.KeyPrefix.Should().Be(clearKey[..8]);
        persisted.KeyHash.Should().NotBe(clearKey, because: "the clear key must never be stored");
        persisted.KeyHash.Length.Should().Be(64, because: "SHA-256 hex is 64 characters");
        persisted.IsActive.Should().BeTrue();
        persisted.Permissions.Should().Be("products.read,products.write");

        await using var db = factory.CreateDbContext();
        (await db.ApiKeys.CountAsync()).Should().Be(1);

        await audit.Received(1).LogAsync(
            "API_KEY_CREATED",
            "ApiKey",
            persisted.Id,
            Arg.Any<object>(),
            AuditSeverity.Info,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateApiKeyAsync_NullPermissions_StoresNull()
    {
        var factory = CreateFactory(nameof(GenerateApiKeyAsync_NullPermissions_StoresNull));
        await SeedUserAsync(factory);
        var sut = CreateSut(factory);

        var (_, persisted) = await sut.GenerateApiKeyAsync(1, "ci-bot");

        persisted.Permissions.Should().BeNull();
    }

    [Fact]
    public async Task GenerateApiKeyAsync_ProducesUniqueKeysAcrossCalls()
    {
        var factory = CreateFactory(nameof(GenerateApiKeyAsync_ProducesUniqueKeysAcrossCalls));
        await SeedUserAsync(factory);
        var sut = CreateSut(factory);

        var (key1, _) = await sut.GenerateApiKeyAsync(1, "k1");
        var (key2, _) = await sut.GenerateApiKeyAsync(1, "k2");

        key1.Should().NotBe(key2);
    }

    [Fact]
    public async Task ValidateApiKeyAsync_ValidKey_ReturnsUser()
    {
        var factory = CreateFactory(nameof(ValidateApiKeyAsync_ValidKey_ReturnsUser));
        await SeedUserAsync(factory);
        var sut = CreateSut(factory);

        var (clearKey, _) = await sut.GenerateApiKeyAsync(1, "k");

        var user = await sut.ValidateApiKeyAsync(clearKey);

        user.Should().NotBeNull();
        user!.Id.Should().Be(1);
    }

    [Fact]
    public async Task ValidateApiKeyAsync_UnknownKey_ReturnsNull()
    {
        var factory = CreateFactory(nameof(ValidateApiKeyAsync_UnknownKey_ReturnsNull));
        await SeedUserAsync(factory);
        var sut = CreateSut(factory);

        var user = await sut.ValidateApiKeyAsync("not-a-real-key");

        user.Should().BeNull();
    }

    [Fact]
    public async Task ValidateApiKeyAsync_ExpiredKey_ReturnsNull()
    {
        var factory = CreateFactory(nameof(ValidateApiKeyAsync_ExpiredKey_ReturnsNull));
        await SeedUserAsync(factory);
        var sut = CreateSut(factory);

        var (clearKey, _) = await sut.GenerateApiKeyAsync(
            1, "expired", expiresAt: DateTime.UtcNow.AddDays(-1));

        var user = await sut.ValidateApiKeyAsync(clearKey);

        user.Should().BeNull();
    }

    [Fact]
    public async Task ValidateApiKeyAsync_RevokedKey_ReturnsNull()
    {
        var factory = CreateFactory(nameof(ValidateApiKeyAsync_RevokedKey_ReturnsNull));
        await SeedUserAsync(factory);
        var sut = CreateSut(factory);

        var (clearKey, persisted) = await sut.GenerateApiKeyAsync(1, "k");
        await sut.RevokeApiKeyAsync(persisted.Id, userId: 1);

        var user = await sut.ValidateApiKeyAsync(clearKey);

        user.Should().BeNull();
    }

    [Fact]
    public async Task GetApiKeyByKeyAsync_ReturnsRecord_ForActiveKey()
    {
        var factory = CreateFactory(nameof(GetApiKeyByKeyAsync_ReturnsRecord_ForActiveKey));
        await SeedUserAsync(factory);
        var sut = CreateSut(factory);

        var (clearKey, persisted) = await sut.GenerateApiKeyAsync(1, "k");

        var found = await sut.GetApiKeyByKeyAsync(clearKey);

        found.Should().NotBeNull();
        found!.Id.Should().Be(persisted.Id);
    }

    [Fact]
    public async Task GetUserApiKeysAsync_ReturnsKeysForUserOnly_OrderedByCreatedDesc()
    {
        var factory = CreateFactory(nameof(GetUserApiKeysAsync_ReturnsKeysForUserOnly_OrderedByCreatedDesc));
        await SeedUserAsync(factory, userId: 1);
        await SeedUserAsync(factory, userId: 2);
        var sut = CreateSut(factory);

        var (_, k1) = await sut.GenerateApiKeyAsync(1, "k1");
        await Task.Delay(15);
        var (_, k2) = await sut.GenerateApiKeyAsync(1, "k2");
        var (_, _) = await sut.GenerateApiKeyAsync(2, "other");

        var keys = await sut.GetUserApiKeysAsync(1);

        keys.Select(k => k.Id).Should().Equal(k2.Id, k1.Id);
    }

    [Fact]
    public async Task RevokeApiKeyAsync_OwnedKey_DeactivatesAndAudits()
    {
        var factory = CreateFactory(nameof(RevokeApiKeyAsync_OwnedKey_DeactivatesAndAudits));
        await SeedUserAsync(factory);
        var audit = Substitute.For<IAuditService>();
        var sut = CreateSut(factory, audit);

        var (_, persisted) = await sut.GenerateApiKeyAsync(1, "k");

        var ok = await sut.RevokeApiKeyAsync(persisted.Id, userId: 1);

        ok.Should().BeTrue();

        await using var db = factory.CreateDbContext();
        (await db.ApiKeys.FindAsync(persisted.Id))!.IsActive.Should().BeFalse();

        await audit.Received(1).LogAsync(
            "API_KEY_REVOKED",
            "ApiKey",
            persisted.Id,
            Arg.Any<object>(),
            AuditSeverity.Warning,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeApiKeyAsync_ForeignKey_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(RevokeApiKeyAsync_ForeignKey_ReturnsFalse));
        await SeedUserAsync(factory, userId: 1);
        await SeedUserAsync(factory, userId: 2);
        var sut = CreateSut(factory);

        var (_, persisted) = await sut.GenerateApiKeyAsync(1, "k");

        var ok = await sut.RevokeApiKeyAsync(persisted.Id, userId: 2);

        ok.Should().BeFalse();
        await using var db = factory.CreateDbContext();
        (await db.ApiKeys.FindAsync(persisted.Id))!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateLastUsedAsync_SetsTimestamp()
    {
        var factory = CreateFactory(nameof(UpdateLastUsedAsync_SetsTimestamp));
        await SeedUserAsync(factory);
        var sut = CreateSut(factory);

        var (_, persisted) = await sut.GenerateApiKeyAsync(1, "k");
        var before = DateTime.UtcNow;

        await sut.UpdateLastUsedAsync(persisted.Id);

        await using var db = factory.CreateDbContext();
        var stored = await db.ApiKeys.FindAsync(persisted.Id);
        stored!.LastUsedAt.Should().NotBeNull();
        stored.LastUsedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task UpdateLastUsedAsync_UnknownId_DoesNotThrow()
    {
        var factory = CreateFactory(nameof(UpdateLastUsedAsync_UnknownId_DoesNotThrow));
        var sut = CreateSut(factory);

        var act = async () => await sut.UpdateLastUsedAsync(999_999);

        await act.Should().NotThrowAsync();
    }
}
