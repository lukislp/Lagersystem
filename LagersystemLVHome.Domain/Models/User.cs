using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// User with access to one or more warehouses.
/// </summary>
public class User
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.User;

    // Foreign Key
    public int WarehouseId { get; set; }

    // Approval Status
    public UserApprovalStatus ApprovalStatus { get; set; } = UserApprovalStatus.Pending;
    public int? ApprovedByUserId { get; set; } // Who approved
    public DateTime? ApprovedAt { get; set; } // When approved
    public string? ApprovalNotes { get; set; } // Approval notes

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginAt { get; set; }

    // GDPR fields
    public bool GdprConsentGiven { get; set; } = false;
    public DateTime? GdprConsentDate { get; set; }
    public string GdprConsentVersion { get; set; } = "1.0"; // Privacy policy version
    public bool MarketingConsent { get; set; } = false;
    public DateTime? MarketingConsentDate { get; set; }

    // Granular consents for GDPR-compliant tracking
    public bool AnalyticsConsent { get; set; } = false;
    public DateTime? AnalyticsConsentDate { get; set; }
    public bool DeviceFingerprintConsent { get; set; } = false;
    public DateTime? DeviceFingerprintConsentDate { get; set; }

    // 2FA (Two-Factor Authentication)
    public bool TwoFactorEnabled { get; set; } = false;
    public string? TwoFactorSecret { get; set; }
    public string? TwoFactorRecoveryCodes { get; set; } // JSON array

    // E-Mail OTP as 2FA method
    public bool EmailOtpEnabled { get; set; } = false;

    /// <summary>
    /// Preferred 2FA method: "Authenticator", "EmailOtp".
    /// Only evaluated when both methods are enabled.
    /// </summary>
    [MaxLength(20)]
    public string Preferred2FAMethod { get; set; } = "Authenticator";

    // 2FA Rate Limiting (brute-force protection)
    public int TwoFAFailedAttempts { get; set; } = 0;
    public DateTime? TwoFALockedUntil { get; set; }

    // Security
    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LastFailedLoginAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastPasswordChangeAt { get; set; }
    public string? LastLoginIp { get; set; }

    // Data deletion
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public string? DeletionReason { get; set; }

    // Profile image (GDPR-compliant in file system)
    [MaxLength(500)]
    public string? ProfileImagePath { get; set; }
    public DateTime? ProfileImageUploadedAt { get; set; }

    // Passwordless Login Option
    /// <summary>
    /// Enables magic-link login for this user.
    /// </summary>
    public bool PasswordlessEnabled { get; set; } = false;

    /// <summary>
    /// Enables IP-based access restrictions for this user.
    /// </summary>
    public bool IpRestrictionsEnabled { get; set; } = false;

    /// <summary>
    /// Default login method (Password, Passwordless, Both).
    /// </summary>
    [MaxLength(20)]
    public string DefaultLoginMethod { get; set; } = "Password";

    // Navigation
    public virtual Warehouse? Warehouse { get; set; }
    public virtual User? ApprovedBy { get; set; }
    public virtual ICollection<UserIpAccessRule>? IpAccessRules { get; set; }
}

public enum UserRole
{
    /// <summary>
    /// Normal user - can scan, manage products.
    /// </summary>
    User = 0,

    /// <summary>
    /// Manager - can view reports.
    /// </summary>
    Manager = 1,

    /// <summary>
    /// Admin - can approve users, full control over the warehouse.
    /// </summary>
    Admin = 2,

    /// <summary>
    /// Super Admin - can manage warehouses.
    /// </summary>
    SuperAdmin = 3
}

public enum UserApprovalStatus
{
    /// <summary>
    /// Waiting for admin approval.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Approved by admin.
    /// </summary>
    Approved = 1,

    /// <summary>
    /// Rejected by admin.
    /// </summary>
    Rejected = 2
}
