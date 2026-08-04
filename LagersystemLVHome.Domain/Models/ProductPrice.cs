using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// Price history for products with validity period.
/// Enables historical price tracking and time-based pricing.
/// </summary>
public class ProductPrice
{
    public int Id { get; set; }

    [Required]
    public int ProductId { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Preis muss größer als 0 sein")]
    public decimal Price { get; set; }

    [Required]
    public DateTime ValidFrom { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// End date of price validity (inclusive, NULL = unlimited).
    /// </summary>
    public DateTime? ValidTo { get; set; }

    /// <summary>
    /// Optional reason for price change (e.g. "Seasonal discount", "Cost increase", "Promotion").
    /// </summary>
    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>
    /// Optional notes for the price change.
    /// </summary>
    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>
    /// Who changed the price.
    /// </summary>
    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Multi-tenancy
    public int WarehouseId { get; set; }

    // Navigation Properties
    public virtual Product? Product { get; set; }
    public virtual Warehouse? Warehouse { get; set; }

    // Computed Properties

    /// <summary>
    /// Whether this price is currently active.
    /// </summary>
    public bool IsActive
    {
        get
        {
            var now = DateTime.UtcNow;
            return ValidFrom <= now && (!ValidTo.HasValue || ValidTo.Value >= now);
        }
    }

    /// <summary>
    /// Whether this price is valid in the future.
    /// </summary>
    public bool IsFuture => ValidFrom > DateTime.UtcNow;

    /// <summary>
    /// Whether this price has expired.
    /// </summary>
    public bool IsExpired => ValidTo.HasValue && ValidTo.Value < DateTime.UtcNow;

    /// <summary>
    /// How many days this price is still valid (in days).
    /// </summary>
    public int? DaysRemaining
    {
        get
        {
            if (!ValidTo.HasValue) return null; // Unlimited
            if (IsExpired) return 0;
            return (ValidTo.Value - DateTime.UtcNow).Days;
        }
    }

    /// <summary>
    /// How long this price was/is valid (duration in days).
    /// </summary>
    public int? ValidityDurationDays
    {
        get
        {
            if (!ValidTo.HasValue) return null; // Unlimited
            return (ValidTo.Value - ValidFrom).Days;
        }
    }
}
