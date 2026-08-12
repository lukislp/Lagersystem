using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// Warehouse - each tenant has their own warehouse.
/// </summary>
public class Warehouse
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Address { get; set; } = string.Empty;

    // Unique code for this warehouse
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty; // e.g. "WH001"

    // Maximum number of users for this warehouse
    public int MaxUsers { get; set; } = 10;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public virtual ICollection<User> Users { get; set; } = [];
    public virtual ICollection<Product> Products { get; set; } = [];
    public virtual ICollection<Category> Categories { get; set; } = [];
    public virtual ICollection<StorageLocation> StorageLocations { get; set; } = [];
    public virtual ICollection<StockMovement> StockMovements { get; set; } = [];
}
