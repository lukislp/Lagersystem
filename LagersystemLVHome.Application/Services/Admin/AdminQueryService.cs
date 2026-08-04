using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

/// <inheritdoc cref="IAdminQueryService"/>
public sealed class AdminQueryService : IAdminQueryService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;

    public AdminQueryService(IDbContextFactory<InventoryDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<AdminDashboardStats> GetDashboardStatsAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var totalUsers = await db.Users.CountAsync(cancellationToken);
        var pendingUsers = await db.Users
            .CountAsync(u => u.ApprovalStatus == UserApprovalStatus.Pending, cancellationToken);

        var totalWarehouses = await db.Warehouses.CountAsync(cancellationToken);
        var activeWarehouses = await db.Warehouses
            .CountAsync(w => w.IsActive, cancellationToken);

        var totalProducts = await db.Products.CountAsync(cancellationToken);
        var totalMovements = await db.StockMovements.CountAsync(cancellationToken);

        return new AdminDashboardStats(
            totalUsers,
            pendingUsers,
            totalWarehouses,
            activeWarehouses,
            totalProducts,
            totalMovements);
    }

    public async Task<UsersByApprovalStatus> GetUsersByApprovalStatusAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var pending = await db.Users
            .AsNoTracking()
            .Where(u => u.WarehouseId == warehouseId && u.ApprovalStatus == UserApprovalStatus.Pending)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(cancellationToken);

        var approved = await db.Users
            .AsNoTracking()
            .Where(u => u.WarehouseId == warehouseId && u.ApprovalStatus == UserApprovalStatus.Approved)
            .OrderBy(u => u.Username)
            .ToListAsync(cancellationToken);

        var rejected = await db.Users
            .AsNoTracking()
            .Where(u => u.WarehouseId == warehouseId && u.ApprovalStatus == UserApprovalStatus.Rejected)
            .OrderByDescending(u => u.ApprovedAt)
            .ToListAsync(cancellationToken);

        return new UsersByApprovalStatus(pending, approved, rejected);
    }

    public async Task<string?> GetSuperAdminEmailAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var superAdmin = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Role == UserRole.SuperAdmin && u.IsActive && !u.IsDeleted,
                cancellationToken);

        return superAdmin?.Email;
    }

    public async Task<IReadOnlyList<User>> GetActiveUsersWithWarehouseAsync(
        int? warehouseId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Users
            .AsNoTracking()
            .Include(u => u.Warehouse)
            .Where(u => u.IsActive);

        if (warehouseId.HasValue)
        {
            query = query.Where(u => u.WarehouseId == warehouseId.Value);
        }

        return await query
            .OrderBy(u => u.DisplayName)
            .ToListAsync(cancellationToken);
    }
}
