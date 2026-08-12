using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Data.Repositories;

public interface IStockMovementRepository
{
    Task<IEnumerable<StockMovement>> GetAllAsync(int warehouseId);
    Task<IEnumerable<StockMovement>> GetByProductAsync(int productId, int warehouseId);
    Task<IEnumerable<StockMovement>> GetRecentAsync(int count, int warehouseId);
    Task<IEnumerable<StockMovement>> GetTodayMovementsAsync(int warehouseId);
    Task<StockMovement> CreateAsync(StockMovement movement);
}
