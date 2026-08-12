using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// Link between product and storage location (many-to-many).
/// A product can be stored in multiple storage locations.
/// </summary>
public class ProductStorageLocation
{
    public int Id { get; set; }

    // Foreign Keys
    public int ProductId { get; set; }
    public int StorageLocationId { get; set; }

    // Quantity at this specific storage location
    public int Quantity { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public virtual Product Product { get; set; } = null!;
    public virtual StorageLocation StorageLocation { get; set; } = null!;
}
