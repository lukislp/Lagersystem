using System;
using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

public class ProductBatch
{
    public int Id { get; set; }

    [Required]
    public int ProductId { get; set; }

    [Required]
    [MaxLength(100)]
    public string BatchNumber { get; set; } = string.Empty; // Batch number

    public int Quantity { get; set; } // Quantity of this batch

    public DateTime? ExpiryDate { get; set; } // Best-before date of this batch

    public DateTime? ManufactureDate { get; set; } // Manufacture date

    [MaxLength(500)]
    public string? Notes { get; set; } // Notes for this batch

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Multi-tenancy
    public int WarehouseId { get; set; }

    // Navigation
    public virtual Product? Product { get; set; }
    public virtual Warehouse? Warehouse { get; set; }

    // Computed Properties
    public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value < DateTime.UtcNow;
    public bool IsExpiringSoon => ExpiryDate.HasValue && ExpiryDate.Value < DateTime.UtcNow.AddDays(7) && !IsExpired;
    public int DaysUntilExpiry => ExpiryDate.HasValue ? (ExpiryDate.Value - DateTime.UtcNow).Days : int.MaxValue;
}
