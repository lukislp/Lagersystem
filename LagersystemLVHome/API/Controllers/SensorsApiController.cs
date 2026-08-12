using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.API.DTOs;

namespace LagersystemLVHome.API.Controllers;

/// <summary>
/// API controller for Home Assistant sensors.
/// </summary>
[ApiController]
[Route("api/sensors")]
public class SensorsApiController : BaseApiController
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<SensorsApiController> _logger;

    public SensorsApiController(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<SensorsApiController> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Sensor: Total inventory value.
    /// </summary>
    [HttpGet("inventory-value")]
    [ProducesResponseType(typeof(ApiResponse<SensorValueDto>), 200)]
    public async Task<ActionResult<ApiResponse<SensorValueDto>>> GetInventoryValueSensor()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var warehouseId = CurrentWarehouseId;

            var products = await context.Products
                .Where(p => p.WarehouseId == warehouseId)
                .Select(p => new { p.Quantity, p.Price })
                .ToListAsync();

            var totalValue = products.Sum(p => p.Quantity * p.Price);
            var productCount = products.Count;

            var sensor = new SensorValueDto
            {
                EntityId = "sensor.inventory_total_value",
                Name = "Lagerbestandswert",
                Value = (double)totalValue,
                Unit = "\u20ac",
                Icon = "mdi:currency-eur",
                State = totalValue > 0 ? "ok" : "warning",
                Attributes = new Dictionary<string, object>
                {
                    { "total_products", productCount },
                    { "warehouse_id", warehouseId },
                    { "currency", "EUR" },
                    { "formatted_value", $"{totalValue:N2} \u20ac" }
                },
                LastUpdated = DateTime.UtcNow
            };

            _logger.LogInformation("API: Inventory value sensor: {Value}", totalValue);
            return Success(sensor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching inventory value sensor");
            return Error<SensorValueDto>("Error fetching inventory value sensor", 500);
        }
    }

    /// <summary>
    /// Sensor: Total product count.
    /// </summary>
    [HttpGet("total-products")]
    [ProducesResponseType(typeof(ApiResponse<SensorValueDto>), 200)]
    public async Task<ActionResult<ApiResponse<SensorValueDto>>> GetTotalProductsSensor()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var warehouseId = CurrentWarehouseId;
            var totalProducts = await context.Products.CountAsync(p => p.WarehouseId == warehouseId);
            var activeProducts = await context.Products.CountAsync(p => p.WarehouseId == warehouseId && p.Quantity > 0);

            var sensor = new SensorValueDto
            {
                EntityId = "sensor.inventory_total_products",
                Name = "Anzahl Produkte",
                Value = totalProducts,
                Unit = "St\u00fcck",
                Icon = "mdi:package-variant",
                State = totalProducts > 0 ? "ok" : "warning",
                Attributes = new Dictionary<string, object>
                {
                    { "active_products", activeProducts },
                    { "inactive_products", totalProducts - activeProducts },
                    { "warehouse_id", warehouseId }
                },
                LastUpdated = DateTime.UtcNow
            };

            return Success(sensor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching total products sensor");
            return Error<SensorValueDto>("Error fetching total products sensor", 500);
        }
    }

    /// <summary>
    /// Sensor: Low stock product count.
    /// </summary>
    [HttpGet("low-stock-count")]
    [ProducesResponseType(typeof(ApiResponse<SensorValueDto>), 200)]
    public async Task<ActionResult<ApiResponse<SensorValueDto>>> GetLowStockCountSensor()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var warehouseId = CurrentWarehouseId;
            var lowStockCount = await context.Products
                .CountAsync(p => p.WarehouseId == warehouseId && p.Quantity > 0 && p.Quantity <= p.MinQuantity);

            var criticalCount = await context.Products
                .CountAsync(p => p.WarehouseId == warehouseId && p.Quantity > 0 && p.Quantity <= p.MinQuantity / 2);

            var state = lowStockCount == 0 ? "ok" : (criticalCount > 0 ? "critical" : "warning");

            var sensor = new SensorValueDto
            {
                EntityId = "sensor.inventory_low_stock_count",
                Name = "Produkte mit niedrigem Bestand",
                Value = lowStockCount,
                Unit = "St\u00fcck",
                Icon = state == "critical" ? "mdi:alert-circle" : "mdi:alert",
                State = state,
                Attributes = new Dictionary<string, object>
                {
                    { "critical_count", criticalCount },
                    { "warehouse_id", warehouseId },
                    { "severity", state }
                },
                LastUpdated = DateTime.UtcNow
            };

            return Success(sensor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching low stock count sensor");
            return Error<SensorValueDto>("Error fetching low stock count sensor", 500);
        }
    }

    /// <summary>
    /// Sensor: Expiring product count.
    /// </summary>
    [HttpGet("expiry-warnings")]
    [ProducesResponseType(typeof(ApiResponse<SensorValueDto>), 200)]
    public async Task<ActionResult<ApiResponse<SensorValueDto>>> GetExpiryWarningsSensor()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var warehouseId = CurrentWarehouseId;
            var now = DateTime.UtcNow;

            var expiringSoon = await context.Products
                .CountAsync(p => p.WarehouseId == warehouseId
                    && p.TrackExpiryDate
                    && p.ExpiryDate.HasValue
                    && p.ExpiryDate.Value > now
                    && p.ExpiryDate.Value <= now.AddDays(7));

            var expired = await context.Products
                .CountAsync(p => p.WarehouseId == warehouseId
                    && p.TrackExpiryDate
                    && p.ExpiryDate.HasValue
                    && p.ExpiryDate.Value <= now);

            var totalWarnings = expiringSoon + expired;
            var state = expired > 0 ? "critical" : (expiringSoon > 0 ? "warning" : "ok");

            var sensor = new SensorValueDto
            {
                EntityId = "sensor.inventory_expiry_warnings",
                Name = "Ablaufwarnungen",
                Value = totalWarnings,
                Unit = "St\u00fcck",
                Icon = state == "critical" ? "mdi:calendar-alert" : "mdi:calendar-clock",
                State = state,
                Attributes = new Dictionary<string, object>
                {
                    { "expiring_soon", expiringSoon },
                    { "expired", expired },
                    { "warehouse_id", warehouseId },
                    { "severity", state }
                },
                LastUpdated = DateTime.UtcNow
            };

            return Success(sensor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching expiry warnings sensor");
            return Error<SensorValueDto>("Error fetching expiry warnings sensor", 500);
        }
    }

    /// <summary>
    /// Sensor: Storage utilization percentage.
    /// </summary>
    [HttpGet("storage-utilization")]
    [ProducesResponseType(typeof(ApiResponse<SensorValueDto>), 200)]
    public async Task<ActionResult<ApiResponse<SensorValueDto>>> GetStorageUtilizationSensor()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var warehouseId = CurrentWarehouseId;

            var storageLocations = await context.StorageLocations
                .Where(sl => sl.WarehouseId == warehouseId && sl.IsActive)
                .ToListAsync();

            var totalCapacity = storageLocations.Where(sl => sl.MaxCapacity.HasValue).Sum(sl => sl.MaxCapacity!.Value);

            var productQuantities = await context.ProductStorageLocations
                .Where(psl => psl.StorageLocation.WarehouseId == warehouseId)
                .SumAsync(psl => psl.Quantity);

            var utilizationPercent = totalCapacity > 0 ? (double)productQuantities / totalCapacity * 100 : 0;
            var state = utilizationPercent >= 90 ? "critical" : (utilizationPercent >= 75 ? "warning" : "ok");

            var sensor = new SensorValueDto
            {
                EntityId = "sensor.inventory_storage_utilization",
                Name = "Lagerauslastung",
                Value = Math.Round(utilizationPercent, 1),
                Unit = "%",
                Icon = "mdi:warehouse",
                State = state,
                Attributes = new Dictionary<string, object>
                {
                    { "total_capacity", totalCapacity },
                    { "used_capacity", productQuantities },
                    { "available_capacity", totalCapacity - productQuantities },
                    { "warehouse_id", warehouseId },
                    { "severity", state }
                },
                LastUpdated = DateTime.UtcNow
            };

            return Success(sensor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching storage utilization sensor");
            return Error<SensorValueDto>("Error fetching storage utilization sensor", 500);
        }
    }

    /// <summary>
    /// Sensor: Daily stock movements.
    /// </summary>
    [HttpGet("daily-movements")]
    [ProducesResponseType(typeof(ApiResponse<SensorValueDto>), 200)]
    public async Task<ActionResult<ApiResponse<SensorValueDto>>> GetDailyMovementsSensor()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var warehouseId = CurrentWarehouseId;
            var today = DateTime.UtcNow.Date;

            var todayMovements = await context.StockMovements
                .Where(sm => sm.WarehouseId == warehouseId && sm.Timestamp >= today)
                .ToListAsync();

            var inCount = todayMovements.Count(m => m.QuantityChange > 0);
            var outCount = todayMovements.Count(m => m.QuantityChange < 0);
            var totalCount = todayMovements.Count;

            var sensor = new SensorValueDto
            {
                EntityId = "sensor.inventory_daily_movements",
                Name = "Bestandsbewegungen heute",
                Value = totalCount,
                Unit = "Bewegungen",
                Icon = "mdi:swap-horizontal",
                State = "ok",
                Attributes = new Dictionary<string, object>
                {
                    { "in_count", inCount },
                    { "out_count", outCount },
                    { "warehouse_id", warehouseId },
                    { "date", today.ToString("yyyy-MM-dd") }
                },
                LastUpdated = DateTime.UtcNow
            };

            return Success(sensor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching daily movements sensor");
            return Error<SensorValueDto>("Error fetching daily movements sensor", 500);
        }
    }

    /// <summary>
    /// Sensor: Top categories (as list).
    /// </summary>
    [HttpGet("top-categories")]
    [ProducesResponseType(typeof(ApiResponse<SensorValueDto>), 200)]
    public async Task<ActionResult<ApiResponse<SensorValueDto>>> GetTopCategoriesSensor()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var warehouseId = CurrentWarehouseId;

            var categoryProducts = await context.Products
                .Where(p => p.WarehouseId == warehouseId && p.CategoryId > 0)
                .Include(p => p.Category)
                .Select(p => new { p.Category!.Name, p.Quantity, p.Price })
                .ToListAsync();

            var topCategories = categoryProducts
                .GroupBy(p => p.Name)
                .Select(g => new
                {
                    Category = g.Key,
                    ProductCount = g.Count(),
                    TotalValue = g.Sum(p => p.Quantity * p.Price)
                })
                .OrderByDescending(c => c.TotalValue)
                .Take(5)
                .ToList();

            var sensor = new SensorValueDto
            {
                EntityId = "sensor.inventory_top_categories",
                Name = "Top Kategorien",
                Value = topCategories.Count,
                Unit = "Kategorien",
                Icon = "mdi:tag-multiple",
                State = "ok",
                Attributes = new Dictionary<string, object>
                {
                    { "categories", topCategories.Select(c => c.Category).ToList() },
                    { "product_counts", topCategories.Select(c => c.ProductCount).ToList() },
                    { "total_values", topCategories.Select(c => (double)c.TotalValue).ToList() },
                    { "warehouse_id", warehouseId }
                },
                LastUpdated = DateTime.UtcNow
            };

            return Success(sensor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching top categories sensor");
            return Error<SensorValueDto>("Error fetching top categories sensor", 500);
        }
    }

    [HttpGet("all")]
    [ProducesResponseType(typeof(ApiResponse<List<SensorValueDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<SensorValueDto>>>> GetAllSensors()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var sensors = new List<SensorValueDto>();
            var warehouseId = CurrentWarehouseId;

            // Existing 7 sensors
            var tasks = new[]
            {
                GetInventoryValueSensor(),
                GetTotalProductsSensor(),
                GetLowStockCountSensor(),
                GetExpiryWarningsSensor(),
                GetStorageUtilizationSensor(),
                GetDailyMovementsSensor(),
                GetTopCategoriesSensor()
            };

            var results = await Task.WhenAll(tasks);

            foreach (var result in results)
            {
                if (result.Result is OkObjectResult okResult &&
                    okResult.Value is ApiResponse<SensorValueDto> response &&
                    response.Data != null)
                {
                    sensors.Add(response.Data);
                }
            }

            // 1. Total Warehouses
            var totalWarehouses = await context.Warehouses.CountAsync();
            sensors.Add(new SensorValueDto
            {
                EntityId = "sensor.total_warehouses",
                Name = "Anzahl Warenlager",
                Value = totalWarehouses,
                Unit = "",
                Icon = "mdi:warehouse",
                State = "ok",
                Attributes = new Dictionary<string, object>
                {
                    { "count", totalWarehouses }
                },
                LastUpdated = DateTime.UtcNow
            });

            // 2. Total Rooms
            var totalRooms = await context.Rooms.CountAsync(r => r.WarehouseId == warehouseId);
            sensors.Add(new SensorValueDto
            {
                EntityId = "sensor.total_rooms",
                Name = "Anzahl R\u00e4ume",
                Value = totalRooms,
                Unit = "",
                Icon = "mdi:door",
                State = "ok",
                Attributes = new Dictionary<string, object>
                {
                    { "warehouse_id", warehouseId },
                    { "count", totalRooms }
                },
                LastUpdated = DateTime.UtcNow
            });

            // 3. Total Storage Locations
            var totalStorageLocations = await context.StorageLocations.CountAsync(sl => sl.WarehouseId == warehouseId);
            sensors.Add(new SensorValueDto
            {
                EntityId = "sensor.total_storage_locations",
                Name = "Anzahl Lagerorte",
                Value = totalStorageLocations,
                Unit = "",
                Icon = "mdi:map-marker-multiple",
                State = "ok",
                Attributes = new Dictionary<string, object>
                {
                    { "warehouse_id", warehouseId },
                    { "count", totalStorageLocations }
                },
                LastUpdated = DateTime.UtcNow
            });

            // 4. Total Users
            var totalUsers = await context.Users.CountAsync(u => u.WarehouseId == warehouseId);
            sensors.Add(new SensorValueDto
            {
                EntityId = "sensor.total_users",
                Name = "Anzahl Benutzer",
                Value = totalUsers,
                Unit = "",
                Icon = "mdi:account-group",
                State = "ok",
                Attributes = new Dictionary<string, object>
                {
                    { "warehouse_id", warehouseId },
                    { "count", totalUsers }
                },
                LastUpdated = DateTime.UtcNow
            });

            // 5. Unread Notifications
            var unreadNotifications = await context.Notifications.CountAsync(n => n.WarehouseId == warehouseId && !n.IsRead);
            sensors.Add(new SensorValueDto
            {
                EntityId = "sensor.unread_notifications",
                Name = "Ungelesene Benachrichtigungen",
                Value = unreadNotifications,
                Unit = "",
                Icon = "mdi:bell-badge",
                State = unreadNotifications > 10 ? "warning" : "ok",
                Attributes = new Dictionary<string, object>
                {
                    { "warehouse_id", warehouseId },
                    { "count", unreadNotifications }
                },
                LastUpdated = DateTime.UtcNow
            });

            // 6. Recent Movements (last hour)
            var lastHour = DateTime.UtcNow.AddHours(-1);
            var recentMovements = await context.StockMovements.CountAsync(sm => sm.WarehouseId == warehouseId && sm.Timestamp >= lastHour);
            sensors.Add(new SensorValueDto
            {
                EntityId = "sensor.recent_movements",
                Name = "Bewegungen (letzte Stunde)",
                Value = recentMovements,
                Unit = "",
                Icon = "mdi:clock-fast",
                State = "ok",
                Attributes = new Dictionary<string, object>
                {
                    { "warehouse_id", warehouseId },
                    { "time_window", "1 hour" }
                },
                LastUpdated = DateTime.UtcNow
            });

            // 7. Expiring Batches (next 7 days)
            var next7Days = DateTime.UtcNow.AddDays(7);
            var expiringBatches = await context.ProductBatches
                .CountAsync(pb => pb.Product.WarehouseId == warehouseId
                    && pb.ExpiryDate.HasValue
                    && pb.ExpiryDate.Value >= DateTime.UtcNow
                    && pb.ExpiryDate.Value <= next7Days);
            sensors.Add(new SensorValueDto
            {
                EntityId = "sensor.expiring_batches",
                Name = "Ablaufende Chargen",
                Value = expiringBatches,
                Unit = "",
                Icon = "mdi:package-variant-closed-remove",
                State = expiringBatches > 0 ? "warning" : "ok",
                Attributes = new Dictionary<string, object>
                {
                    { "warehouse_id", warehouseId },
                    { "time_window", "7 days" }
                },
                LastUpdated = DateTime.UtcNow
            });

            // 8. Total Audit Logs (last 30 days)
            var last30Days = DateTime.UtcNow.AddDays(-30);
            var totalAuditLogs = await context.AuditLogs.CountAsync(al => al.Timestamp >= last30Days);
            sensors.Add(new SensorValueDto
            {
                EntityId = "sensor.total_audit_logs",
                Name = "Audit Logs (30 Tage)",
                Value = totalAuditLogs,
                Unit = "",
                Icon = "mdi:file-document-multiple",
                State = "ok",
                Attributes = new Dictionary<string, object>
                {
                    { "time_window", "30 days" }
                },
                LastUpdated = DateTime.UtcNow
            });

            // 9. Active Users (logged in last 7 days)
            var activeUsers = await context.Users.CountAsync(u => u.WarehouseId == warehouseId);
            sensors.Add(new SensorValueDto
            {
                EntityId = "sensor.active_users",
                Name = "Aktive Benutzer",
                Value = activeUsers,
                Unit = "",
                Icon = "mdi:account-check",
                State = "ok",
                Attributes = new Dictionary<string, object>
                {
                    { "warehouse_id", warehouseId },
                    { "note", "All users (LastLogin not tracked)" }
                },
                LastUpdated = DateTime.UtcNow
            });

            // 10. Warehouse Capacity
            var warehouseCapacity = await context.StorageLocations
                .Where(sl => sl.WarehouseId == warehouseId && sl.MaxCapacity.HasValue)
                .SumAsync(sl => sl.MaxCapacity!.Value);
            sensors.Add(new SensorValueDto
            {
                EntityId = "sensor.warehouse_capacity",
                Name = "Lagerkapazit\u00e4t gesamt",
                Value = warehouseCapacity,
                Unit = "",
                Icon = "mdi:package-variant",
                State = "ok",
                Attributes = new Dictionary<string, object>
                {
                    { "warehouse_id", warehouseId }
                },
                LastUpdated = DateTime.UtcNow
            });

            // 11. Average Product Value
            var products = await context.Products
                .Where(p => p.WarehouseId == warehouseId)
                .Select(p => p.Price)
                .ToListAsync();
            var averageValue = products.Any() ? (double)products.Average() : 0;
            sensors.Add(new SensorValueDto
            {
                EntityId = "sensor.average_product_value",
                Name = "Durchschnittlicher Produktwert",
                Value = Math.Round(averageValue, 2),
                Unit = "\u20ac",
                Icon = "mdi:calculator",
                State = "ok",
                Attributes = new Dictionary<string, object>
                {
                    { "warehouse_id", warehouseId },
                    { "formatted_value", $"{averageValue:N2} \u20ac" }
                },
                LastUpdated = DateTime.UtcNow
            });

            _logger.LogInformation("API: {Count} sensors fetched (7 original + 11 new)", sensors.Count);
            return Success(sensors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching all sensors");
            return Error<List<SensorValueDto>>("Error fetching all sensors", 500);
        }
    }
}
