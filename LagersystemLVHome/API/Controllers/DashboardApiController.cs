using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.API.DTOs;

namespace LagersystemLVHome.API.Controllers;

/// <summary>
/// API controller for dashboard statistics.
/// </summary>
[ApiController]
[Route("api/dashboard")]
public class DashboardApiController : BaseApiController
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<DashboardApiController> _logger;

    public DashboardApiController(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<DashboardApiController> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    [HttpGet("stats")]
    [ProducesResponseType(typeof(ApiResponse<DashboardStatsDto>), 200)]
    public async Task<ActionResult<ApiResponse<DashboardStatsDto>>> GetStats()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var warehouseId = CurrentWarehouseId;

            var totalProducts = await context.Products
                .CountAsync(p => p.WarehouseId == warehouseId);

            var lowStockProducts = await context.Products
                .CountAsync(p => p.WarehouseId == warehouseId && p.Quantity <= p.MinQuantity);

            var categories = await context.Categories
                .CountAsync(c => c.WarehouseId == warehouseId && c.IsActive);

            var storageLocations = await context.StorageLocations
                .CountAsync(s => s.WarehouseId == warehouseId);

            // Load first, calculate in memory (SQLite decimal issue)
            var products = await context.Products
                .Where(p => p.WarehouseId == warehouseId)
                .Select(p => new { p.Quantity, p.Price })
                .ToListAsync();

            var totalValue = products.Sum(p => p.Quantity * p.Price);

            var topCategories = await context.Categories
                .Where(c => c.WarehouseId == warehouseId && c.IsActive)
                .Select(c => new CategoryStatDto
                {
                    CategoryName = c.Name,
                    ProductCount = c.Products.Count
                })
                .OrderByDescending(c => c.ProductCount)
                .Take(5)
                .ToListAsync();

            var lowStockItems = await context.Products
                .Where(p => p.WarehouseId == warehouseId && p.Quantity <= p.MinQuantity)
                .Select(p => new ProductStatDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Quantity = p.Quantity,
                    MinStock = p.MinQuantity
                })
                .OrderBy(p => p.Quantity)
                .Take(10)
                .ToListAsync();

            var stats = new DashboardStatsDto
            {
                TotalProducts = totalProducts,
                LowStockProducts = lowStockProducts,
                Categories = categories,
                StorageLocations = storageLocations,
                TotalValue = (double)totalValue,
                TopCategories = topCategories,
                LowStockItems = lowStockItems
            };

            _logger.LogInformation("API: Dashboard stats fetched by user {UserId}", CurrentUserId);

            return Success(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching dashboard stats");
            return Error<DashboardStatsDto>("Error fetching dashboard stats", 500);
        }
    }

    [HttpGet("movements")]
    [ProducesResponseType(typeof(ApiResponse<List<StockMovementDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<StockMovementDto>>>> GetRecentMovements(
        [FromQuery] int limit = 20)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var movements = await context.StockMovements
                .Include(sm => sm.Product)
                .Where(sm => sm.WarehouseId == CurrentWarehouseId)
                .OrderByDescending(sm => sm.Timestamp)
                .Take(limit)
                .Select(sm => new StockMovementDto
                {
                    Id = sm.Id,
                    ProductId = sm.ProductId,
                    ProductName = sm.Product.Name,
                    Quantity = sm.QuantityChange,
                    Type = sm.Type.ToString(),
                    Reason = sm.Notes,
                    Timestamp = sm.Timestamp,
                    Username = null
                })
                .ToListAsync();

            _logger.LogInformation("API: {Count} recent movements fetched by user {UserId}",
                movements.Count, CurrentUserId);

            return Success(movements);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching recent movements");
            return Error<List<StockMovementDto>>("Error fetching movements", 500);
        }
    }
}
