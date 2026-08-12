namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Password-change and password-reset flows for authenticated and
/// unauthenticated users. Extracted from <see cref="IAuthService"/>.
/// </summary>
/// <remarks>
/// All methods are tolerant to recoverable failures (unknown user,
/// wrong old password, invalid/expired token) and return <c>false</c> /
/// <c>null</c> instead of throwing. Security-relevant attempts are
/// forwarded to <c>IAuditService</c> when available.
/// </remarks>
public interface IPasswordResetService
{
    /// <summary>
    /// Verifies <paramref name="oldPassword"/> and, on success, rehashes
    /// <paramref name="newPassword"/> using BCrypt and stamps
    /// <c>LastPasswordChangeAt</c>. Does not verify password policy; the
    /// caller is expected to have validated it via <see cref="IPasswordValidationService"/>.
    /// </summary>
    Task<bool> ChangePasswordAsync(int userId, string oldPassword, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a single-use password-reset token (24 h lifetime) for the
    /// active user with the given e-mail. All previous unused tokens for
    /// that user are invalidated. Returns <c>null</c> when no matching
    /// active user exists.
    /// </summary>
    Task<string?> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes a reset token, hashes <paramref name="newPassword"/> and
    /// clears the failed-login counter/lockout. Returns <c>false</c> for
    /// unknown, used or expired tokens.
    /// </summary>
    Task<bool> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> if the token exists, is unused and not expired.</summary>
    Task<bool> ValidateResetTokenAsync(string token, CancellationToken cancellationToken = default);
}
