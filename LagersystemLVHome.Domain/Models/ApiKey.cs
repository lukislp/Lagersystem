using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// API key for REST API authentication.
/// </summary>
public class ApiKey
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// User who owns this API key.
    /// </summary>
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [Required]
    [MaxLength(64)]
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the key (e.g. "Home Assistant Integration").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// First 8 characters of the key for identification (not the full key!).
    /// </summary>
    [Required]
    [MaxLength(8)]
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// When the key was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the key was last used.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Whether the key is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optional expiration date.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Permissions for this key (JSON).
    /// e.g. ["products.read", "products.write", "categories.read"]
    /// </summary>
    [MaxLength(1000)]
    public string? Permissions { get; set; }
}
