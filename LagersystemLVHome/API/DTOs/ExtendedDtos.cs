namespace LagersystemLVHome.API.DTOs;

// ==================== MOVEMENTS ====================

/// <summary>
/// DTO for stock movements.
/// </summary>
public class MovementDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public int QuantityChange { get; set; }
    public string Type { get; set; } = string.Empty; // ManualAdd, ManualRemove, Sale, etc.
    public string? Notes { get; set; }
    public DateTime Timestamp { get; set; }
    public int WarehouseId { get; set; }
}

// ==================== STORAGE LOCATIONS ====================

/// <summary>
/// DTO for storage locations (full detail).
/// </summary>
public class StorageLocationDetailDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Room { get; set; }
    public int? MaxCapacity { get; set; }
    public int? CurrentCapacity { get; set; }
    public double UtilizationPercentage { get; set; }
    public bool IsActive { get; set; }
    public int WarehouseId { get; set; }
    public List<ProductInLocationDto> Products { get; set; } = new();
}

public class ProductInLocationDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public int Quantity { get; set; }
    public string? CategoryName { get; set; }
}

public class CreateStorageLocationRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Room { get; set; }
    public int? MaxCapacity { get; set; }
}

public class UpdateStorageLocationRequest
{
    public string? Name { get; set; }
    public string? Room { get; set; }
    public int? MaxCapacity { get; set; }
    public bool? IsActive { get; set; }
}

// ==================== ROOMS ====================

/// <summary>
/// DTO for rooms.
/// </summary>
public class RoomDto
{
    public string Name { get; set; } = string.Empty;
    public int StorageLocationCount { get; set; }
    public int ProductCount { get; set; }
    public int TotalCapacity { get; set; }
    public int UsedCapacity { get; set; }
    public double UtilizationPercentage { get; set; }
}

// ==================== USERS ====================

/// <summary>
/// DTO for user info (read-only, safe).
/// </summary>
public class UserInfoDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public bool TwoFactorEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class UserActivitySummaryDto
{
    public int TotalActions { get; set; }
    public int LoginCount { get; set; }
    public int ProductsCreated { get; set; }
    public int StockMovements { get; set; }
    public DateTime? LastActivity { get; set; }
    public List<RecentActivityDto> RecentActivities { get; set; } = new();
}

public class RecentActivityDto
{
    public string Action { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Details { get; set; }
}

// ==================== WAREHOUSES ====================

/// <summary>
/// DTO for warehouses (read-only).
/// </summary>
public class WarehouseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WarehouseStatsDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TotalProducts { get; set; }
    public int TotalCategories { get; set; }
    public int TotalStorageLocations { get; set; }
    public int TotalUsers { get; set; }
    public decimal TotalInventoryValue { get; set; }
    public int LowStockCount { get; set; }
    public DateTime LastUpdated { get; set; }
}

// ==================== SEARCH ====================

/// <summary>
/// DTO for search results.
/// </summary>
public class SearchResultDto
{
    public string Type { get; set; } = string.Empty; // Product, Category, StorageLocation
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? AdditionalInfo { get; set; }
    public double RelevanceScore { get; set; }
}

public class GlobalSearchResultDto
{
    public List<SearchResultDto> Products { get; set; } = new();
    public List<SearchResultDto> Categories { get; set; } = new();
    public List<SearchResultDto> StorageLocations { get; set; } = new();
    public int TotalResults { get; set; }
    public string Query { get; set; } = string.Empty;
}

// ==================== BATCHES ====================

/// <summary>
/// DTO for product batches (best-before date).
/// </summary>
public class BatchDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public int Quantity { get; set; }
    public int? StorageLocationId { get; set; }
    public string? StorageLocationCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? DaysUntilExpiry { get; set; }
    public string Status { get; set; } = string.Empty; // Fresh, Expiring, Expired
}

// ==================== AUDIT LOGS ====================

/// <summary>
/// DTO for audit logs (read-only).
/// </summary>
public class AuditLogDto
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? Details { get; set; }
    public string Severity { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public string? Username { get; set; }
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; }
}

// ==================== NOTIFICATIONS ====================

/// <summary>
/// DTO for notifications (read-only).
/// </summary>
public class NotificationDto
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? RelatedEntity { get; set; }
    public int? RelatedEntityId { get; set; }
}
