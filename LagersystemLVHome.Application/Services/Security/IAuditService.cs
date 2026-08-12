using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

public interface IAuditService
{
    Task LogAsync(string action, string entity, int? entityId = null, object? details = null, AuditSeverity severity = AuditSeverity.Info, CancellationToken cancellationToken = default);

    // Authentication
    Task LogLoginAsync(int userId, bool success, string? reason = null, CancellationToken cancellationToken = default);
    Task LogLogoutAsync(int userId, CancellationToken cancellationToken = default);
    Task Log2FAEnabledAsync(int userId, CancellationToken cancellationToken = default);
    Task Log2FADisabledAsync(int userId, CancellationToken cancellationToken = default);
    Task LogPasswordChangedAsync(int userId, CancellationToken cancellationToken = default);
    Task LogPasswordResetRequestAsync(string email, CancellationToken cancellationToken = default);

    // Product operations
    Task LogProductCreatedAsync(int productId, string productName, CancellationToken cancellationToken = default);
    Task LogProductUpdatedAsync(int productId, string productName, object changes, CancellationToken cancellationToken = default);
    Task LogProductDeletedAsync(int productId, string productName, CancellationToken cancellationToken = default);

    // Stock movements
    Task LogStockMovementAsync(int productId, string productName, int quantityChange, string type, CancellationToken cancellationToken = default);

    // Category operations
    Task LogCategoryCreatedAsync(int categoryId, string categoryName, CancellationToken cancellationToken = default);
    Task LogCategoryUpdatedAsync(int categoryId, string categoryName, CancellationToken cancellationToken = default);
    Task LogCategoryDeletedAsync(int categoryId, string categoryName, CancellationToken cancellationToken = default);

    // Storage location operations
    Task LogStorageLocationCreatedAsync(int locationId, string locationCode, CancellationToken cancellationToken = default);
    Task LogStorageLocationUpdatedAsync(int locationId, string locationCode, CancellationToken cancellationToken = default);
    Task LogStorageLocationDeletedAsync(int locationId, string locationCode, CancellationToken cancellationToken = default);

    // Export/Import
    Task LogExportAsync(string format, string entity, int recordCount, CancellationToken cancellationToken = default);
    Task LogImportAsync(string format, string entity, int recordCount, int successCount, int errorCount, CancellationToken cancellationToken = default);

    // User management (Admin)
    Task LogUserApprovedAsync(int userId, string username, CancellationToken cancellationToken = default);
    Task LogUserRejectedAsync(int userId, string username, CancellationToken cancellationToken = default);
    Task LogUserDeletedAsync(int userId, string username, CancellationToken cancellationToken = default);

    // GDPR
    Task LogGdprDataExportAsync(int userId, CancellationToken cancellationToken = default);
    Task LogGdprAccountDeletionAsync(int userId, string reason, CancellationToken cancellationToken = default);

    // Queries
    Task<List<AuditLog>> GetRecentLogsAsync(int count = 100, CancellationToken cancellationToken = default);
    Task<List<AuditLog>> GetUserLogsAsync(int userId, int count = 100, CancellationToken cancellationToken = default);
    Task<List<AuditLog>> GetEntityLogsAsync(string entity, int entityId, CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> GetActionStatisticsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    Task<List<AuditLog>> GetSecurityEventsAsync(int count = 50, CancellationToken cancellationToken = default);

    // Tamper-proof integrity verification
    Task<AuditLogVerificationResult> VerifyIntegrityAsync(int? limitToLast = null, CancellationToken cancellationToken = default);
}
