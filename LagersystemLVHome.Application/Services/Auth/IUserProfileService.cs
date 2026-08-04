using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// User-profile and consent helpers used by <c>Login.razor</c>,
/// <c>Register.razor</c> and <c>Profile.razor</c>. Extracted from
/// <see cref="IAuthService"/> to keep that interface focused on the
/// authentication flow (login, 2FA, sessions).
/// </summary>
/// <remarks>
/// Implementations must be free of side effects outside the database and
/// must never throw on recoverable validation errors – return <c>false</c>
/// or <c>null</c> instead.
/// </remarks>
public interface IUserProfileService
{
    // Query helpers

    /// <summary>Returns <c>true</c> when at least one user row exists (setup check).</summary>
    Task<bool> AnyUsersExistAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds a user by username; returns <c>null</c> for blank input or unknown user.</summary>
    Task<User?> GetActiveUserByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>Finds an active, non-deleted user by e-mail.</summary>
    Task<User?> GetActiveUserByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Loads a user including its warehouse navigation.</summary>
    Task<User?> GetUserWithWarehouseAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Counts approved + active users in the given warehouse.</summary>
    Task<int> CountApprovedActiveUsersInWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default);

    /// <summary>Counts active users in the given warehouse (ignores approval state).</summary>
    Task<int> CountActiveUsersInWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default);

    /// <summary>Returns the serialised 2FA recovery codes for a user, or <c>null</c>.</summary>
    Task<string?> GetTwoFactorRecoveryCodesAsync(int userId, CancellationToken cancellationToken = default);

    // Mutation helpers

    /// <summary>Stores consent flags and stamps the acceptance timestamps.</summary>
    Task<bool> UpdateConsentPreferencesAsync(int userId, bool analyticsConsent, bool fingerprintConsent, CancellationToken cancellationToken = default);

    /// <summary>Sets the profile image path and timestamp (used by Register.razor).</summary>
    Task<bool> SetProfileImagePathAsync(int userId, string profileImagePath, CancellationToken cancellationToken = default);

    /// <summary>Sets or clears the profile image path (used by Profile.razor).</summary>
    Task<bool> UpdateProfileImageAsync(int userId, string? profileImagePath, CancellationToken cancellationToken = default);

    /// <summary>Promotes a user to <see cref="UserRole.Admin"/> and approves them.</summary>
    Task<bool> ApproveAsAdminAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Revokes marketing consent and clears its timestamp.</summary>
    Task<bool> RevokeMarketingConsentAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a specific consent type (<c>Analytics</c>, <c>Analytics &amp; Performance</c>,
    /// <c>Fingerprint</c>, <c>Device Fingerprinting</c>). Returns <c>false</c> for unknown types.
    /// </summary>
    Task<bool> RevokeConsentAsync(int userId, string consentType, CancellationToken cancellationToken = default);
}
