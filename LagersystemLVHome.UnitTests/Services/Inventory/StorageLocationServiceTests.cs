using LagersystemLVHome.Data.Repositories;
using LagersystemLVHome.Application.Services;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace LagersystemLVHome.UnitTests.Services.Inventory;

public class StorageLocationServiceTests
{
    private static IHttpContextAccessor AnonymousAccessor()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        return accessor;
    }

    private static IHttpContextAccessor AuthenticatedAccessor(string? warehouseIdClaim = null)
    {
        var claims = new List<Claim>();
        if (warehouseIdClaim != null) claims.Add(new Claim("WarehouseId", warehouseIdClaim));
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);
        return accessor;
    }

    private sealed class Fixture
    {
        public required StorageLocationService Sut { get; init; }
        public required IStorageLocationRepository Repo { get; init; }
        public required IAuditService Audit { get; init; }
    }

    private static Fixture CreateSut(IHttpContextAccessor? accessor = null)
    {
        var repo = Substitute.For<IStorageLocationRepository>();
        var audit = Substitute.For<IAuditService>();
        var sut = new StorageLocationService(repo, accessor ?? AnonymousAccessor(), audit);
        return new Fixture { Sut = sut, Repo = repo, Audit = audit };
    }

    // --- GetWarehouseId resolution, exercised through the public read methods ---

    [Fact]
    public async Task GetAllAsync_Anonymous_UsesWarehouseIdOne()
    {
        var f = CreateSut(AnonymousAccessor());
        f.Repo.GetAllAsync(1).Returns(new List<StorageLocation>());

        await f.Sut.GetAllAsync();

        await f.Repo.Received(1).GetAllAsync(1);
    }

    [Fact]
    public async Task GetAllAsync_AuthenticatedWithoutClaim_UsesWarehouseIdOne()
    {
        var f = CreateSut(AuthenticatedAccessor());
        f.Repo.GetAllAsync(1).Returns(new List<StorageLocation>());

        await f.Sut.GetAllAsync();

        await f.Repo.Received(1).GetAllAsync(1);
    }

    [Fact]
    public async Task GetAllAsync_AuthenticatedWithClaim_UsesClaimWarehouseId()
    {
        var f = CreateSut(AuthenticatedAccessor("7"));
        f.Repo.GetAllAsync(7).Returns(new List<StorageLocation>());

        await f.Sut.GetAllAsync();

        await f.Repo.Received(1).GetAllAsync(7);
    }

    [Fact]
    public async Task GetAllAsync_AuthenticatedWithUnparsableClaim_FallsBackToOne()
    {
        var f = CreateSut(AuthenticatedAccessor("not-a-number"));
        f.Repo.GetAllAsync(1).Returns(new List<StorageLocation>());

        await f.Sut.GetAllAsync();

        await f.Repo.Received(1).GetAllAsync(1);
    }

    [Fact]
    public async Task GetByIdAsync_DelegatesToRepositoryWithResolvedWarehouseId()
    {
        var f = CreateSut(AuthenticatedAccessor("3"));
        var location = new StorageLocation { Id = 5, Code = "A", Name = "A", WarehouseId = 3 };
        f.Repo.GetByIdAsync(5, 3).Returns(location);

        var result = await f.Sut.GetByIdAsync(5);

        result.Should().Be(location);
    }

    [Fact]
    public async Task GetByCodeAsync_DelegatesToRepository()
    {
        var f = CreateSut();
        f.Repo.GetByCodeAsync("X", 1).Returns(new StorageLocation { Code = "X", Name = "X", WarehouseId = 1 });

        var result = await f.Sut.GetByCodeAsync("X");

        result.Should().NotBeNull();
        await f.Repo.Received(1).GetByCodeAsync("X", 1);
    }

    [Fact]
    public async Task GetByQRCodeAsync_DelegatesToRepository()
    {
        var f = CreateSut();

        await f.Sut.GetByQRCodeAsync("QR");

        await f.Repo.Received(1).GetByQRCodeAsync("QR", 1);
    }

    [Fact]
    public async Task GetByAisleAsync_DelegatesToRepository()
    {
        var f = CreateSut();

        await f.Sut.GetByAisleAsync("A1");

        await f.Repo.Received(1).GetByAisleAsync("A1", 1);
    }

    [Fact]
    public async Task GetByRoomAsync_DelegatesToRepository()
    {
        var f = CreateSut();

        await f.Sut.GetByRoomAsync("Hall");

        await f.Repo.Received(1).GetByRoomAsync("Hall", 1);
    }

    [Fact]
    public async Task GetAllRoomsAsync_DelegatesToRepository()
    {
        var f = CreateSut();

        await f.Sut.GetAllRoomsAsync();

        await f.Repo.Received(1).GetAllRoomsAsync(1);
    }

    [Fact]
    public async Task GetProductsInLocationAsync_DelegatesToRepository()
    {
        var f = CreateSut();

        await f.Sut.GetProductsInLocationAsync(42);

        await f.Repo.Received(1).GetProductsByLocationAsync(42, 1);
    }

    [Fact]
    public async Task CodeExistsAsync_DelegatesToRepositoryWithExcludeId()
    {
        var f = CreateSut();

        await f.Sut.CodeExistsAsync("ABC", excludeId: 9);

        await f.Repo.Received(1).CodeExistsAsync("ABC", 1, 9);
    }

    // --- CreateAsync ---

    [Fact]
    public async Task CreateAsync_SetsWarehouseIdAndLogsAudit()
    {
        var f = CreateSut(AuthenticatedAccessor("4"));
        var location = new StorageLocation { Code = "NEW", Name = "New" };
        var created = new StorageLocation { Id = 10, Code = "NEW", Name = "New", WarehouseId = 4 };
        f.Repo.CreateAsync(Arg.Any<StorageLocation>()).Returns(created);

        var result = await f.Sut.CreateAsync(location);

        location.WarehouseId.Should().Be(4);
        result.Should().Be(created);
        await f.Audit.Received(1).LogStorageLocationCreatedAsync(10, "NEW", Arg.Any<CancellationToken>());
    }

    // --- UpdateAsync ---

    [Fact]
    public async Task UpdateAsync_PersistsAndLogsAudit()
    {
        var f = CreateSut();
        var location = new StorageLocation { Id = 1, Code = "A", Name = "A", WarehouseId = 1 };
        f.Repo.UpdateAsync(location).Returns(location);

        var result = await f.Sut.UpdateAsync(location);

        result.Should().Be(location);
        await f.Audit.Received(1).LogStorageLocationUpdatedAsync(1, "A", Arg.Any<CancellationToken>());
    }

    // --- GenerateQRCodeAsync ---

    [Fact]
    public async Task GenerateQRCodeAsync_DelegatesToRepository()
    {
        var f = CreateSut();

        await f.Sut.GenerateQRCodeAsync(3, "QR-CONTENT");

        await f.Repo.Received(1).GenerateQRCodeAsync(3, "QR-CONTENT");
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsync_KnownLocation_LogsAuditWithCode()
    {
        var f = CreateSut(AuthenticatedAccessor("2"));
        f.Repo.GetByIdAsync(5, 2).Returns(new StorageLocation { Id = 5, Code = "DEL", Name = "Del", WarehouseId = 2 });

        await f.Sut.DeleteAsync(5);

        await f.Repo.Received(1).DeleteAsync(5);
        await f.Audit.Received(1).LogStorageLocationDeletedAsync(5, "DEL", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_UnknownLocation_LogsAuditWithFallbackLabel()
    {
        var f = CreateSut();
        f.Repo.GetByIdAsync(99, 1).Returns((StorageLocation?)null);

        await f.Sut.DeleteAsync(99);

        await f.Repo.Received(1).DeleteAsync(99);
        await f.Audit.Received(1).LogStorageLocationDeletedAsync(99, "Location#99", Arg.Any<CancellationToken>());
    }
}
