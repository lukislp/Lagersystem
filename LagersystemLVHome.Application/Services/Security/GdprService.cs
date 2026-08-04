using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LagersystemLVHome.Application.Services;

public sealed class GdprService : IGdprService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IAuditService _auditService;

    public GdprService(IDbContextFactory<InventoryDbContext> contextFactory, IAuditService auditService)
    {
        _contextFactory = contextFactory;
        _auditService = auditService;
    }

    public async Task<bool> GiveConsentAsync(int userId, bool marketingConsent = false, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.FindAsync(userId);
        if (user == null) return false;

        user.GdprConsentGiven = true;
        user.GdprConsentDate = DateTime.UtcNow;
        user.GdprConsentVersion = "1.0";
        user.MarketingConsent = marketingConsent;
        user.MarketingConsentDate = marketingConsent ? DateTime.UtcNow : null;

        await context.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync("GDPR_CONSENT", "User", userId, new { MarketingConsent = marketingConsent });

        return true;
    }

    public async Task<UserDataExport> ExportUserDataAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users
            .Include(u => u.Warehouse)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
            throw new ArgumentException("Benutzer nicht gefunden");

        // Collect all user data
        var stockMovements = await context.StockMovements
            .Where(sm => sm.ProductId == userId)
            .Select(sm => new
            {
                sm.Id,
                sm.Type,
                sm.QuantityChange,
                sm.Timestamp,
                sm.Notes
            })
            .ToListAsync(cancellationToken);

        var auditLogs = await context.AuditLogs
            .Where(al => al.UserId == userId)
            .Select(al => new
            {
                al.Action,
                al.Entity,
                al.Timestamp,
                al.IpAddress
            })
            .ToListAsync(cancellationToken);

        var export = new UserDataExport
        {
            ExportDate = DateTime.UtcNow,
            User = new
            {
                user.Id,
                user.Username,
                user.Email,
                user.DisplayName,
                user.CreatedAt,
                user.LastLoginAt,
                Warehouse = user.Warehouse?.Name
            },
            StockMovements = stockMovements,
            AuditLogs = auditLogs,
            GdprInfo = new
            {
                user.GdprConsentGiven,
                user.GdprConsentDate,
                user.MarketingConsent
            }
        };

        await _auditService.LogAsync("GDPR_EXPORT", "User", userId);

        return export;
    }

    public async Task<bool> DeleteUserAccountAsync(int userId, string reason, bool hardDelete = false, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.FindAsync(userId);
        if (user == null) return false;

        if (hardDelete)
        {
            // Hard delete: removes all data completely (testing only)
            context.Users.Remove(user);
            await _auditService.LogAsync("GDPR_HARD_DELETE", "User", userId, new { Reason = reason }, AuditSeverity.Critical);
        }
        else
        {
            // Soft delete: mark as deleted and anonymize
            user.IsDeleted = true;
            user.DeletedAt = DateTime.UtcNow;
            user.DeletionReason = reason;
            user.IsActive = false;

            // Anonymize personal data
            user.Email = $"deleted_{userId}@anonymized.local";
            user.DisplayName = $"Gel\u00f6schter Benutzer {userId}";
            user.PasswordHash = string.Empty;
            user.LastLoginIp = null;

            await _auditService.LogAsync("GDPR_SOFT_DELETE", "User", userId, new { Reason = reason }, AuditSeverity.Warning);
        }

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AnonymizeUserDataAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.FindAsync(userId);
        if (user == null) return false;

        // Anonymize all personal data
        user.Email = $"anonymized_{userId}@local";
        user.DisplayName = "Anonymisierter Benutzer";
        user.LastLoginIp = null;

        // Anonymize audit logs
        var auditLogs = await context.AuditLogs
            .Where(al => al.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var log in auditLogs)
        {
            log.IpAddress = "xxx.xxx.xxx.xxx";
            log.UserAgent = "Anonymized";
        }

        await context.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync("GDPR_ANONYMIZE", "User", userId);

        return true;
    }

    public async Task<List<User>> GetInactiveUsersAsync(int daysInactive = 365, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var cutoffDate = DateTime.UtcNow.AddDays(-daysInactive);

        return await context.Users
            .Where(u => u.LastLoginAt < cutoffDate && u.IsActive && !u.IsDeleted)
            .ToListAsync(cancellationToken);
    }
}

public sealed class UserDataExport
{
    public DateTime ExportDate { get; set; }
    public object User { get; set; } = new();
    public object StockMovements { get; set; } = new();
    public object AuditLogs { get; set; } = new();
    public object GdprInfo { get; set; } = new();

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
