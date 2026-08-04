using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

/// <inheritdoc cref="IAuditQueryService"/>
public sealed class AuditQueryService : IAuditQueryService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;

    public AuditQueryService(IDbContextFactory<InventoryDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<User>> GetActiveUsersForFilterAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && !u.IsDeleted)
            .OrderBy(u => u.Username)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditLog>> GetAuditLogsAsync(
        AuditLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (filter.UserId is > 0)
        {
            query = query.Where(l => l.UserId == filter.UserId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(l => l.Action == filter.Action);
        }

        if (filter.Severity is { } severity)
        {
            query = query.Where(l => l.Severity == severity);
        }

        return await query
            .OrderByDescending(l => l.Timestamp)
            .Take(filter.TakeCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<AuditLogStats> GetAuditLogStatsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var total = await db.AuditLogs.CountAsync(cancellationToken);
        var info = await db.AuditLogs.CountAsync(l => l.Severity == AuditSeverity.Info, cancellationToken);
        var warning = await db.AuditLogs.CountAsync(l => l.Severity == AuditSeverity.Warning, cancellationToken);
        var errorAndCritical = await db.AuditLogs.CountAsync(
            l => l.Severity == AuditSeverity.Critical || l.Severity == AuditSeverity.Error,
            cancellationToken);

        return new AuditLogStats(total, info, warning, errorAndCritical);
    }
}
