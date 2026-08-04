namespace LagersystemLVHome.API.DTOs;

/// <summary>
/// Analytics DTO for standard dashboard data.
/// </summary>
public class AnalyticsStandardDto
{
    public int TotalProducts { get; set; }
    public int TotalCategories { get; set; }
    public int LowStockCount { get; set; }
    public int ExpiringSoonCount { get; set; }
    public decimal TotalInventoryValue { get; set; }

    public List<MovementTrendDto>? RecentMovements { get; set; }
    public List<CategoryValueDto>? TopCategories { get; set; }
    public List<LowStockItemDto>? LowStockItems { get; set; }
}

/// <summary>
/// Analytics DTO for enhanced dashboard data with extended metrics.
/// </summary>
public class AnalyticsEnhancedDto
{
    // Extended statistics
    public int TotalProducts { get; set; }
    public int ActiveProducts { get; set; }
    public int InactiveProducts { get; set; }
    public int TotalCategories { get; set; }
    public int TotalStorageLocations { get; set; }
    public int TotalRooms { get; set; }

    // Financial metrics
    public decimal TotalInventoryValue { get; set; }
    public decimal AveragePurchasePrice { get; set; }
    public decimal PotentialSalesValue { get; set; }
    public decimal PotentialProfit { get; set; }

    // Stock level metrics
    public int LowStockCount { get; set; }
    public int OutOfStockCount { get; set; }
    public int OverstockedCount { get; set; }
    public int ExpiringSoonCount { get; set; }
    public int ExpiredCount { get; set; }

    // Movement metrics
    public int TodayMovements { get; set; }
    public int WeekMovements { get; set; }
    public int MonthMovements { get; set; }

    // Trend data
    public List<MovementTrendDto>? MovementTrend7Days { get; set; }
    public List<MovementTrendDto>? MovementTrend30Days { get; set; }
    public List<CategoryValueDto>? CategoryDistribution { get; set; }
    public List<StorageUtilizationDto>? StorageUtilization { get; set; }

    // Top lists
    public List<TopProductDto>? TopMovedProducts { get; set; }
    public List<TopProductDto>? MostValuableProducts { get; set; }
    public List<LowStockItemDto>? CriticalLowStock { get; set; }
    public List<ExpiringProductDto>? ExpiringProducts { get; set; }

    // Performance indicators
    public double StockTurnoverRate { get; set; }
    public double AverageStockAge { get; set; }
    public double InventoryAccuracy { get; set; }
}

/// <summary>
/// Movement trend per day/period.
/// </summary>
public class MovementTrendDto
{
    public DateTime Date { get; set; }
    public int InCount { get; set; }
    public int OutCount { get; set; }
    public int NetChange { get; set; }
    public string? Label { get; set; }
}

/// <summary>
/// Category with value and count.
/// </summary>
public class CategoryValueDto
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = "bi-tag";
    public int ProductCount { get; set; }
    public decimal TotalValue { get; set; }
    public double Percentage { get; set; }
}

/// <summary>
/// Storage location utilization.
/// </summary>
public class StorageUtilizationDto
{
    public int StorageLocationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? RoomName { get; set; }
    public int ProductCount { get; set; }
    public int TotalQuantity { get; set; }
    public int? MaxCapacity { get; set; }
    public double UtilizationPercentage { get; set; }
}

/// <summary>
/// Top product (most moved or most valuable).
/// </summary>
public class TopProductDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public int Quantity { get; set; }
    public decimal? Value { get; set; }
    public int MovementCount { get; set; }
    public string? CategoryName { get; set; }
}

/// <summary>
/// Low stock item.
/// </summary>
public class LowStockItemDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public int CurrentQuantity { get; set; }
    public int MinQuantity { get; set; }
    public int MissingQuantity { get; set; }
    public string? CategoryName { get; set; }
    public DateTime? LastRestocked { get; set; }
}

/// <summary>
/// Expiring product (best-before date).
/// </summary>
public class ExpiringProductDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int DaysUntilExpiry { get; set; }
    public int Quantity { get; set; }
    public string? CategoryName { get; set; }
    public string Status { get; set; } = "Expiring"; // Expiring, Expired
}

/// <summary>
/// Time range filter for analytics.
/// </summary>
public class AnalyticsTimeRangeDto
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string TimeRange { get; set; } = "7days"; // 7days, 30days, 90days, year, custom
}
