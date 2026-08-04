using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Domain service for Room CRUD + business rules. Extracted so the
/// "create room" dialogs in the Storage Overview and QR Code Generator
/// pages never touch <c>InventoryDbContext</c> directly.
/// </summary>
public interface IRoomService
{
    /// <summary>
    /// Returns <c>true</c> if a room with the given code already exists in the
    /// warehouse (case-sensitive match, consistent with how the code is stored).
    /// </summary>
    Task<bool> RoomCodeExistsAsync(int warehouseId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new room. The caller supplies a fully-populated
    /// <see cref="Room"/> (Name, Code, WarehouseId, …). The service enforces the
    /// unique-code invariant per warehouse.
    /// </summary>
    Task<Result<Room>> CreateRoomAsync(Room room, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all admin view data for a warehouse in a single round-trip:
    /// rooms, their storage locations and the distinct-product count per room.
    /// </summary>
    Task<RoomAdminView> GetAdminViewAsync(int warehouseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates scalar properties of an existing room. Enforces unique code per warehouse.
    /// </summary>
    Task<Result<Room>> UpdateRoomAsync(Room room, CancellationToken cancellationToken = default);

    /// <summary>Activates or deactivates a room.</summary>
    Task<Result> SetRoomActiveAsync(int roomId, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>Deletes a room (caller is expected to check for dependent storage locations).</summary>
    Task<Result> DeleteRoomAsync(int roomId, CancellationToken cancellationToken = default);
}

/// <summary>Aggregated admin data for the Rooms management page.</summary>
public sealed record RoomAdminView(
    List<Room> Rooms,
    List<StorageLocation> StorageLocations,
    Dictionary<int, int> ProductCountByRoomId);
