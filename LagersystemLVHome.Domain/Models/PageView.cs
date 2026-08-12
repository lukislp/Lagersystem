namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// Page view tracking for Application Insights.
/// </summary>
public class PageView
{
    public int Id { get; set; }

    // User Information
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Username { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;

    // Page Information
    public string PageUrl { get; set; } = string.Empty;
    public string PageTitle { get; set; } = string.Empty;
    public string? Referrer { get; set; }

    // Session Information
    public string SessionId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;

    // Device Information
    public string? DeviceType { get; set; } // Mobile, Desktop, Tablet
    public string? Browser { get; set; }
    public string? OperatingSystem { get; set; }

    // Timing
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int? LoadTimeMs { get; set; } // Page load time in milliseconds
    public int? TimeOnPageSeconds { get; set; } // How long user stayed on page

    // Location (optional)
    public string? Country { get; set; }
    public string? City { get; set; }

    // Warehouse Context
    public int? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
}

/// <summary>
/// API request tracking.
/// </summary>
public class ApiRequest
{
    public int Id { get; set; }

    // Request Information
    public string Endpoint { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty; // GET, POST, PUT, DELETE
    public int StatusCode { get; set; }

    // Authentication
    public string? ApiKeyName { get; set; }
    public string? TokenUserId { get; set; }
    public bool IsAuthenticated { get; set; }

    // Timing
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int DurationMs { get; set; }

    // Request Details
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }

    // Error Information
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
}

/// <summary>
/// System performance metrics.
/// </summary>
public class PerformanceMetric
{
    public int Id { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // System Metrics
    public double CpuUsagePercent { get; set; }
    public long MemoryUsedMB { get; set; }
    public long MemoryTotalMB { get; set; }

    // Database Metrics
    public int ActiveConnections { get; set; }
    public double AvgQueryTimeMs { get; set; }
    public int TotalQueries { get; set; }

    // Application Metrics
    public int ActiveUsers { get; set; }
    public int TotalRequests { get; set; }
    public double AvgResponseTimeMs { get; set; }

    // Cache Metrics
    public int CacheHits { get; set; }
    public int CacheMisses { get; set; }
    public double CacheHitRatio { get; set; }
}

/// <summary>
/// User activity event.
/// </summary>
public class UserActivity
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Activity Information
    public string ActivityType { get; set; } = string.Empty; // Login, Logout, Create, Update, Delete, Export, etc.
    public string EntityType { get; set; } = string.Empty; // Product, Category, User, etc.
    public string? EntityId { get; set; }
    public string? EntityName { get; set; }

    // Details
    public string? Description { get; set; }
    public string? AdditionalData { get; set; } // JSON for complex data

    // Context
    public int? WarehouseId { get; set; }
    public string SessionId { get; set; } = string.Empty;
}
