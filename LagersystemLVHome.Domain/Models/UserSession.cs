using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// Active user session with VPN detection and tracking.
/// </summary>
public class UserSession
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string SessionId { get; set; } = Guid.NewGuid().ToString();

    public int UserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    public int WarehouseId { get; set; }

    // Session Details
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public bool IsActive { get; set; } = true;

    // Device & Location
    [MaxLength(200)] // Extended: 100 -> 200 for IPv6 + Forwarded-For
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [MaxLength(100)]
    public string? DeviceType { get; set; } // Desktop, Mobile, Tablet

    [MaxLength(100)]
    public string? Browser { get; set; }

    [MaxLength(100)]
    public string? OperatingSystem { get; set; }

    // Geolocation
    [MaxLength(100)]
    public string? Country { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(50)]
    public string? Region { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // VPN Detection
    public bool IsVpn { get; set; } = false;
    public bool IsProxy { get; set; } = false;
    public bool IsTor { get; set; } = false;

    [MaxLength(200)]
    public string? VpnProvider { get; set; }

    public int VpnConfidenceScore { get; set; } = 0; // 0-100

    [MaxLength(100)]
    public string? HostingProvider { get; set; }

    // Security Risk Assessment
    public SessionRiskLevel RiskLevel { get; set; } = SessionRiskLevel.Low;

    [MaxLength(1000)]
    public string? RiskFactors { get; set; } // JSON array of risk factors

    // Session Hijacking Detection
    public int SuspiciousActivityCount { get; set; } = 0;
    public DateTime? LastSuspiciousActivity { get; set; }
    public bool IsSuspicious { get; set; } = false;

    [MaxLength(500)]
    public string? SuspiciousReason { get; set; }

    // Concurrent Login Prevention
    public bool IsConcurrent { get; set; } = false;
    public int ConcurrentSessionCount { get; set; } = 1;

    // Statistics
    public int PageViewsCount { get; set; } = 0;
    public int ApiRequestsCount { get; set; } = 0;
    public int ActionsCount { get; set; } = 0;

    [MaxLength(500)]
    public string? LastPageUrl { get; set; }

    // Termination
    public SessionEndReason? EndReason { get; set; }

    [MaxLength(500)]
    public string? EndReasonDetails { get; set; }

    public bool WasForcedLogout { get; set; } = false;
    public int? TerminatedByUserId { get; set; }

    // Session Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? TerminatedAt { get; set; }

    // Device Fingerprinting
    [MaxLength(100)]
    public string? DeviceFingerprint { get; set; } // SHA256 hash of User-Agent + IP + Language + Encoding

    [MaxLength(50)]
    public string? DeviceInfo { get; set; } // "Desktop", "Mobile", "Tablet"

    // Navigation
    public virtual User? User { get; set; }
    public virtual Warehouse? Warehouse { get; set; }
    public virtual User? TerminatedBy { get; set; }
    public virtual ICollection<SessionActivity> Activities { get; set; } = [];
}

public enum SessionRiskLevel
{
    VeryLow = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum SessionEndReason
{
    UserLogout = 0,
    Timeout = 1,
    SessionExpired = 2,
    AdminForceLogout = 3,
    ConcurrentLogin = 4,
    SuspiciousActivity = 5,
    SystemShutdown = 6
}

/// <summary>
/// Individual activity within a session.
/// </summary>
public class SessionActivity
{
    public int Id { get; set; }

    public int SessionId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(100)]
    public string ActivityType { get; set; } = string.Empty; // PageView, ApiCall, Action, etc.

    [MaxLength(500)]
    public string? ActivityDetails { get; set; }

    [MaxLength(500)]
    public string? PageUrl { get; set; }

    [MaxLength(100)]
    public string? IpAddress { get; set; }

    public bool IsAnomaly { get; set; } = false;

    [MaxLength(500)]
    public string? AnomalyReason { get; set; }

    // Navigation
    public virtual UserSession? Session { get; set; }
}

/// <summary>
/// Security events and suspicious activities.
/// </summary>
public class SecurityEvent
{
    public int Id { get; set; }

    public int? SessionId { get; set; }
    public int? UserId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = string.Empty;

    public SecurityEventSeverity Severity { get; set; } = SecurityEventSeverity.Info;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(100)]
    public string? IpAddress { get; set; }

    [MaxLength(100)]
    public string? Country { get; set; }

    public bool IsVpn { get; set; } = false;

    [MaxLength(1000)]
    public string? Details { get; set; } // JSON

    public bool IsResolved { get; set; } = false;
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedByUserId { get; set; }

    [MaxLength(500)]
    public string? Resolution { get; set; }

    // Navigation
    public virtual UserSession? Session { get; set; }
    public virtual User? User { get; set; }
    public virtual User? ResolvedBy { get; set; }
}

public enum SecurityEventSeverity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}
