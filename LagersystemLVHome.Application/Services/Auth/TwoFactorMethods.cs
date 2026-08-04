namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Canonical string values for 2FA methods stored on <see cref="Domain.Models.User.Preferred2FAMethod"/>
/// and exchanged with the UI. Use these constants instead of string literals to keep the
/// code typo-safe and refactor-friendly.
/// </summary>
public static class TwoFactorMethods
{
    /// <summary>TOTP / authenticator-app based 2FA.</summary>
    public const string Authenticator = "Authenticator";

    /// <summary>One-time password delivered via e-mail.</summary>
    public const string EmailOtp = "EmailOtp";

    /// <summary>Returns <c>true</c> if <paramref name="method"/> is one of the known values.</summary>
    public static bool IsKnown(string? method) =>
        method is Authenticator or EmailOtp;
}
