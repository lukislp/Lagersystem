using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.API.DTOs;

namespace LagersystemLVHome.API.Controllers;

/// <summary>
/// API controller for stock alerts.
/// </summary>
[ApiController]
[Route("api/alerts")]
public class AlertsApiController : BaseApiController
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<AlertsApiController> _logger;

    public AlertsApiController(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<AlertsApiController> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResponse<AlertSummaryDto>), 200)]
    public async Task<ActionResult<ApiResponse<AlertSummaryDto>>> GetAlertSummary()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var warehouseId = CurrentWarehouseId;
            var now = DateTime.UtcNow;

            var products = await context.Products
                .Where(p => p.WarehouseId == warehouseId)
                .ToListAsync();

            var summary = new AlertSummaryDto
            {
                LowStock = products.Count(p => p.Quantity > 0 && p.Quantity <= p.MinQuantity),
                OutOfStock = products.Count(p => p.Quantity == 0),
                ExpiringSoon = products.Count(p => p.TrackExpiryDate
                    && p.ExpiryDate.HasValue
                    && p.ExpiryDate.Value > now
                    && p.ExpiryDate.Value <= now.AddDays(7)),
                Expired = products.Count(p => p.TrackExpiryDate
                    && p.ExpiryDate.HasValue
                    && p.ExpiryDate.Value <= now),
                Overstock = products.Count(p => p.Quantity > p.MinQuantity * 3),
                LastUpdated = DateTime.UtcNow
            };

            summary.TotalAlerts = summary.LowStock + summary.OutOfStock + summary.ExpiringSoon + summary.Expired;

            _logger.LogInformation("API: Alert summary fetched - Total: {Total}", summary.TotalAlerts);
            return Success(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching alert summary");
            return Error<AlertSummaryDto>("Error fetching alert summary", 500);
        }
    }

    [HttpGet("low-stock")]
    [ProducesResponseType(typeof(ApiResponse<List<StockAlertDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<StockAlertDto>>>> GetLowStockAlerts(
        [FromQuery] int limit = 50)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var warehouseId = CurrentWarehouseId;

            var alerts = await context.Products
                .Where(p => p.WarehouseId == warehouseId && p.Quantity > 0 && p.Quantity <= p.MinQuantity)
                .Include(p => p.Category)
                .OrderBy(p => p.Quantity)
                .Take(limit)
                .Select(p => new StockAlertDto
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    Barcode = p.Barcode,
                    AlertType = "LowStock",
                    CurrentQuantity = p.Quantity,
                    MinQuantity = p.MinQuantity,
                    MissingQuantity = p.MinQuantity - p.Quantity,
                    Severity = p.Quantity <= p.MinQuantity / 2 ? "Critical" : "High",
                    Message = $"{p.Name} has only {p.Quantity} left (minimum stock: {p.MinQuantity})",
                    CategoryName = p.Category != null ? p.Category.Name : null,
                    CategoryIcon = p.Category != null ? p.Category.Icon : null,
                    CreatedAt = DateTime.UtcNow
                })
                .ToListAsync();

            _logger.LogInformation("API: {Count} low stock alerts fetched", alerts.Count);
            return Success(alerts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching low stock alerts");
            return Error<List<StockAlertDto>>("Error fetching low stock alerts", 500);
        }
    }

    [HttpGet("out-of-stock")]
    [ProducesResponseType(typeof(ApiResponse<List<StockAlertDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<StockAlertDto>>>> GetOutOfStockAlerts(
        [FromQuery] int limit = 50)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var warehouseId = CurrentWarehouseId;

            var alerts = await context.Products
                .Where(p => p.WarehouseId == warehouseId && p.Quantity == 0)
                .Include(p => p.Category)
                .OrderBy(p => p.Name)
                .Take(limit)
                .Select(p => new StockAlertDto
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    Barcode = p.Barcode,
                    AlertType = "OutOfStock",
                    CurrentQuantity = 0,
                    MinQuantity = p.MinQuantity,
                    MissingQuantity = p.MinQuantity,
                    Severity = "Critical",
                    Message = $"{p.Name} is out of stock!",
                    CategoryName = p.Category != null ? p.Category.Name : null,
                    CategoryIcon = p.Category != null ? p.Category.Icon : null,
                    CreatedAt = DateTime.UtcNow
                })
                .ToListAsync();

            _logger.LogInformation("API: {Count} out of stock alerts fetched", alerts.Count);
            return Success(alerts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching out of stock alerts");
            return Error<List<StockAlertDto>>("Error fetching out of stock alerts", 500);
        }
    }

    [HttpGet("expiring-soon")]
    [ProducesResponseType(typeof(ApiResponse<List<StockAlertDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<StockAlertDto>>>> GetExpiringSoonAlerts(
        [FromQuery] int days = 7,
        [FromQuery] int limit = 50)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var warehouseId = CurrentWarehouseId;
            var now = DateTime.UtcNow;
            var threshold = now.AddDays(days);

            var alerts = await context.Products
                .Where(p => p.WarehouseId == warehouseId
                    && p.TrackExpiryDate
                    && p.ExpiryDate.HasValue
                    && p.ExpiryDate.Value > now
                    && p.ExpiryDate.Value <= threshold)
                .Include(p => p.Category)
                .OrderBy(p => p.ExpiryDate)
                .Take(limit)
                .Select(p => new StockAlertDto
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    Barcode = p.Barcode,
                    AlertType = "ExpiringSoon",
                    CurrentQuantity = p.Quantity,
                    ExpiryDate = p.ExpiryDate,
                    DaysUntilExpiry = (int)(p.ExpiryDate!.Value - now).TotalDays,
                    Severity = (p.ExpiryDate.Value - now).TotalDays <= 3 ? "Critical" : "High",
                    Message = $"{p.Name} expires in {(int)(p.ExpiryDate.Value - now).TotalDays} days",
                    CategoryName = p.Category != null ? p.Category.Name : null,
                    CategoryIcon = p.Category != null ? p.Category.Icon : null,
                    CreatedAt = DateTime.UtcNow
                })
                .ToListAsync();

            _logger.LogInformation("API: {Count} expiring soon alerts fetched", alerts.Count);
            return Success(alerts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching expiring soon alerts");
            return Error<List<StockAlertDto>>("Error fetching expiring soon alerts", 500);
        }
    }

    [HttpGet("expired")]
    [ProducesResponseType(typeof(ApiResponse<List<StockAlertDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<StockAlertDto>>>> GetExpiredAlerts(
        [FromQuery] int limit = 50)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var warehouseId = CurrentWarehouseId;
            var now = DateTime.UtcNow;

            var alerts = await context.Products
                .Where(p => p.WarehouseId == warehouseId
                    && p.TrackExpiryDate
                    && p.ExpiryDate.HasValue
                    && p.ExpiryDate.Value <= now)
                .Include(p => p.Category)
                .OrderBy(p => p.ExpiryDate)
                .Take(limit)
                .Select(p => new StockAlertDto
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    Barcode = p.Barcode,
                    AlertType = "Expired",
                    CurrentQuantity = p.Quantity,
                    ExpiryDate = p.ExpiryDate,
                    DaysUntilExpiry = (int)(p.ExpiryDate!.Value - now).TotalDays,
                    Severity = "Critical",
                    Message = $"{p.Name} ist seit {Math.Abs((int)(p.ExpiryDate.Value - now).TotalDays)} Tagen abgelaufen!",
                    CategoryName = p.Category != null ? p.Category.Name : null,
                    CategoryIcon = p.Category != null ? p.Category.Icon : null,
                    CreatedAt = DateTime.UtcNow
                })
                .ToListAsync();

            _logger.LogInformation("API: {Count} expired alerts fetched", alerts.Count);
            return Success(alerts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching expired alerts");
            return Error<List<StockAlertDto>>("Error fetching expired alerts", 500);
        }
    }

    [HttpGet("overstock")]
    [ProducesResponseType(typeof(ApiResponse<List<StockAlertDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<StockAlertDto>>>> GetOverstockAlerts(
        [FromQuery] int limit = 50)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var warehouseId = CurrentWarehouseId;

            var alerts = await context.Products
                .Where(p => p.WarehouseId == warehouseId && p.Quantity > p.MinQuantity * 3)
                .Include(p => p.Category)
                .OrderByDescending(p => p.Quantity)
                .Take(limit)
                .Select(p => new StockAlertDto
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    Barcode = p.Barcode,
                    AlertType = "Overstock",
                    CurrentQuantity = p.Quantity,
                    MinQuantity = p.MinQuantity,
                    Severity = "Medium",
                    Message = $"{p.Name} ist \u00fcberlagert ({p.Quantity} St\u00fcck, Mindest: {p.MinQuantity})",
                    CategoryName = p.Category != null ? p.Category.Name : null,
                    CategoryIcon = p.Category != null ? p.Category.Icon : null,
                    CreatedAt = DateTime.UtcNow
                })
                .ToListAsync();

            _logger.LogInformation("API: {Count} overstock alerts fetched", alerts.Count);
            return Success(alerts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching overstock alerts");
            return Error<List<StockAlertDto>>("Error fetching overstock alerts", 500);
        }
    }

    [HttpGet("all")]
    [ProducesResponseType(typeof(ApiResponse<List<StockAlertDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<StockAlertDto>>>> GetAllAlerts(
        [FromQuery] int limit = 100)
    {
        try
        {
            var allAlerts = new List<StockAlertDto>();

            var lowStock = await GetLowStockAlerts(limit / 4);
            if (lowStock.Result is OkObjectResult lowStockResult && lowStockResult.Value is ApiResponse<List<StockAlertDto>> lowStockResponse)
            {
                allAlerts.AddRange(lowStockResponse.Data ?? new List<StockAlertDto>());
            }

            var outOfStock = await GetOutOfStockAlerts(limit / 4);
            if (outOfStock.Result is OkObjectResult outOfStockResult && outOfStockResult.Value is ApiResponse<List<StockAlertDto>> outOfStockResponse)
            {
                allAlerts.AddRange(outOfStockResponse.Data ?? new List<StockAlertDto>());
            }

            var expiring = await GetExpiringSoonAlerts(7, limit / 4);
            if (expiring.Result is OkObjectResult expiringResult && expiringResult.Value is ApiResponse<List<StockAlertDto>> expiringResponse)
            {
                allAlerts.AddRange(expiringResponse.Data ?? new List<StockAlertDto>());
            }

            var expired = await GetExpiredAlerts(limit / 4);
            if (expired.Result is OkObjectResult expiredResult && expiredResult.Value is ApiResponse<List<StockAlertDto>> expiredResponse)
            {
                allAlerts.AddRange(expiredResponse.Data ?? new List<StockAlertDto>());
            }

            var severityOrder = new Dictionary<string, int>
            {
                { "Critical", 0 },
                { "High", 1 },
                { "Medium", 2 },
                { "Low", 3 }
            };

            allAlerts = allAlerts
                .OrderBy(a => severityOrder.GetValueOrDefault(a.Severity, 99))
                .Take(limit)
                .ToList();

            _logger.LogInformation("API: {Count} total alerts fetched", allAlerts.Count);
            return Success(allAlerts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching all alerts");
            return Error<List<StockAlertDto>>("Error fetching all alerts", 500);
        }
    }
}
