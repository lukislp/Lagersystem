using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// Links alternative device fingerprints as the same physical device.
/// Example: Browser and PWA on the same device produce different
/// Thumbmarkjs fingerprints but are the same physical device.
/// User-based, independent of TrustedDevice.
/// </summary>
public class LinkedDeviceFingerprint
{
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [MaxLength(200)]
    public string PrimaryFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// The linked alternative fingerprint (e.g. PWA when primary = browser).
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string LinkedFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// Description of the fingerprint source (e.g. "PWA", "Browser", "Chrome Mobile").
    /// </summary>
    [MaxLength(100)]
    public string? Source { get; set; }

    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual User? User { get; set; }
}
