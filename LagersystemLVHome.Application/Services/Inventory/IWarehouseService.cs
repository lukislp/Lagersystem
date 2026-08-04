using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Aggregated admin data for the Warehouses management page.
/// </summary>
public sealed record WarehouseAdminView(
    List<Warehouse> Warehouses,
    Dictionary<int, int> UserCountByWarehouseId,
    Dictionary<int, int> ProductCountByWarehouseId,
    Dictionary<int, int> ActiveRoomCountByWarehouseId,
    Dictionary<int, int> ActiveStorageLocationCountByWarehouseId);

/// <summary>
/// Warehouse CRUD service used by the admin page.
/// </summary>
public interface IWarehouseService
{
    /// <summary>
    /// Returns warehouses visible to the given user (SuperAdmin sees all, Admin sees only own)
    /// plus aggregate counts needed by the admin grid. Single round-trip.
    /// </summary>
    Task<WarehouseAdminView> GetAdminViewAsync(User currentUser, CancellationToken cancellationToken = default);

    /// <summary>Returns all active warehouses ordered by name (used by registration dropdown).</summary>
    Task<List<Warehouse>> GetActiveWarehousesAsync(CancellationToken cancellationToken = default);

    /// <summary>Create a new warehouse. Enforces unique code. Seeds default categories.</summary>
    Task<Result<Warehouse>> CreateWarehouseAsync(Warehouse warehouse, CancellationToken cancellationToken = default);

    /// <summary>Update scalar properties. Enforces unique code.</summary>
    Task<Result<Warehouse>> UpdateWarehouseAsync(Warehouse warehouse, CancellationToken cancellationToken = default);

    /// <summary>Activate/deactivate a warehouse.</summary>
    Task<Result> SetWarehouseActiveAsync(int warehouseId, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>Delete a warehouse.</summary>
    Task<Result> DeleteWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default);
}
