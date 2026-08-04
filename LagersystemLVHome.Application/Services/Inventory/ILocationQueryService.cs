using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Read-only queries about the physical warehouse layout (rooms, storage
/// locations and their product contents). Keeps direct DbContext access out
/// of the Razor pages that consume these queries.
/// </summary>
public interface ILocationQueryService
{
    /// <summary>Active rooms of a warehouse, ordered by name.</summary>
    Task<IReadOnlyList<Room>> GetActiveRoomsForWarehouseAsync(
        int warehouseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Products stored at a location together with their per-location quantities.
    /// </summary>
    Task<LocationContents> GetProductsAtLocationAsync(
        int storageLocationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rooms of a warehouse with aggregated statistics (number of storage
    /// locations, distinct products, total quantity). Used by the Rooms
    /// Overview page. Ordered by <see cref="RoomWithStats.DistinctProductCount"/> descending.
    /// </summary>
    Task<IReadOnlyList<RoomWithStats>> GetRoomsWithStatsAsync(
        int warehouseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Full contents of a single room: the room itself, its storage locations
    /// (ordered by code) and all product placements inside them (eagerly
    /// including the product + its category and the storage location). Returns
    /// <c>null</c> if the room does not exist in the given warehouse.
    /// </summary>
    Task<RoomContents?> GetRoomContentsAsync(
        int roomId,
        int warehouseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-storage-location statistics for the given warehouse, plus the list
    /// of available rooms used by the "create location" dialog on the Storage
    /// Overview page. Ordered by <see cref="StorageLocationWithStats.TotalQuantity"/> descending.
    /// </summary>
    Task<StorageOverviewData> GetStorageOverviewAsync(
        int warehouseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Case-insensitive lookup of an active storage location by its code.
    /// Used by the QR-scan and manual-code search on the Storage Overview page.
    /// </summary>
    Task<StorageLocation?> FindActiveStorageLocationByCodeAsync(
        int warehouseId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all active storage locations ordered by code.
    /// Used by the Products page to populate the storage-location multi-select.
    /// </summary>
    Task<IReadOnlyList<StorageLocation>> GetActiveStorageLocationsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all active storage locations for the given warehouse, ordered by code.
    /// Used by the Scanner page.
    /// </summary>
    Task<IReadOnlyList<StorageLocation>> GetActiveStorageLocationsForWarehouseAsync(
        int warehouseId,
        CancellationToken cancellationToken = default);

    /// <summary>Looks up room IDs for a set of room names (as stored on StorageLocation.Room).</summary>
    Task<Dictionary<string, int>> GetRoomIdsByNameAsync(
        IEnumerable<string> roomNames,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of <see cref="ILocationQueryService.GetProductsAtLocationAsync"/>.</summary>
public sealed record LocationContents(
    IReadOnlyList<Product> Products,
    IReadOnlyDictionary<int, int> QuantityByProductId);

/// <summary>Room + aggregated stats used by the Rooms Overview dashboard.</summary>
public sealed record RoomWithStats(
    Room Room,
    int StorageLocationCount,
    int DistinctProductCount,
    int TotalQuantity);

/// <summary>Full contents of a room used by the Room Products page.</summary>
public sealed record RoomContents(
    Room Room,
    IReadOnlyList<StorageLocation> StorageLocations,
    IReadOnlyList<ProductStorageLocation> ProductPlacements);

/// <summary>Data shown on the Storage Overview dashboard.</summary>
public sealed record StorageOverviewData(
    IReadOnlyList<StorageLocationWithStats> Locations,
    IReadOnlyList<Room> AvailableRooms);

/// <summary>Single row in the storage overview table.</summary>
public sealed record StorageLocationWithStats(
    StorageLocation Location,
    int DistinctProductCount,
    int TotalQuantity);

