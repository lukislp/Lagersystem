using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Core authentication API: live login/logout flows plus the verification
/// counterparts used while a login is in progress. User management, profile,
/// consents, password reset, 2FA management and user registration have been
/// moved to dedicated services (see <see cref="IUserProfileService"/>,
/// <see cref="IPasswordResetService"/>, <see cref="IUserRegistrationService"/>
/// and <see cref="ITwoFactorManagementService"/>).
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Verifies credentials and, on success, establishes a session. Returns
    /// a <see cref="Result{T}"/> whose <see cref="Result{T}.ErrorCode"/> is one
    /// of the constants defined in <see cref="LoginFailures"/> when authentication
    /// is rejected. UI code should switch on the error code instead of
    /// duplicating the rejection rules itself.
    /// </summary>
    Task<Result<User>> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
    Task<User?> GetCurrentUserAsync(CancellationToken cancellationToken = default);
    Task<string?> GetCurrentSessionIdAsync(CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    bool IsAuthenticated();
    int GetCurrentWarehouseId();

    // Two-Factor Authentication (verification only; management lives in ITwoFactorManagementService)
    Task<bool> Verify2FACodeAsync(int userId, string code, CancellationToken cancellationToken = default);
    Task<bool> Verify2FARecoveryCodeAsync(int userId, string recoveryCode, CancellationToken cancellationToken = default);
    Task<bool> VerifyEmailOtpAsync(int userId, string code, CancellationToken cancellationToken = default);

    // Magic-link login flow (the send/info helpers live on IPasswordlessLoginService; this one
    // stays here because it performs the actual session establishment.)
    /// <summary>
    /// Validates a magic-link token and, on success, establishes a session.
    /// Failure codes are defined in <see cref="LoginFailures"/> –
    /// e.g. <see cref="LoginFailures.MagicLinkInvalid"/>,
    /// <see cref="LoginFailures.GdprConsentRequired"/>,
    /// <see cref="LoginFailures.GranularConsentRequired"/>,
    /// <see cref="LoginFailures.IpDenied"/>,
    /// <see cref="LoginFailures.PasswordlessUnavailable"/>.
    /// </summary>
    Task<Result<User>> LoginWithMagicLinkAsync(string token, CancellationToken cancellationToken = default);

    // Passkey / WebAuthn login flow.
    /// <summary>
    /// Establishes a session for a user that has just completed a successful
    /// WebAuthn/passkey assertion. Failure codes are defined in
    /// <see cref="LoginFailures"/> – e.g. <see cref="LoginFailures.UserNotFound"/>,
    /// <see cref="LoginFailures.Inactive"/>, <see cref="LoginFailures.PendingApproval"/>,
    /// <see cref="LoginFailures.Rejected"/>, <see cref="LoginFailures.GdprConsentRequired"/>,
    /// <see cref="LoginFailures.GranularConsentRequired"/>,
    /// <see cref="LoginFailures.IpDenied"/>.
    /// </summary>
    Task<Result<User>> LoginWithPasskeyAsync(int userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about enabled 2FA methods for a user.
/// </summary>
public sealed class TwoFactorMethodInfo
{
    public bool AuthenticatorEnabled { get; set; }
    public bool EmailOtpEnabled { get; set; }
    public string PreferredMethod { get; set; } = "Authenticator";

    /// <summary>
    /// True if at least one 2FA method is active.
    /// </summary>
    public bool Any2FAEnabled => AuthenticatorEnabled || EmailOtpEnabled;

    public List<string> AvailableMethods
    {
        get
        {
            var methods = new List<string>();
            if (AuthenticatorEnabled) methods.Add("Authenticator");
            if (EmailOtpEnabled) methods.Add("EmailOtp");
            return methods;
        }
    }
}
