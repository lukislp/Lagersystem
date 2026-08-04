using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Self-service registration plus admin-driven user management
/// (approval, rejection, role changes). Extracted from <see cref="IAuthService"/>
/// to keep that interface focused on the live authentication flow.
/// </summary>
/// <remarks>
/// All mutations emit structured audit entries via the optional <see cref="IAuditService"/>.
/// Role changes enforce a simple RBAC policy:
/// <list type="bullet">
/// <item>The <c>SuperAdmin</c> role can neither be assigned nor removed here.</item>
/// <item>An <c>Admin</c> may only promote/demote users below <c>Admin</c>.</item>
/// <item>Roles below <c>Admin</c> are never allowed to change roles.</item>
/// </list>
/// </remarks>
public interface IUserRegistrationService
{
    /// <summary>
    /// Creates a new user in <c>Pending</c> approval state. Returns <c>null</c>
    /// when the username or e-mail is already taken, the target warehouse is
    /// missing/inactive, or the warehouse user quota is exhausted.
    /// </summary>
    Task<User?> RegisterAsync(
        string username,
        string email,
        string password,
        string displayName,
        int warehouseId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns pending users for a warehouse, including their warehouse navigation.</summary>
    Task<List<User>> GetPendingUsersAsync(int warehouseId, CancellationToken cancellationToken = default);

    /// <summary>Approves a pending user. Returns <c>false</c> if the user does not exist or is not pending.</summary>
    Task<bool> ApproveUserAsync(int userId, int approvedByUserId, string? notes = null, CancellationToken cancellationToken = default);

    /// <summary>Rejects a pending user and deactivates the account.</summary>
    Task<bool> RejectUserAsync(int userId, int rejectedByUserId, string? notes = null, CancellationToken cancellationToken = default);

    /// <summary>Changes a user's role while enforcing the RBAC policy described on the interface.</summary>
    Task<bool> ChangeUserRoleAsync(int userId, UserRole newRole, int changedByUserId, CancellationToken cancellationToken = default);
}
