using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.API.DTOs;
using LagersystemLVHome.Application.Services;

namespace LagersystemLVHome.API.Controllers;

/// <summary>
/// API controller for analytics and extended dashboard data.
/// </summary>
[ApiController]
[Route("api/analytics")]
public class AnalyticsApiController : BaseApiController
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IDashboardService _dashboardService;
    private readonly ILogger<AnalyticsApiController> _logger;

    public AnalyticsApiController(
        IDbContextFactory<InventoryDbContext> contextFactory,
        IDashboardService dashboardService,
        ILogger<AnalyticsApiController> logger)
    {
        _contextFactory = contextFactory;
        _dashboardService = dashboardService;
        _logger = logger;
    }

    [HttpGet("standard")]
    [ProducesResponseType(typeof(ApiResponse<AnalyticsStandardDto>), 200)]
    public async Task<ActionResult<ApiResponse<AnalyticsStandardDto>>> GetStandardAnalytics()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var warehouseId = CurrentWarehouseId;
            var now = DateTime.UtcNow;

            // Basic statistics
            var totalProducts = await context.Products
                .CountAsync(p => p.WarehouseId == warehouseId);

            var totalCategories = await context.Categories
                .CountAsync(c => c.WarehouseId == warehouseId && c.IsActive);

            var lowStockCount = await context.Products
                .CountAsync(p => p.WarehouseId == warehouseId && p.Quantity <= p.MinQuantity);

            var expiringSoonCount = await context.Products
                .CountAsync(p => p.WarehouseId == warehouseId
                    && p.TrackExpiryDate
                    && p.ExpiryDate.HasValue
                    && p.ExpiryDate.Value > now
                    && p.ExpiryDate.Value <= now.AddDays(7));

            // Load products first, then calculate sum in memory (SQLite decimal issue)
            var products = await context.Products
                .Where(p => p.WarehouseId == warehouseId)
                .Select(p => new { p.Quantity, p.Price })
                .ToListAsync();

            var totalValue = products.Sum(p => p.Quantity * p.Price);

            // Last 7 days movements
            var last7Days = now.AddDays(-7);
            var recentMovements = await context.StockMovements
                .Where(sm => sm.WarehouseId == warehouseId && sm.Timestamp >= last7Days)
                .GroupBy(sm => sm.Timestamp.Date)
                .Select(g => new MovementTrendDto
                {
                    Date = g.Key,
                    InCount = g.Count(m => m.QuantityChange > 0),
                    OutCount = g.Count(m => m.QuantityChange < 0),
                    NetChange = g.Sum(m => m.QuantityChange)
                })
                .OrderBy(m => m.Date)
                .ToListAsync();

            // Top 5 categories by value - load first, then calculate
            var categoryProducts = await context.Products
                .Where(p => p.WarehouseId == warehouseId && p.CategoryId > 0)
                .Include(p => p.Category)
                .Select(p => new
                {
                    p.CategoryId,
                    CategoryName = p.Category!.Name,
                    CategoryIcon = p.Category.Icon,
                    p.Quantity,
                    p.Price
                })
                .ToListAsync();

            var topCategories = categoryProducts
                .GroupBy(p => new { p.CategoryId, p.CategoryName, p.CategoryIcon })
                .Select(g => new CategoryValueDto
                {
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.CategoryName,
                    CategoryIcon = g.Key.CategoryIcon,
                    ProductCount = g.Count(),
                    TotalValue = g.Sum(p => p.Quantity * p.Price)
                })
                .OrderByDescending(c => c.TotalValue)
                .Take(5)
                .ToList();

            // Critical stock levels (top 10)
            var lowStockItems = await context.Products
                .Where(p => p.WarehouseId == warehouseId && p.Quantity <= p.MinQuantity)
                .Include(p => p.Category)
                .OrderBy(p => p.Quantity)
                .Take(10)
                .Select(p => new LowStockItemDto
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    Barcode = p.Barcode,
                    CurrentQuantity = p.Quantity,
                    MinQuantity = p.MinQuantity,
                    MissingQuantity = p.MinQuantity - p.Quantity,
                    CategoryName = p.Category != null ? p.Category.Name : null
                })
                .ToListAsync();

            var analytics = new AnalyticsStandardDto
            {
                TotalProducts = totalProducts,
                TotalCategories = totalCategories,
                LowStockCount = lowStockCount,
                ExpiringSoonCount = expiringSoonCount,
                TotalInventoryValue = totalValue,
                RecentMovements = recentMovements,
                TopCategories = topCategories,
                LowStockItems = lowStockItems
            };

            _logger.LogInformation("API: Standard analytics fetched by user {UserId}", CurrentUserId);
            return Success(analytics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching standard analytics");
            return Error<AnalyticsStandardDto>("Error fetching analytics", 500);
        }
    }

    [HttpGet("enhanced")]
    [ProducesResponseType(typeof(ApiResponse<AnalyticsEnhancedDto>), 200)]
    public async Task<ActionResult<ApiResponse<AnalyticsEnhancedDto>>> GetEnhancedAnalytics(
        [FromQuery] string timeRange = "30days")
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var warehouseId = CurrentWarehouseId;
            var now = DateTime.UtcNow;

            // Determine time range
            var startDate = timeRange switch
            {
                "7days" => now.AddDays(-7),
                "30days" => now.AddDays(-30),
                "90days" => now.AddDays(-90),
                "year" => now.AddYears(-1),
                _ => now.AddDays(-30)
            };

            // Basic product statistics
            var products = await context.Products
                .Where(p => p.WarehouseId == warehouseId)
                .ToListAsync();

            var totalProducts = products.Count;
            var activeProducts = products.Count(p => p.Quantity > 0);
            var inactiveProducts = products.Count(p => p.Quantity == 0);

            var totalCategories = await context.Categories
                .CountAsync(c => c.WarehouseId == warehouseId && c.IsActive);

            var totalStorageLocations = await context.StorageLocations
                .CountAsync(sl => sl.WarehouseId == warehouseId && sl.IsActive);

            var totalRooms = await context.StorageLocations
                .Where(sl => sl.WarehouseId == warehouseId && sl.Room != null)
                .Select(sl => sl.Room)
                .Distinct()
                .CountAsync();

            // Financial metrics
            var totalValue = products.Sum(p => p.Quantity * p.Price);
            var avgPurchasePrice = products.Any() ? products.Average(p => p.Price) : 0;
            var potentialSalesValue = products.Sum(p => p.Quantity * (p.Price * 1.3m)); // 30% markup
            var potentialProfit = potentialSalesValue - totalValue;

            // Stock metrics
            var lowStockCount = products.Count(p => p.Quantity <= p.MinQuantity && p.Quantity > 0);
            var outOfStockCount = products.Count(p => p.Quantity == 0);
            var overstockedCount = products.Count(p => p.Quantity > p.MinQuantity * 3);
            var expiringSoonCount = products.Count(p => p.TrackExpiryDate
                && p.ExpiryDate.HasValue
                && p.ExpiryDate.Value > now
                && p.ExpiryDate.Value <= now.AddDays(7));
            var expiredCount = products.Count(p => p.TrackExpiryDate
                && p.ExpiryDate.HasValue
                && p.ExpiryDate.Value <= now);

            // Movement metrics
            var todayStart = now.Date;
            var weekStart = now.AddDays(-7);
            var monthStart = now.AddDays(-30);

            var todayMovements = await context.StockMovements
                .CountAsync(sm => sm.WarehouseId == warehouseId && sm.Timestamp >= todayStart);

            var weekMovements = await context.StockMovements
                .CountAsync(sm => sm.WarehouseId == warehouseId && sm.Timestamp >= weekStart);

            var monthMovements = await context.StockMovements
                .CountAsync(sm => sm.WarehouseId == warehouseId && sm.Timestamp >= monthStart);

            // Movement trends (7 days)
            var movementTrend7Days = await context.StockMovements
                .Where(sm => sm.WarehouseId == warehouseId && sm.Timestamp >= now.AddDays(-7))
                .GroupBy(sm => sm.Timestamp.Date)
                .Select(g => new MovementTrendDto
                {
                    Date = g.Key,
                    InCount = g.Count(m => m.QuantityChange > 0),
                    OutCount = g.Count(m => m.QuantityChange < 0),
                    NetChange = g.Sum(m => m.QuantityChange),
                    Label = g.Key.ToString("ddd")
                })
                .OrderBy(m => m.Date)
                .ToListAsync();

            // Movement trends (30 days)
            var movementTrend30Days = await context.StockMovements
                .Where(sm => sm.WarehouseId == warehouseId && sm.Timestamp >= now.AddDays(-30))
                .GroupBy(sm => sm.Timestamp.Date)
                .Select(g => new MovementTrendDto
                {
                    Date = g.Key,
                    InCount = g.Count(m => m.QuantityChange > 0),
                    OutCount = g.Count(m => m.QuantityChange < 0),
                    NetChange = g.Sum(m => m.QuantityChange)
                })
                .OrderBy(m => m.Date)
                .ToListAsync();

            // Category distribution - load first, calculate in memory
            var categoryProducts = await context.Products
                .Where(p => p.WarehouseId == warehouseId && p.CategoryId > 0)
                .Include(p => p.Category)
                .Select(p => new
                {
                    p.CategoryId,
                    CategoryName = p.Category!.Name,
                    CategoryIcon = p.Category.Icon,
                    p.Quantity,
                    p.Price
                })
                .ToListAsync();

            var categoryDistribution = categoryProducts
                .GroupBy(p => new { p.CategoryId, p.CategoryName, p.CategoryIcon })
                .Select(g => new CategoryValueDto
                {
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.CategoryName,
                    CategoryIcon = g.Key.CategoryIcon,
                    ProductCount = g.Count(),
                    TotalValue = g.Sum(p => p.Quantity * p.Price)
                })
                .ToList();

            if (totalValue > 0)
            {
                foreach (var cat in categoryDistribution)
                {
                    cat.Percentage = (double)(cat.TotalValue / totalValue * 100);
                }
            }
            categoryDistribution = categoryDistribution.OrderByDescending(c => c.TotalValue).ToList();

            // Storage utilization
            var storageUtilization = await context.ProductStorageLocations
                .Where(psl => psl.StorageLocation.WarehouseId == warehouseId)
                .Include(psl => psl.StorageLocation)
                .GroupBy(psl => new
                {
                    psl.StorageLocationId,
                    psl.StorageLocation.Code,
                    psl.StorageLocation.Name,
                    psl.StorageLocation.Room,
                    psl.StorageLocation.MaxCapacity
                })
                .Select(g => new StorageUtilizationDto
                {
                    StorageLocationId = g.Key.StorageLocationId,
                    Code = g.Key.Code,
                    Name = g.Key.Name,
                    RoomName = g.Key.Room,
                    ProductCount = g.Count(),
                    TotalQuantity = g.Sum(psl => psl.Quantity),
                    MaxCapacity = g.Key.MaxCapacity,
                    UtilizationPercentage = g.Key.MaxCapacity.HasValue && g.Key.MaxCapacity.Value > 0
                        ? (double)g.Sum(psl => psl.Quantity) / g.Key.MaxCapacity.Value * 100
                        : 0
                })
                .OrderByDescending(s => s.UtilizationPercentage)
                .Take(10)
                .ToListAsync();

            // Top moved products
            var topMovedProducts = await context.StockMovements
                .Where(sm => sm.WarehouseId == warehouseId && sm.Timestamp >= startDate)
                .Include(sm => sm.Product)
                .ThenInclude(p => p.Category)
                .GroupBy(sm => new
                {
                    sm.ProductId,
                    sm.Product.Name,
                    sm.Product.Barcode,
                    sm.Product.Quantity,
                    CategoryName = sm.Product.Category != null ? sm.Product.Category.Name : null
                })
                .Select(g => new TopProductDto
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.Name,
                    Barcode = g.Key.Barcode,
                    Quantity = g.Key.Quantity,
                    MovementCount = g.Count(),
                    CategoryName = g.Key.CategoryName
                })
                .OrderByDescending(p => p.MovementCount)
                .Take(10)
                .ToListAsync();

            // Most valuable products
            var mostValuableProducts = products
                .OrderByDescending(p => p.Quantity * p.Price)
                .Take(10)
                .Select(p => new TopProductDto
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    Barcode = p.Barcode,
                    Quantity = p.Quantity,
                    Value = p.Quantity * p.Price,
                    CategoryName = p.Category?.Name
                })
                .ToList();

            // Critically low stock
            var criticalLowStock = products
                .Where(p => p.Quantity <= p.MinQuantity)
                .OrderBy(p => p.Quantity)
                .Take(10)
                .Select(p => new LowStockItemDto
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    Barcode = p.Barcode,
                    CurrentQuantity = p.Quantity,
                    MinQuantity = p.MinQuantity,
                    MissingQuantity = p.MinQuantity - p.Quantity,
                    CategoryName = p.Category?.Name
                })
                .ToList();

            // Expiring products
            var expiringProducts = products
                .Where(p => p.TrackExpiryDate && p.ExpiryDate.HasValue)
                .Where(p => p.ExpiryDate!.Value <= now.AddDays(30))
                .OrderBy(p => p.ExpiryDate)
                .Take(10)
                .Select(p => new ExpiringProductDto
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    Barcode = p.Barcode,
                    ExpiryDate = p.ExpiryDate,
                    DaysUntilExpiry = (p.ExpiryDate!.Value - now).Days,
                    Quantity = p.Quantity,
                    CategoryName = p.Category?.Name,
                    Status = p.ExpiryDate.Value <= now ? "Expired" : "Expiring"
                })
                .ToList();

            // Performance indicators
            var stockTurnoverRate = totalValue > 0 && monthMovements > 0
                ? (double)monthMovements / (double)totalProducts * 100
                : 0;

            var avgStockAge = products.Any()
                ? products.Average(p => (now - p.CreatedAt).TotalDays)
                : 0;

            var inventoryAccuracy = totalProducts > 0
                ? (double)(totalProducts - outOfStockCount) / totalProducts * 100
                : 100;

            var analytics = new AnalyticsEnhancedDto
            {
                // Base
                TotalProducts = totalProducts,
                ActiveProducts = activeProducts,
                InactiveProducts = inactiveProducts,
                TotalCategories = totalCategories,
                TotalStorageLocations = totalStorageLocations,
                TotalRooms = totalRooms,

                // Financial
                TotalInventoryValue = totalValue,
                AveragePurchasePrice = avgPurchasePrice,
                PotentialSalesValue = potentialSalesValue,
                PotentialProfit = potentialProfit,

                // Stock levels
                LowStockCount = lowStockCount,
                OutOfStockCount = outOfStockCount,
                OverstockedCount = overstockedCount,
                ExpiringSoonCount = expiringSoonCount,
                ExpiredCount = expiredCount,

                // Movements
                TodayMovements = todayMovements,
                WeekMovements = weekMovements,
                MonthMovements = monthMovements,

                // Trends
                MovementTrend7Days = movementTrend7Days,
                MovementTrend30Days = movementTrend30Days,
                CategoryDistribution = categoryDistribution,
                StorageUtilization = storageUtilization,

                // Top lists
                TopMovedProducts = topMovedProducts,
                MostValuableProducts = mostValuableProducts,
                CriticalLowStock = criticalLowStock,
                ExpiringProducts = expiringProducts,

                // Performance
                StockTurnoverRate = stockTurnoverRate,
                AverageStockAge = avgStockAge,
                InventoryAccuracy = inventoryAccuracy
            };

            _logger.LogInformation("API: Enhanced analytics fetched by user {UserId} for timeRange {TimeRange}",
                CurrentUserId, timeRange);
            return Success(analytics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching enhanced analytics");
            return Error<AnalyticsEnhancedDto>("Error fetching enhanced analytics", 500);
        }
    }

    [HttpGet("movements/trend")]
    [ProducesResponseType(typeof(ApiResponse<List<MovementTrendDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<MovementTrendDto>>>> GetMovementTrend(
        [FromQuery] string period = "7days")
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var warehouseId = CurrentWarehouseId;
            var now = DateTime.UtcNow;

            var startDate = period switch
            {
                "24hours" => now.AddHours(-24),
                "7days" => now.AddDays(-7),
                "30days" => now.AddDays(-30),
                "90days" => now.AddDays(-90),
                _ => now.AddDays(-7)
            };

            var movements = await context.StockMovements
                .Where(sm => sm.WarehouseId == warehouseId && sm.Timestamp >= startDate)
                .GroupBy(sm => sm.Timestamp.Date)
                .Select(g => new MovementTrendDto
                {
                    Date = g.Key,
                    InCount = g.Count(m => m.QuantityChange > 0),
                    OutCount = g.Count(m => m.QuantityChange < 0),
                    NetChange = g.Sum(m => m.QuantityChange)
                })
                .OrderBy(m => m.Date)
                .ToListAsync();

            _logger.LogInformation("API: Movement trend fetched for period {Period}", period);
            return Success(movements);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching movement trend");
            return Error<List<MovementTrendDto>>("Error fetching movement trend", 500);
        }
    }

    [HttpGet("categories/distribution")]
    [ProducesResponseType(typeof(ApiResponse<List<CategoryValueDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<CategoryValueDto>>>> GetCategoryDistribution()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var warehouseId = CurrentWarehouseId;

            // Load first, calculate in memory
            var categoryProducts = await context.Products
                .Where(p => p.WarehouseId == warehouseId && p.CategoryId > 0)
                .Include(p => p.Category)
                .Select(p => new
                {
                    p.CategoryId,
                    CategoryName = p.Category!.Name,
                    CategoryIcon = p.Category.Icon,
                    p.Quantity,
                    p.Price
                })
                .ToListAsync();

            var distribution = categoryProducts
                .GroupBy(p => new { p.CategoryId, p.CategoryName, p.CategoryIcon })
                .Select(g => new CategoryValueDto
                {
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.CategoryName,
                    CategoryIcon = g.Key.CategoryIcon,
                    ProductCount = g.Count(),
                    TotalValue = g.Sum(p => p.Quantity * p.Price)
                })
                .ToList();

            var totalValue = distribution.Sum(c => c.TotalValue);
            if (totalValue > 0)
            {
                foreach (var cat in distribution)
                {
                    cat.Percentage = (double)(cat.TotalValue / totalValue * 100);
                }
            }

            distribution = distribution.OrderByDescending(c => c.TotalValue).ToList();

            _logger.LogInformation("API: Category distribution fetched");
            return Success(distribution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching category distribution");
            return Error<List<CategoryValueDto>>("Error fetching category distribution", 500);
        }
    }

    [HttpGet("storage/utilization")]
    [ProducesResponseType(typeof(ApiResponse<List<StorageUtilizationDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<StorageUtilizationDto>>>> GetStorageUtilization(
        [FromQuery] int limit = 10)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var warehouseId = CurrentWarehouseId;

            var utilization = await context.ProductStorageLocations
                .Where(psl => psl.StorageLocation.WarehouseId == warehouseId)
                .Include(psl => psl.StorageLocation)
                .GroupBy(psl => new
                {
                    psl.StorageLocationId,
                    psl.StorageLocation.Code,
                    psl.StorageLocation.Name,
                    psl.StorageLocation.Room,
                    psl.StorageLocation.MaxCapacity
                })
                .Select(g => new StorageUtilizationDto
                {
                    StorageLocationId = g.Key.StorageLocationId,
                    Code = g.Key.Code,
                    Name = g.Key.Name,
                    RoomName = g.Key.Room,
                    ProductCount = g.Count(),
                    TotalQuantity = g.Sum(psl => psl.Quantity),
                    MaxCapacity = g.Key.MaxCapacity,
                    UtilizationPercentage = g.Key.MaxCapacity.HasValue && g.Key.MaxCapacity.Value > 0
                        ? (double)g.Sum(psl => psl.Quantity) / g.Key.MaxCapacity.Value * 100
                        : 0
                })
                .OrderByDescending(s => s.UtilizationPercentage)
                .Take(limit)
                .ToListAsync();

            _logger.LogInformation("API: Storage utilization fetched");
            return Success(utilization);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching storage utilization");
            return Error<List<StorageUtilizationDto>>("Error fetching storage utilization", 500);
        }
    }
}
