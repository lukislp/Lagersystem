using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Data.Repositories;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Wrapper service for StorageLocationRepository with automatic WarehouseId resolution.
/// </summary>
public sealed class StorageLocationService
{
    private readonly IStorageLocationRepository _repository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuditService _auditService;

    public StorageLocationService(
        IStorageLocationRepository repository,
        IHttpContextAccessor httpContextAccessor,
        IAuditService auditService)
    {
        _repository = repository;
        _httpContextAccessor = httpContextAccessor;
        _auditService = auditService;
    }

    private int GetWarehouseId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return 1;

        var warehouseIdClaim = user.FindFirst("WarehouseId");
        if (warehouseIdClaim != null && int.TryParse(warehouseIdClaim.Value, out var warehouseId))
            return warehouseId;

        return 1;
    }

    public Task<IEnumerable<StorageLocation>> GetAllAsync(CancellationToken cancellationToken = default)
        => _repository.GetAllAsync(GetWarehouseId());

    public Task<StorageLocation?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => _repository.GetByIdAsync(id, GetWarehouseId());

    public Task<StorageLocation?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        => _repository.GetByCodeAsync(code, GetWarehouseId());

    public Task<StorageLocation?> GetByQRCodeAsync(string qrCode, CancellationToken cancellationToken = default)
        => _repository.GetByQRCodeAsync(qrCode, GetWarehouseId());

    public Task<IEnumerable<StorageLocation>> GetByAisleAsync(string aisle, CancellationToken cancellationToken = default)
        => _repository.GetByAisleAsync(aisle, GetWarehouseId());

    public Task<IEnumerable<StorageLocation>> GetByRoomAsync(string room, CancellationToken cancellationToken = default)
        => _repository.GetByRoomAsync(room, GetWarehouseId());

    public Task<IEnumerable<string>> GetAllRoomsAsync(CancellationToken cancellationToken = default)
        => _repository.GetAllRoomsAsync(GetWarehouseId());

    public Task<IEnumerable<Product>> GetProductsInLocationAsync(int locationId, CancellationToken cancellationToken = default)
        => _repository.GetProductsByLocationAsync(locationId, GetWarehouseId());

    public async Task<StorageLocation> CreateAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        location.WarehouseId = GetWarehouseId();
        var created = await _repository.CreateAsync(location);
        await _auditService.LogStorageLocationCreatedAsync(created.Id, created.Code);
        return created;
    }

    public async Task<StorageLocation> UpdateAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        var updated = await _repository.UpdateAsync(location);
        await _auditService.LogStorageLocationUpdatedAsync(updated.Id, updated.Code);
        return updated;
    }

    public Task<StorageLocation> GenerateQRCodeAsync(int locationId, string qrCodeContent, CancellationToken cancellationToken = default)
        => _repository.GenerateQRCodeAsync(locationId, qrCodeContent);

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var location = await _repository.GetByIdAsync(id, GetWarehouseId());
        await _repository.DeleteAsync(id);
        await _auditService.LogStorageLocationDeletedAsync(id, location?.Code ?? $"Location#{id}");
    }

    public Task<bool> CodeExistsAsync(string code, int? excludeId = null, CancellationToken cancellationToken = default)
        => _repository.CodeExistsAsync(code, GetWarehouseId(), excludeId);
}
