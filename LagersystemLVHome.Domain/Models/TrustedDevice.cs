using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// Stores trusted devices that are allowed to skip 2FA.
/// Based on the Thumbmarkjs browser fingerprint.
/// </summary>
public class TrustedDevice
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>
    /// Thumbmarkjs browser fingerprint (stable, precise device identifier).
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string DeviceFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly device name (e.g. "Chrome on Windows 10/11").
    /// </summary>
    [MaxLength(200)]
    public string? DeviceName { get; set; }

    public DateTime TrustedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Trust expires after this time (2FA required again).
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// IP address at the time of trust establishment.
    /// </summary>
    [MaxLength(200)]
    public string? IpAddress { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public virtual User? User { get; set; }
}
