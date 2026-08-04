namespace LagersystemLVHome.API.DTOs;

/// <summary>
/// DTO for stock alerts.
/// </summary>
public class StockAlertDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string AlertType { get; set; } = string.Empty; // LowStock, OutOfStock, Expiring, Expired, Overstock
    public int? CurrentQuantity { get; set; }
    public int? MinQuantity { get; set; }
    public int? MissingQuantity { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? DaysUntilExpiry { get; set; }
    public string Severity { get; set; } = string.Empty; // Low, Medium, High, Critical
    public string Message { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string? CategoryIcon { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// DTO for alert summary.
/// </summary>
public class AlertSummaryDto
{
    public int LowStock { get; set; }
    public int OutOfStock { get; set; }
    public int ExpiringSoon { get; set; }
    public int Expired { get; set; }
    public int Overstock { get; set; }
    public int TotalAlerts { get; set; }
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// DTO for Home Assistant sensors.
/// </summary>
public class SensorValueDto
{
    public string EntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public object Value { get; set; } = 0;
    public string? Unit { get; set; }
    public string Icon { get; set; } = "mdi:package-variant";
    public Dictionary<string, object> Attributes { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public string State { get; set; } = "ok"; // ok, warning, critical
}
