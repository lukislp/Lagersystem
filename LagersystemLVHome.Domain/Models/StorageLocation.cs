using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

public class StorageLocation
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty; // e.g. "A1-R2-F3"

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty; // e.g. "Rack A1, Row 2, Shelf 3"

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    // Hierarchical structure
    [MaxLength(50)]
    public string? Room { get; set; } // Room (e.g. "Storage 1", "Basement", "Hall A")

    public string? Aisle { get; set; } // Aisle (e.g. "A", "B", "C")
    public string? Rack { get; set; } // Rack (e.g. "1", "2", "3")
    public string? Shelf { get; set; } // Shelf/level (e.g. "1", "2", "3")
    public string? Bin { get; set; } // Bin/container (optional)

    // QR code for quick access
    [MaxLength(500)]
    public string? QRCode { get; set; } // QR code content (can be URL or ID)

    public DateTime? QRCodeGeneratedAt { get; set; } // When the QR code was generated

    // Properties
    public int? MaxCapacity { get; set; } // Maximum capacity (units)
    public double? Width { get; set; } // Width in cm
    public double? Height { get; set; } // Height in cm
    public double? Depth { get; set; } // Depth in cm

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Multi-tenancy
    public int WarehouseId { get; set; }

    // Navigation
    public virtual Warehouse? Warehouse { get; set; }
    [Obsolete("Use ProductStorageLocations instead")]
    public virtual ICollection<Product> Products { get; set; } = [];

    // Many-to-many relationship
    public virtual ICollection<ProductStorageLocation> ProductStorageLocations { get; set; } = [];
}
