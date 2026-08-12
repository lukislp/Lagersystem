using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// Audit log for GDPR compliance and security.
/// Records all important actions in the system.
/// Tamper-proof with hash chain.
/// </summary>
public class AuditLog
{
    public int Id { get; set; }

    // Who performed the action
    public int? UserId { get; set; }

    [MaxLength(100)]
    public string? Username { get; set; } // Redundant for deleted users

    // What was done
    [Required]
    [MaxLength(50)]
    public string Action { get; set; } = string.Empty; // CREATE, UPDATE, DELETE, LOGIN, LOGOUT, etc.

    [MaxLength(100)]
    public string? EntityType { get; set; } // Product, User, StorageLocation, etc.

    [MaxLength(100)]
    public string Entity { get; set; } = string.Empty; // Alias for EntityType (backwards compatibility)

    public int? EntityId { get; set; }

    [MaxLength(2000)]
    public string? Changes { get; set; } // JSON with change details

    [MaxLength(2000)]
    public string? Details { get; set; } // Alias for Changes (backwards compatibility)

    // When and where
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [MaxLength(200)] // Extended: 50 -> 200 for IPv6 + Forwarded-For
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    // Tamper-proof hash chain
    [MaxLength(100)]
    public string? Hash { get; set; } // SHA256 hash of this entry

    [MaxLength(100)]
    public string? PreviousHash { get; set; } // Hash of previous entry (blockchain-style)

    // Multi-tenancy
    public int? WarehouseId { get; set; }

    // Severity level
    public AuditSeverity Severity { get; set; } = AuditSeverity.Info;

    // Navigation
    public virtual User? User { get; set; }
    public virtual Warehouse? Warehouse { get; set; }
}

public enum AuditSeverity
{
    Info = 0,       // Normal action
    Warning = 1,    // Suspicious action
    Error = 2,      // Error
    Critical = 3    // Critical security incident
}
