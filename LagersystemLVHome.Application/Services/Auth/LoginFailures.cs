namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Stable error codes returned by <see cref="IAuthService.LoginAsync"/> when
/// authentication fails. Codes are intentionally machine-readable strings so
/// they can be mapped to localised UI messages without leaking internal
/// implementation details into log output.
/// </summary>
public static class LoginFailures
{
    /// <summary>The supplied username does not exist.</summary>
    public const string UserNotFound = "login.user_not_found";

    /// <summary>
    /// Account is currently locked due to repeated failed attempts.
    /// <see cref="Result{T}.ErrorMessage"/> contains the remaining lockout
    /// minutes (rounded up) as a string.
    /// </summary>
    public const string AccountLocked = "login.account_locked";

    /// <summary>
    /// The caller's IP address is not allowed to sign in for this user.
    /// <see cref="Result{T}.ErrorMessage"/> contains the matched rule, if any.
    /// </summary>
    public const string IpDenied = "login.ip_denied";

    /// <summary>Password verification failed.</summary>
    public const string InvalidPassword = "login.invalid_password";

    /// <summary>The user has not yet accepted the GDPR consent.</summary>
    public const string GdprConsentRequired = "login.gdpr_consent_required";

    /// <summary>
    /// At least one mandatory granular consent (analytics or device fingerprint)
    /// is missing.
    /// </summary>
    public const string GranularConsentRequired = "login.granular_consent_required";

    /// <summary>Account is disabled or soft-deleted.</summary>
    public const string Inactive = "login.inactive";

    /// <summary>Approval is still pending.</summary>
    public const string PendingApproval = "login.pending_approval";

    /// <summary>Account registration was rejected by an administrator.</summary>
    public const string Rejected = "login.rejected";

    /// <summary>
    /// The magic-link token is unknown, expired or has already been consumed.
    /// Returned by <see cref="IAuthService.LoginWithMagicLinkAsync"/>.
    /// </summary>
    public const string MagicLinkInvalid = "login.magic_link_invalid";

    /// <summary>
    /// Passwordless login (magic link / passkey) is not configured on this
    /// instance. Returned when the optional collaborator is missing.
    /// </summary>
    public const string PasswordlessUnavailable = "login.passwordless_unavailable";
}
