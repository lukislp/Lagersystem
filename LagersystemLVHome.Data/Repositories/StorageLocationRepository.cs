using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Data.Repositories;

public class StorageLocationRepository : IStorageLocationRepository
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;

    public StorageLocationRepository(IDbContextFactory<InventoryDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IEnumerable<StorageLocation>> GetAllAsync(int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.StorageLocations
            .Include(s => s.ProductStorageLocations)
            .Where(s => s.WarehouseId == warehouseId)
            .OrderBy(s => s.Room)
            .ThenBy(s => s.Aisle)
            .ThenBy(s => s.Rack)
            .ThenBy(s => s.Shelf)
            .ToListAsync();
    }

    public async Task<StorageLocation?> GetByIdAsync(int id, int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.StorageLocations
            .Include(s => s.ProductStorageLocations)
                .ThenInclude(psl => psl.Product)
                .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(s => s.Id == id && s.WarehouseId == warehouseId);
    }

    public async Task<StorageLocation?> GetByCodeAsync(string code, int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.StorageLocations
            .Include(s => s.ProductStorageLocations)
                .ThenInclude(psl => psl.Product)
                .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(s => s.Code == code && s.WarehouseId == warehouseId);
    }

    public async Task<StorageLocation?> GetByQRCodeAsync(string qrCode, int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.StorageLocations
            .Include(s => s.ProductStorageLocations)
                .ThenInclude(psl => psl.Product)
                .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(s => s.QRCode == qrCode && s.WarehouseId == warehouseId);
    }

    public async Task<IEnumerable<StorageLocation>> GetByAisleAsync(string aisle, int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.StorageLocations
            .Include(s => s.ProductStorageLocations)
            .Where(s => s.Aisle == aisle && s.WarehouseId == warehouseId)
            .OrderBy(s => s.Rack)
            .ThenBy(s => s.Shelf)
            .ToListAsync();
    }

    public async Task<IEnumerable<StorageLocation>> GetByRoomAsync(string room, int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.StorageLocations
            .Include(s => s.ProductStorageLocations)
            .Where(s => s.Room == room && s.WarehouseId == warehouseId)
            .OrderBy(s => s.Aisle)
            .ThenBy(s => s.Rack)
            .ThenBy(s => s.Shelf)
            .ToListAsync();
    }

    public async Task<IEnumerable<string>> GetAllRoomsAsync(int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.StorageLocations
            .Where(s => !string.IsNullOrEmpty(s.Room) && s.WarehouseId == warehouseId)
            .Select(s => s.Room!)
            .Distinct()
            .OrderBy(r => r)
            .ToListAsync();
    }

    public async Task<IEnumerable<Product>> GetProductsByLocationAsync(int locationId, int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductStorageLocations)
            .Where(p => p.ProductStorageLocations.Any(psl => psl.StorageLocationId == locationId)
                && p.WarehouseId == warehouseId)
            .ToListAsync();
    }

    public async Task<StorageLocation> CreateAsync(StorageLocation location)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        location.CreatedAt = DateTime.UtcNow;
        location.UpdatedAt = DateTime.UtcNow;
        context.StorageLocations.Add(location);
        await context.SaveChangesAsync();
        return location;
    }

    public async Task<StorageLocation> UpdateAsync(StorageLocation location)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        location.UpdatedAt = DateTime.UtcNow;
        context.StorageLocations.Update(location);
        await context.SaveChangesAsync();
        return location;
    }

    public async Task<StorageLocation> GenerateQRCodeAsync(int locationId, string qrCodeContent)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var location = await context.StorageLocations.FindAsync(locationId);
        if (location == null)
            throw new InvalidOperationException($"Storage location with ID {locationId} not found");

        location.QRCode = qrCodeContent;
        location.QRCodeGeneratedAt = DateTime.UtcNow;
        location.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return location;
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var location = await context.StorageLocations.FindAsync(id);
        if (location != null)
        {
            context.StorageLocations.Remove(location);
            await context.SaveChangesAsync();
        }
    }

    public async Task<bool> CodeExistsAsync(string code, int warehouseId, int? excludeId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var query = context.StorageLocations
            .Where(s => s.Code == code && s.WarehouseId == warehouseId);

        if (excludeId.HasValue)
        {
            query = query.Where(s => s.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }
}
