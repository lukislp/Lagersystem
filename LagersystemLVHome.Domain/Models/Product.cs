using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

public class Product
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Barcode { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int MinQuantity { get; set; } = 5; // Minimum stock for warning

    [Required(ErrorMessage = "Preis ist erforderlich")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Preis muss größer als 0 sein")]
    public decimal Price { get; set; }

    // Best-before date (food items only)
    public DateTime? ExpiryDate { get; set; }
    public bool TrackExpiryDate { get; set; } = false;

    // Product images
    [MaxLength(500)]
    public string? ImageUrl { get; set; }
    [MaxLength(500)]
    public string? ThumbnailUrl { get; set; }

    // Specification PDF (optional)
    [MaxLength(500)]
    public string? SpecificationPdfPath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Foreign Keys
    public int CategoryId { get; set; }
    public int WarehouseId { get; set; } // Multi-tenancy

    // Navigation Properties
    public virtual Category? Category { get; set; }
    public virtual Warehouse? Warehouse { get; set; }
    public virtual ICollection<StockMovement> StockMovements { get; set; } = [];

    // Many-to-many relationship
    public virtual ICollection<ProductStorageLocation> ProductStorageLocations { get; set; } = [];
    public virtual ICollection<ProductPrice> PriceHistory { get; set; } = [];

    // Computed Properties
    public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value < DateTime.UtcNow;
    public bool IsExpiringSoon => ExpiryDate.HasValue && ExpiryDate.Value < DateTime.UtcNow.AddDays(7) && !IsExpired;
    public int DaysUntilExpiry => ExpiryDate.HasValue ? (ExpiryDate.Value - DateTime.UtcNow).Days : int.MaxValue;

    // Get current price from price history
    public decimal GetCurrentPrice()
    {
        var now = DateTime.UtcNow;
        var activePrice = PriceHistory?
            .Where(p => p.ValidFrom <= now && (!p.ValidTo.HasValue || p.ValidTo.Value >= now))
            .OrderByDescending(p => p.ValidFrom)
            .FirstOrDefault();

        return activePrice?.Price ?? Price; // Fallback to legacy price
    }
}
