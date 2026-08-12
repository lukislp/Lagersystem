namespace LagersystemLVHome.Domain.Models;

public class DashboardData
{
    // KPIs
    public int TotalProducts { get; set; }
    public decimal TotalStockValue { get; set; }
    public int LowStockCount { get; set; }
    public int TotalCategories { get; set; }
    public int TotalWarehouses { get; set; }
    public int TotalStorageLocations { get; set; }
    public int TotalStockQuantity { get; set; }
    public decimal AverageProductValue { get; set; }

    // Advanced Analytics
    public double InventoryHealthScore { get; set; }
    public double StockTurnoverRate { get; set; }
    public ABCAnalysisData AbcAnalysis { get; set; } = new();
    public ExpiryAnalyticsData ExpiryAnalytics { get; set; } = new();
    public StorageUtilizationData StorageUtilization { get; set; } = new();

    // Charts Data
    public List<StockTrendData> StockTrends { get; set; } = new();
    public List<TopMoverData> TopMovers { get; set; } = new();
    public List<CategoryValueData> CategoryValues { get; set; } = new();
    public List<WarehouseStockData> WarehouseDistribution { get; set; } = new();
    public List<StockMovement> RecentMovements { get; set; } = new();
}

public class StockTrendData
{
    public DateTime Date { get; set; }
    public int StockIn { get; set; }
    public int StockOut { get; set; }
    public int TotalStock { get; set; }
    public decimal Value { get; set; }
}

public class TopMoverData
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int MovementCount { get; set; }
    public decimal TotalValue { get; set; }
}

public class CategoryValueData
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal TotalValue { get; set; }
    public int ProductCount { get; set; }
    public int TotalQuantity { get; set; }
}

public class WarehouseStockData
{
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int StorageLocationCount { get; set; }
    public int ProductCount { get; set; }
    public decimal TotalValue { get; set; }
}

// ABC Analysis Data
public class ABCAnalysisData
{
    public int ClassACount { get; set; }
    public decimal ClassAValue { get; set; }
    public int ClassBCount { get; set; }
    public decimal ClassBValue { get; set; }
    public int ClassCCount { get; set; }
    public decimal ClassCValue { get; set; }
    public decimal TotalValue { get; set; }

    public double ClassAPercentage => TotalValue > 0 ? (double)(ClassAValue / TotalValue * 100) : 0;
    public double ClassBPercentage => TotalValue > 0 ? (double)(ClassBValue / TotalValue * 100) : 0;
    public double ClassCPercentage => TotalValue > 0 ? (double)(ClassCValue / TotalValue * 100) : 0;
}

// Expiry Analytics Data
public class ExpiryAnalyticsData
{
    public int ExpiredCount { get; set; }
    public decimal ExpiredValue { get; set; }
    public int ExpiringSoonCount { get; set; }
    public decimal ExpiringSoonValue { get; set; }
    public int ExpiringThisMonthCount { get; set; }
    public decimal ExpiringThisMonthValue { get; set; }

    public int TotalAtRisk => ExpiredCount + ExpiringSoonCount + ExpiringThisMonthCount;
    public decimal TotalAtRiskValue => ExpiredValue + ExpiringSoonValue + ExpiringThisMonthValue;
}

// Storage Utilization Data
public class StorageUtilizationData
{
    public int TotalLocations { get; set; }
    public int OccupiedLocations { get; set; }
    public int EmptyLocations { get; set; }
    public int FullLocations { get; set; }
    public double AverageUtilization { get; set; }
    public int LocationsWithCapacity { get; set; }

    public double OccupancyRate => TotalLocations > 0 ? (OccupiedLocations / (double)TotalLocations * 100) : 0;
}
