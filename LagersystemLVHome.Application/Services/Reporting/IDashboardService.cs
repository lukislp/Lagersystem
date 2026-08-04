using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

public interface IDashboardService
{
    Task<DashboardData> GetDashboardDataAsync(int? warehouseId = null, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    Task<List<StockTrendData>> GetStockTrendsAsync(int days = 30, CancellationToken cancellationToken = default);
    Task<List<TopMoverData>> GetTopMoversAsync(int count = 10, CancellationToken cancellationToken = default);
    Task<List<CategoryValueData>> GetCategoryValuesAsync(CancellationToken cancellationToken = default);
    Task<List<WarehouseStockData>> GetWarehouseDistributionAsync(CancellationToken cancellationToken = default);
}
