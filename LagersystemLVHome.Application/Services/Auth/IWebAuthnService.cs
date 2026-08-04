using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Service for WebAuthn/Passkey operations.
/// </summary>
public interface IWebAuthnService
{
    Task<PasskeyRegistrationOptions> GenerateRegistrationOptionsAsync(int userId, string deviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies and stores a new passkey.
    /// </summary>
    Task<PasskeyRegistrationResult> VerifyRegistrationAsync(int userId, string credentialJson, string sessionId, CancellationToken cancellationToken = default);

    Task<PasskeyAuthenticationOptions> GenerateAuthenticationOptionsAsync(string? username = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a passkey authentication.
    /// </summary>
    Task<PasskeyAuthenticationResult> VerifyAuthenticationAsync(string credentialJson, string sessionId, CancellationToken cancellationToken = default);

    Task<List<UserPasskey>> GetUserPasskeysAsync(int userId, CancellationToken cancellationToken = default);

    Task<bool> DeletePasskeyAsync(int userId, int passkeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a passkey.
    /// </summary>
    Task<bool> RenamePasskeyAsync(int userId, int passkeyId, string newName, CancellationToken cancellationToken = default);

    Task<bool> HasPasskeysAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired challenges.
    /// </summary>
    Task CleanupExpiredChallengesAsync(CancellationToken cancellationToken = default);
}
