using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace LagersystemLVHome.Application.Services;

public sealed class AuditService : IAuditService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IGamificationService _gamificationService;

    public AuditService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditService> logger,
        ILoggerFactory loggerFactory,
        IGamificationService gamificationService)
    {
        _contextFactory = contextFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _gamificationService = gamificationService;
    }

    /// <summary>
    /// Uses TamperProofAuditService for all logs (with hash chain).
    /// UserId can be null for system events (e.g. login failures).
    /// </summary>
    public async Task LogAsync(string action, string entity, int? entityId = null, object? details = null, AuditSeverity severity = AuditSeverity.Info, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var httpContext = _httpContextAccessor.HttpContext;
            var currentUser = await GetCurrentUserFromContextAsync();

            var tamperProofLogger = _loggerFactory.CreateLogger<TamperProofAuditService>();
            var tamperProofService = new TamperProofAuditService(context, tamperProofLogger);

            var changesJson = details != null ? JsonSerializer.Serialize(details) : null;

            // Use 0 as system user when no user is logged in (stored as NULL in DB)
            await tamperProofService.CreateTamperProofAuditLogAsync(
                userId: currentUser?.Id ?? 0,
                action: action,
                entityType: entity,
                entityId: entityId,
                changes: changesJson,
                ipAddress: GetIpAddress(httpContext),
                severity: severity
            );

            _logger.LogDebug("Tamper-proof audit log created: {Action} on {Entity}", action, entity);

            // Gamification: persist counter increment
            // During login, currentUser is still null in HttpContext,
            // so fall back to entityId when entity == "User"
            var gamificationUserId = currentUser?.Id ?? 0;
            if (gamificationUserId <= 0 && entity == "User" && entityId is > 0)
            {
                gamificationUserId = entityId.Value;
            }

            if (gamificationUserId > 0)
            {
                _ = Task.Run(async () =>
                {
                    try { await _gamificationService.RecordActionAsync(gamificationUserId, action, changesJson); }
                    catch { /* Gamification errors must not block the application */ }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit log error for action: {Action}", action);
            // Audit errors must not crash the application
        }
    }

    // ========== AUTHENTICATION ==========

    public async Task LogLoginAsync(int userId, bool success, string? reason = null, CancellationToken cancellationToken = default)
    {
        var severity = success ? AuditSeverity.Info : AuditSeverity.Warning;
        var action = success ? "LOGIN_SUCCESS" : "LOGIN_FAILED";

        await LogAsync(action, "User", userId, new
        {
            Success = success,
            Reason = reason
        }, severity);
    }

    public async Task LogLogoutAsync(int userId, CancellationToken cancellationToken = default)
    {
        await LogAsync("LOGOUT", "User", userId);
    }

    public async Task Log2FAEnabledAsync(int userId, CancellationToken cancellationToken = default)
    {
        await LogAsync("2FA_ENABLED", "User", userId, null, AuditSeverity.Info);
    }

    public async Task Log2FADisabledAsync(int userId, CancellationToken cancellationToken = default)
    {
        await LogAsync("2FA_DISABLED", "User", userId, null, AuditSeverity.Warning);
    }

    public async Task LogPasswordChangedAsync(int userId, CancellationToken cancellationToken = default)
    {
        await LogAsync("PASSWORD_CHANGED", "User", userId, null, AuditSeverity.Info);
    }

    public async Task LogPasswordResetRequestAsync(string email, CancellationToken cancellationToken = default)
    {
        await LogAsync("PASSWORD_RESET_REQUEST", "User", null, new { Email = email }, AuditSeverity.Info);
    }

    // ========== PRODUCT OPERATIONS ==========

    public async Task LogProductCreatedAsync(int productId, string productName, CancellationToken cancellationToken = default)
    {
        await LogAsync("PRODUCT_CREATED", "Product", productId, new { Name = productName });
    }

    public async Task LogProductUpdatedAsync(int productId, string productName, object changes, CancellationToken cancellationToken = default)
    {
        await LogAsync("PRODUCT_UPDATED", "Product", productId, new { Name = productName, Changes = changes });
    }

    public async Task LogProductDeletedAsync(int productId, string productName, CancellationToken cancellationToken = default)
    {
        await LogAsync("PRODUCT_DELETED", "Product", productId, new { Name = productName }, AuditSeverity.Warning);
    }

    // ========== STOCK MOVEMENTS ==========

    public async Task LogStockMovementAsync(int productId, string productName, int quantityChange, string type, CancellationToken cancellationToken = default)
    {
        await LogAsync("STOCK_MOVEMENT", "Product", productId, new
        {
            Name = productName,
            QuantityChange = quantityChange,
            Type = type
        });
    }

    // ========== CATEGORY OPERATIONS ==========

    public async Task LogCategoryCreatedAsync(int categoryId, string categoryName, CancellationToken cancellationToken = default)
    {
        await LogAsync("CATEGORY_CREATED", "Category", categoryId, new { Name = categoryName });
    }

    public async Task LogCategoryUpdatedAsync(int categoryId, string categoryName, CancellationToken cancellationToken = default)
    {
        await LogAsync("CATEGORY_UPDATED", "Category", categoryId, new { Name = categoryName });
    }

    public async Task LogCategoryDeletedAsync(int categoryId, string categoryName, CancellationToken cancellationToken = default)
    {
        await LogAsync("CATEGORY_DELETED", "Category", categoryId, new { Name = categoryName }, AuditSeverity.Warning);
    }

    // ========== STORAGE LOCATION OPERATIONS ==========

    public async Task LogStorageLocationCreatedAsync(int locationId, string locationCode, CancellationToken cancellationToken = default)
    {
        await LogAsync("STORAGE_LOCATION_CREATED", "StorageLocation", locationId, new { Code = locationCode });
    }

    public async Task LogStorageLocationUpdatedAsync(int locationId, string locationCode, CancellationToken cancellationToken = default)
    {
        await LogAsync("STORAGE_LOCATION_UPDATED", "StorageLocation", locationId, new { Code = locationCode });
    }

    public async Task LogStorageLocationDeletedAsync(int locationId, string locationCode, CancellationToken cancellationToken = default)
    {
        await LogAsync("STORAGE_LOCATION_DELETED", "StorageLocation", locationId, new { Code = locationCode }, AuditSeverity.Warning);
    }

    // ========== EXPORT/IMPORT ==========

    public async Task LogExportAsync(string format, string entity, int recordCount, CancellationToken cancellationToken = default)
    {
        await LogAsync("DATA_EXPORT", entity, null, new
        {
            Format = format,
            RecordCount = recordCount
        });
    }

    public async Task LogImportAsync(string format, string entity, int recordCount, int successCount, int errorCount, CancellationToken cancellationToken = default)
    {
        var severity = errorCount > 0 ? AuditSeverity.Warning : AuditSeverity.Info;
        await LogAsync("DATA_IMPORT", entity, null, new
        {
            Format = format,
            TotalRecords = recordCount,
            SuccessCount = successCount,
            ErrorCount = errorCount
        }, severity);
    }

    // ========== USER MANAGEMENT ==========

    public async Task LogUserApprovedAsync(int userId, string username, CancellationToken cancellationToken = default)
    {
        await LogAsync("USER_APPROVED", "User", userId, new { Username = username });
    }

    public async Task LogUserRejectedAsync(int userId, string username, CancellationToken cancellationToken = default)
    {
        await LogAsync("USER_REJECTED", "User", userId, new { Username = username }, AuditSeverity.Warning);
    }

    public async Task LogUserDeletedAsync(int userId, string username, CancellationToken cancellationToken = default)
    {
        await LogAsync("USER_DELETED", "User", userId, new { Username = username }, AuditSeverity.Warning);
    }

    // ========== GDPR ==========

    public async Task LogGdprDataExportAsync(int userId, CancellationToken cancellationToken = default)
    {
        await LogAsync("GDPR_DATA_EXPORT", "User", userId);
    }

    public async Task LogGdprAccountDeletionAsync(int userId, string reason, CancellationToken cancellationToken = default)
    {
        await LogAsync("GDPR_ACCOUNT_DELETION", "User", userId, new { Reason = reason }, AuditSeverity.Warning);
    }

    // ========== QUERIES ==========

    public async Task<List<AuditLog>> GetRecentLogsAsync(int count = 100, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.AuditLogs
            .OrderByDescending(al => al.Timestamp)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AuditLog>> GetUserLogsAsync(int userId, int count = 100, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.AuditLogs
            .Where(al => al.UserId == userId)
            .OrderByDescending(al => al.Timestamp)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AuditLog>> GetEntityLogsAsync(string entity, int entityId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.AuditLogs
            .Where(al => al.Entity == entity && al.EntityId == entityId)
            .OrderByDescending(al => al.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, int>> GetActionStatisticsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.AuditLogs.AsQueryable();

        if (from.HasValue)
            query = query.Where(al => al.Timestamp >= from.Value);

        if (to.HasValue)
            query = query.Where(al => al.Timestamp <= to.Value);

        return await query
            .GroupBy(al => al.Action)
            .Select(g => new { Action = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Action, x => x.Count);
    }

    public async Task<List<AuditLog>> GetSecurityEventsAsync(int count = 50, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.AuditLogs
            .Where(al => al.Severity >= AuditSeverity.Warning ||
                al.Action.Contains("FAILED") ||
                al.Action.Contains("REJECTED") ||
                al.Action.Contains("DELETED"))
            .OrderByDescending(al => al.Timestamp)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    // ========== TAMPER-PROOF VERIFICATION ==========

    /// <summary>
    /// Verifies the integrity of the audit log hash chain.
    /// </summary>
    public async Task<AuditLogVerificationResult> VerifyIntegrityAsync(int? limitToLast = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var tamperProofLogger = _loggerFactory.CreateLogger<TamperProofAuditService>();
            var tamperProofService = new TamperProofAuditService(context, tamperProofLogger);

            return await tamperProofService.VerifyAuditLogIntegrityAsync(limitToLast);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying audit log integrity");
            return new AuditLogVerificationResult
            {
                IsValid = false,
                InvalidLogs = new List<InvalidLogEntry>
                {
                    new InvalidLogEntry
                    {
                        LogId = 0,
                        Reason = $"Verification failed: {ex.Message}"
                    }
                }
            };
        }
    }

    // ========== PRIVATE HELPERS ==========

    private async Task<User?> GetCurrentUserFromContextAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
            return null;

        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            return null;

        return await context.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive && !u.IsDeleted, cancellationToken);
    }

    private string? GetIpAddress(HttpContext? context)
    {
        if (context == null) return null;

        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }
}
