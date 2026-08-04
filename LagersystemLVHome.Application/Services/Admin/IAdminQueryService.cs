using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Read-only queries used by administration pages. Encapsulates direct
/// DbContext access so the Razor components stay free of persistence concerns.
/// </summary>
public interface IAdminQueryService
{
    /// <summary>Dashboard-level counts for the admin landing page.</summary>
    Task<AdminDashboardStats> GetDashboardStatsAsync(int warehouseId, CancellationToken cancellationToken = default);

    /// <summary>Users grouped by <see cref="UserApprovalStatus"/> for the given warehouse.</summary>
    Task<UsersByApprovalStatus> GetUsersByApprovalStatusAsync(int warehouseId, CancellationToken cancellationToken = default);

    /// <summary>Email of the active SuperAdmin, or <c>null</c> if none exists.</summary>
    Task<string?> GetSuperAdminEmailAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Active users including their <see cref="Warehouse"/>. When
    /// <paramref name="warehouseId"/> is <c>null</c> (SuperAdmin view) all
    /// warehouses are included.
    /// </summary>
    Task<IReadOnlyList<User>> GetActiveUsersWithWarehouseAsync(int? warehouseId, CancellationToken cancellationToken = default);
}

public sealed record AdminDashboardStats(
    int TotalUsers,
    int PendingUsers,
    int TotalWarehouses,
    int ActiveWarehouses,
    int TotalProducts,
    int TotalMovements);

public sealed record UsersByApprovalStatus(
    IReadOnlyList<User> Pending,
    IReadOnlyList<User> Approved,
    IReadOnlyList<User> Rejected);
