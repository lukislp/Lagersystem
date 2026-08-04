using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// IP-based access rule for a user (optional).
/// Allows restricting login to specific IP addresses/ranges.
/// </summary>
public class UserIpAccessRule
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>
    /// IP pattern (e.g. "192.168.1.100", "192.168.1.*", "10.0.0.0/24").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string IpPattern { get; set; } = string.Empty;

    /// <summary>
    /// Description of the rule (e.g. "Office", "Home Office", "VPN").
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// true = IP is allowed, false = IP is blocked.
    /// </summary>
    public bool IsAllowed { get; set; } = true;

    /// <summary>
    /// Higher priority is checked first (for blacklist/whitelist combinations).
    /// </summary>
    public int Priority { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? CreatedByUserId { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedByUserId { get; set; }

    // Navigation
    public virtual User? User { get; set; }
    public virtual User? CreatedBy { get; set; }
    public virtual User? UpdatedBy { get; set; }
}

/// <summary>
/// Login method for user.
/// </summary>
public enum LoginMethod
{
    /// <summary>Standard login with password.</summary>
    Password,

    /// <summary>Passwordless only (magic link).</summary>
    Passwordless,

    /// <summary>User chooses at login.</summary>
    Both
}
