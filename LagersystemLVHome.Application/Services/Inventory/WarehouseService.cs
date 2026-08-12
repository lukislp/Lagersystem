using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

/// <inheritdoc cref="IWarehouseService"/>
public sealed class WarehouseService : IWarehouseService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly CategorySeederService _categorySeeder;
    private readonly ILogger<WarehouseService> _logger;

    public WarehouseService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        CategorySeederService categorySeeder,
        ILogger<WarehouseService> logger)
    {
        _contextFactory = contextFactory;
        _categorySeeder = categorySeeder;
        _logger = logger;
    }

    public async Task<WarehouseAdminView> GetAdminViewAsync(
        User currentUser,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var warehousesQuery = db.Warehouses.AsNoTracking();
        if (currentUser.Role != UserRole.SuperAdmin)
        {
            warehousesQuery = warehousesQuery.Where(w => w.Id == currentUser.WarehouseId);
        }

        var warehouses = await warehousesQuery
            .OrderBy(w => w.Name)
            .ToListAsync(cancellationToken);

        var userCounts = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .GroupBy(u => u.WarehouseId)
            .Select(g => new { WarehouseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WarehouseId, x => x.Count, cancellationToken);

        var productCounts = await db.Products
            .AsNoTracking()
            .GroupBy(p => p.WarehouseId)
            .Select(g => new { WarehouseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WarehouseId, x => x.Count, cancellationToken);

        var roomCounts = await db.Rooms
            .AsNoTracking()
            .Where(r => r.IsActive)
            .GroupBy(r => r.WarehouseId)
            .Select(g => new { WarehouseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WarehouseId, x => x.Count, cancellationToken);

        var storageLocationCounts = await db.StorageLocations
            .AsNoTracking()
            .Where(s => s.IsActive)
            .GroupBy(s => s.WarehouseId)
            .Select(g => new { WarehouseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WarehouseId, x => x.Count, cancellationToken);

        return new WarehouseAdminView(
            warehouses,
            userCounts,
            productCounts,
            roomCounts,
            storageLocationCounts);
    }

    public async Task<List<Warehouse>> GetActiveWarehousesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Warehouses
            .AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<Warehouse>> CreateWarehouseAsync(
        Warehouse warehouse,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(warehouse.Name) || string.IsNullOrWhiteSpace(warehouse.Code))
        {
            return Result<Warehouse>.Failure("warehouse.invalid", "Name and Code are required");
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var exists = await db.Warehouses
                .AnyAsync(w => w.Code == warehouse.Code, cancellationToken);

            if (exists)
            {
                return Result<Warehouse>.Failure("warehouse.codeexists", $"Warehouse code '{warehouse.Code}' already exists");
            }

            if (warehouse.CreatedAt == default)
            {
                warehouse.CreatedAt = DateTime.UtcNow;
            }
            warehouse.UpdatedAt = DateTime.UtcNow;

            db.Warehouses.Add(warehouse);
            await db.SaveChangesAsync(cancellationToken);

            // Seed default categories for the new warehouse
            await _categorySeeder.SeedCategoriesAsync(warehouse.Id);

            return Result<Warehouse>.Success(warehouse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating warehouse {Code}", warehouse.Code);
            return Result<Warehouse>.Failure("warehouse.createfailed", ex.Message);
        }
    }

    public async Task<Result<Warehouse>> UpdateWarehouseAsync(
        Warehouse warehouse,
        CancellationToken cancellationToken = default)
    {
        if (warehouse.Id <= 0 || string.IsNullOrWhiteSpace(warehouse.Name) || string.IsNullOrWhiteSpace(warehouse.Code))
        {
            return Result<Warehouse>.Failure("warehouse.invalid", "Id, Name and Code are required");
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var codeConflict = await db.Warehouses
                .AnyAsync(w => w.Code == warehouse.Code && w.Id != warehouse.Id, cancellationToken);

            if (codeConflict)
            {
                return Result<Warehouse>.Failure("warehouse.codeexists", $"Warehouse code '{warehouse.Code}' already exists");
            }

            var existing = await db.Warehouses.FindAsync([warehouse.Id], cancellationToken);
            if (existing is null)
            {
                return Result<Warehouse>.Failure("warehouse.notfound", $"Warehouse {warehouse.Id} not found");
            }

            existing.Name = warehouse.Name;
            existing.Code = warehouse.Code;
            existing.Description = warehouse.Description;
            existing.Address = warehouse.Address;
            existing.MaxUsers = warehouse.MaxUsers;
            existing.IsActive = warehouse.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            return Result<Warehouse>.Success(existing);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating warehouse {Id}", warehouse.Id);
            return Result<Warehouse>.Failure("warehouse.updatefailed", ex.Message);
        }
    }

    public async Task<Result> SetWarehouseActiveAsync(
        int warehouseId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var warehouse = await db.Warehouses.FindAsync([warehouseId], cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure("warehouse.notfound", $"Warehouse {warehouseId} not found");
            }

            warehouse.IsActive = isActive;
            warehouse.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling active state for warehouse {Id}", warehouseId);
            return Result.Failure("warehouse.updatefailed", ex.Message);
        }
    }

    public async Task<Result> DeleteWarehouseAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var warehouse = await db.Warehouses.FindAsync([warehouseId], cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure("warehouse.notfound", $"Warehouse {warehouseId} not found");
            }

            db.Warehouses.Remove(warehouse);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting warehouse {Id}", warehouseId);
            return Result.Failure("warehouse.deletefailed", ex.Message);
        }
    }
}
