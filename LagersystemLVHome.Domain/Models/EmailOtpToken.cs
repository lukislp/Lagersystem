using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// Stores temporary e-mail OTP codes for 2FA.
/// </summary>
public class EmailOtpToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [MaxLength(10)]
    public string Code { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public bool IsUsed { get; set; } = false;

    /// <summary>
    /// IP address of the requester (brute-force protection).
    /// </summary>
    [MaxLength(100)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// Number of failed validation attempts for this token.
    /// </summary>
    public int FailedAttempts { get; set; } = 0;

    // Navigation
    public virtual User? User { get; set; }
}
