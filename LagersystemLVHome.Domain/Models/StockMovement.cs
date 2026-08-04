namespace LagersystemLVHome.Domain.Models;

public class StockMovement
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int QuantityChange { get; set; } // Positive = inbound, negative = outbound
    public MovementType Type { get; set; }
    public string? Notes { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? ScannedBarcode { get; set; }

    // Multi-tenancy
    public int WarehouseId { get; set; }

    public virtual Product? Product { get; set; }
    public virtual Warehouse? Warehouse { get; set; }
}

public enum MovementType
{
    Initial = 0,        // Initial stock
    ManualAdd = 1,      // Manual inbound
    ManualRemove = 2,   // Manual outbound
    ScanAdd = 3,        // Scan inbound
    ScanRemove = 4,     // Scan outbound
    Adjustment = 5,     // Manual adjustment
    Disposal = 6        // Disposal (e.g. expired goods)
}
