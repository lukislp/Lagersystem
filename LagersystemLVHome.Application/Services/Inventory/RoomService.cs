using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

/// <inheritdoc cref="IRoomService"/>
public sealed class RoomService : IRoomService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<RoomService> _logger;

    public RoomService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<RoomService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<bool> RoomCodeExistsAsync(
        int warehouseId,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Rooms
            .AnyAsync(r => r.Code == code && r.WarehouseId == warehouseId, cancellationToken);
    }

    public async Task<Result<Room>> CreateRoomAsync(
        Room room,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(room.Name) || string.IsNullOrWhiteSpace(room.Code))
        {
            return Result<Room>.Failure("room.invalid", "Name and Code are required");
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var exists = await db.Rooms
                .AnyAsync(r => r.Code == room.Code && r.WarehouseId == room.WarehouseId, cancellationToken);

            if (exists)
            {
                return Result<Room>.Failure("room.codeexists", $"Room code '{room.Code}' already exists for this warehouse");
            }

            if (room.CreatedAt == default)
            {
                room.CreatedAt = DateTime.UtcNow;
            }
            room.UpdatedAt = DateTime.UtcNow;

            db.Rooms.Add(room);
            await db.SaveChangesAsync(cancellationToken);

            return Result<Room>.Success(room);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating room {Code} for warehouse {WarehouseId}", room.Code, room.WarehouseId);
            return Result<Room>.Failure("room.createfailed", ex.Message);
        }
    }

    public async Task<RoomAdminView> GetAdminViewAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var rooms = await db.Rooms
            .AsNoTracking()
            .Where(r => r.WarehouseId == warehouseId)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        var storageLocations = await db.StorageLocations
            .AsNoTracking()
            .Include(s => s.Products)
            .Where(s => s.WarehouseId == warehouseId)
            .ToListAsync(cancellationToken);

        var storageLocationIds = storageLocations.Select(s => s.Id).ToList();
        var productsByStorage = await db.ProductStorageLocations
            .AsNoTracking()
            .Where(psl => storageLocationIds.Contains(psl.StorageLocationId))
            .Select(psl => new { psl.StorageLocationId, psl.ProductId })
            .ToListAsync(cancellationToken);

        var productCountByRoomId = new Dictionary<int, int>();
        foreach (var room in rooms)
        {
            var roomStorageIds = storageLocations
                .Where(s => s.Room == room.Name)
                .Select(s => s.Id)
                .ToHashSet();
            productCountByRoomId[room.Id] = productsByStorage
                .Where(p => roomStorageIds.Contains(p.StorageLocationId))
                .Select(p => p.ProductId)
                .Distinct()
                .Count();
        }

        return new RoomAdminView(rooms, storageLocations, productCountByRoomId);
    }

    public async Task<Result<Room>> UpdateRoomAsync(
        Room room,
        CancellationToken cancellationToken = default)
    {
        if (room.Id <= 0 || string.IsNullOrWhiteSpace(room.Name) || string.IsNullOrWhiteSpace(room.Code))
        {
            return Result<Room>.Failure("room.invalid", "Id, Name and Code are required");
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var codeConflict = await db.Rooms
                .AnyAsync(r => r.Code == room.Code
                               && r.WarehouseId == room.WarehouseId
                               && r.Id != room.Id, cancellationToken);

            if (codeConflict)
            {
                return Result<Room>.Failure("room.codeexists", $"Room code '{room.Code}' already exists for this warehouse");
            }

            var existing = await db.Rooms.FindAsync([room.Id], cancellationToken);
            if (existing is null)
            {
                return Result<Room>.Failure("room.notfound", $"Room {room.Id} not found");
            }

            existing.Name = room.Name;
            existing.Code = room.Code;
            existing.Description = room.Description;
            existing.Type = room.Type;
            existing.Floor = room.Floor;
            existing.Area = room.Area;
            existing.Capacity = room.Capacity;
            existing.IsActive = room.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            return Result<Room>.Success(existing);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating room {Id}", room.Id);
            return Result<Room>.Failure("room.updatefailed", ex.Message);
        }
    }

    public async Task<Result> SetRoomActiveAsync(
        int roomId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var room = await db.Rooms.FindAsync([roomId], cancellationToken);
            if (room is null)
            {
                return Result.Failure("room.notfound", $"Room {roomId} not found");
            }

            room.IsActive = isActive;
            room.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling active state for room {Id}", roomId);
            return Result.Failure("room.updatefailed", ex.Message);
        }
    }

    public async Task<Result> DeleteRoomAsync(
        int roomId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var room = await db.Rooms.FindAsync([roomId], cancellationToken);
            if (room is null)
            {
                return Result.Failure("room.notfound", $"Room {roomId} not found");
            }

            db.Rooms.Remove(room);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting room {Id}", roomId);
            return Result.Failure("room.deletefailed", ex.Message);
        }
    }
}
