using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Data.Repositories;

public class StockMovementRepository : IStockMovementRepository
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;

    public StockMovementRepository(IDbContextFactory<InventoryDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IEnumerable<StockMovement>> GetAllAsync(int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.StockMovements
            .Include(sm => sm.Product)
                .ThenInclude(p => p!.Category)
            .Where(sm => sm.WarehouseId == warehouseId)
            .OrderByDescending(sm => sm.Timestamp)
            .ToListAsync();
    }

    public async Task<IEnumerable<StockMovement>> GetByProductAsync(int productId, int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.StockMovements
            .Include(sm => sm.Product)
            .Where(sm => sm.ProductId == productId && sm.WarehouseId == warehouseId)
            .OrderByDescending(sm => sm.Timestamp)
            .ToListAsync();
    }

    public async Task<IEnumerable<StockMovement>> GetRecentAsync(int count, int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.StockMovements
            .Include(sm => sm.Product)
                .ThenInclude(p => p!.Category)
            .Where(sm => sm.WarehouseId == warehouseId)
            .OrderByDescending(sm => sm.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    public async Task<IEnumerable<StockMovement>> GetTodayMovementsAsync(int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var today = DateTime.UtcNow.Date;
        return await context.StockMovements
            .Where(sm => sm.Timestamp >= today && sm.WarehouseId == warehouseId)
            .ToListAsync();
    }

    public async Task<StockMovement> CreateAsync(StockMovement movement)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        movement.Timestamp = DateTime.UtcNow;
        context.StockMovements.Add(movement);
        await context.SaveChangesAsync();
        return movement;
    }
}
