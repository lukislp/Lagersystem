using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Two-factor authentication *management* (enable / disable / preferred method / info).
/// Extracted from <see cref="IAuthService"/>; the <c>Verify*</c> counterparts intentionally
/// remain on <see cref="IAuthService"/> because they are part of the live login flow.
/// </summary>
public interface ITwoFactorManagementService
{
    /// <summary>
    /// Enables authenticator-app 2FA for the user. Validates the verification code
    /// against <paramref name="secret"/>, generates recovery codes and persists them.
    /// Returns <c>false</c> if the code is wrong or <see cref="ITwoFactorService"/> is not registered.
    /// </summary>
    Task<bool> Enable2FAAsync(int userId, string secret, string verificationCode, CancellationToken cancellationToken = default);

    /// <summary>Disables authenticator-app 2FA after verifying the user's password.</summary>
    Task<bool> Disable2FAAsync(int userId, string password, CancellationToken cancellationToken = default);

    /// <summary>Enables the e-mail OTP method; falls back to being the preferred method if no authenticator is active.</summary>
    Task<bool> EnableEmailOtpAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Disables e-mail OTP after verifying the user's password.</summary>
    Task<bool> DisableEmailOtpAsync(int userId, string password, CancellationToken cancellationToken = default);

    /// <summary>Sends a fresh e-mail OTP to the user. Returns <c>false</c> if <c>IEmailOtpService</c> is not registered.</summary>
    Task<bool> SendEmailOtpAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the preferred 2FA method. Accepted values are <c>"Authenticator"</c> and <c>"EmailOtp"</c>;
    /// only methods that are actually enabled for the user may be selected.
    /// </summary>
    Task<bool> SetPreferred2FAMethodAsync(int userId, string method, CancellationToken cancellationToken = default);

    /// <summary>Returns aggregated 2FA configuration for UI display.</summary>
    Task<TwoFactorMethodInfo> Get2FAMethodsAsync(int userId, CancellationToken cancellationToken = default);
}
