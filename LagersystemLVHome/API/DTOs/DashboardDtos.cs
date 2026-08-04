namespace LagersystemLVHome.API.DTOs;

/// <summary>
/// Stock movement DTO.
/// </summary>
public class StockMovementDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Username { get; set; }
    public int? StorageLocationId { get; set; }
    public string? StorageLocationCode { get; set; }
}

/// <summary>
/// Dashboard statistics DTO.
/// </summary>
public class DashboardStatsDto
{
    public int TotalProducts { get; set; }
    public int LowStockProducts { get; set; }
    public int Categories { get; set; }
    public int StorageLocations { get; set; }
    public double TotalValue { get; set; }
    public List<CategoryStatDto>? TopCategories { get; set; }
    public List<ProductStatDto>? LowStockItems { get; set; }
}

public class CategoryStatDto
{
    public string CategoryName { get; set; } = string.Empty;
    public int ProductCount { get; set; }
}

public class ProductStatDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int MinStock { get; set; }
}
