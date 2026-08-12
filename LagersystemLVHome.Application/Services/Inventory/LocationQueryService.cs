using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

/// <inheritdoc cref="ILocationQueryService"/>
public sealed class LocationQueryService : ILocationQueryService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;

    public LocationQueryService(IDbContextFactory<InventoryDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<Room>> GetActiveRoomsForWarehouseAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Rooms
            .AsNoTracking()
            .Where(r => r.WarehouseId == warehouseId && r.IsActive)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<LocationContents> GetProductsAtLocationAsync(
        int storageLocationId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var productStorageLocations = await db.ProductStorageLocations
            .AsNoTracking()
            .Include(psl => psl.Product)
                .ThenInclude(p => p.Category)
            .Where(psl => psl.StorageLocationId == storageLocationId)
            .ToListAsync(cancellationToken);

        var products = productStorageLocations
            .Select(psl => psl.Product)
            .ToList();

        var quantities = productStorageLocations
            .ToDictionary(psl => psl.ProductId, psl => psl.Quantity);

        return new LocationContents(products, quantities);
    }

    public async Task<IReadOnlyList<RoomWithStats>> GetRoomsWithStatsAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var rooms = await db.Rooms
            .AsNoTracking()
            .Where(r => r.WarehouseId == warehouseId && r.IsActive)
            .ToListAsync(cancellationToken);

        if (rooms.Count == 0)
        {
            return Array.Empty<RoomWithStats>();
        }

        // Pre-fetch all storage locations and product placements for this
        // warehouse once so we can compute per-room stats in memory without
        // issuing N+1 round-trips for every room.
        var roomNames = rooms.Select(r => r.Name).ToHashSet();

        var storageLocations = await db.StorageLocations
            .AsNoTracking()
            .Where(sl => sl.WarehouseId == warehouseId && roomNames.Contains(sl.Room!))
            .Select(sl => new { sl.Id, sl.Room })
            .ToListAsync(cancellationToken);

        var locationIds = storageLocations.Select(sl => sl.Id).ToList();

        var productPlacements = await db.ProductStorageLocations
            .AsNoTracking()
            .Where(psl => locationIds.Contains(psl.StorageLocationId))
            .Select(psl => new { psl.StorageLocationId, psl.ProductId, psl.Quantity })
            .ToListAsync(cancellationToken);

        var locationsByRoomName = storageLocations
            .Where(sl => sl.Room is not null)
            .GroupBy(sl => sl.Room!)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToHashSet());

        var stats = rooms.Select(room =>
        {
            locationsByRoomName.TryGetValue(room.Name, out var locIds);
            locIds ??= [];

            var placementsForRoom = productPlacements
                .Where(p => locIds.Contains(p.StorageLocationId))
                .ToList();

            return new RoomWithStats(
                Room: room,
                StorageLocationCount: locIds.Count,
                DistinctProductCount: placementsForRoom.Select(p => p.ProductId).Distinct().Count(),
                TotalQuantity: placementsForRoom.Sum(p => p.Quantity));
        })
        .OrderByDescending(s => s.DistinctProductCount)
        .ToList();

        return stats;
    }

    public async Task<RoomContents?> GetRoomContentsAsync(
        int roomId,
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var room = await db.Rooms
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roomId && r.WarehouseId == warehouseId, cancellationToken);

        if (room is null)
        {
            return null;
        }

        var storageLocations = await db.StorageLocations
            .AsNoTracking()
            .Where(sl => sl.Room == room.Name && sl.WarehouseId == warehouseId)
            .OrderBy(sl => sl.Code)
            .ToListAsync(cancellationToken);

        var locationIds = storageLocations.Select(sl => sl.Id).ToList();

        var placements = await db.ProductStorageLocations
            .AsNoTracking()
            .Include(psl => psl.Product)
                .ThenInclude(p => p.Category)
            .Include(psl => psl.StorageLocation)
            .Where(psl => locationIds.Contains(psl.StorageLocationId))
            .ToListAsync(cancellationToken);

        return new RoomContents(room, storageLocations, placements);
    }

    public async Task<StorageOverviewData> GetStorageOverviewAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var storageLocations = await db.StorageLocations
            .AsNoTracking()
            .Where(sl => sl.WarehouseId == warehouseId && sl.IsActive)
            .ToListAsync(cancellationToken);

        var availableRooms = await db.Rooms
            .AsNoTracking()
            .Where(r => r.WarehouseId == warehouseId)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        if (storageLocations.Count == 0)
        {
            return new StorageOverviewData([], availableRooms);
        }

        var locationIds = storageLocations.Select(sl => sl.Id).ToList();

        // One round-trip instead of N (one per storage location).
        var placements = await db.ProductStorageLocations
            .AsNoTracking()
            .Where(psl => locationIds.Contains(psl.StorageLocationId))
            .Select(psl => new { psl.StorageLocationId, psl.ProductId, psl.Quantity })
            .ToListAsync(cancellationToken);

        var byLocation = placements.ToLookup(p => p.StorageLocationId);

        var stats = storageLocations
            .Select(sl =>
            {
                var items = byLocation[sl.Id].ToList();
                return new StorageLocationWithStats(
                    Location: sl,
                    DistinctProductCount: items.Select(i => i.ProductId).Distinct().Count(),
                    TotalQuantity: items.Sum(i => i.Quantity));
            })
            .OrderByDescending(s => s.TotalQuantity)
            .ToList();

        return new StorageOverviewData(stats, availableRooms);
    }

    public async Task<StorageLocation?> FindActiveStorageLocationByCodeAsync(
        int warehouseId,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var searchCode = code.ToUpper();

        return await db.StorageLocations
            .AsNoTracking()
            .Where(sl =>
                sl.Code.ToUpper() == searchCode &&
                sl.WarehouseId == warehouseId &&
                sl.IsActive)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StorageLocation>> GetActiveStorageLocationsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.StorageLocations
            .AsNoTracking()
            .Where(sl => sl.IsActive)
            .OrderBy(sl => sl.Code)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StorageLocation>> GetActiveStorageLocationsForWarehouseAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.StorageLocations
            .AsNoTracking()
            .Where(sl => sl.WarehouseId == warehouseId && sl.IsActive)
            .OrderBy(sl => sl.Code)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, int>> GetRoomIdsByNameAsync(
        IEnumerable<string> roomNames,
        CancellationToken cancellationToken = default)
    {
        var names = roomNames as IReadOnlyCollection<string> ?? roomNames.ToList();
        if (names.Count == 0) return new Dictionary<string, int>();

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Rooms
            .AsNoTracking()
            .Where(r => names.Contains(r.Name))
            .ToDictionaryAsync(r => r.Name, r => r.Id, cancellationToken);
    }
}


