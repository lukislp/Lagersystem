using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

public sealed class WebAuthnService : IWebAuthnService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<WebAuthnService> _logger;
    private readonly IAuditService? _auditService;
    private readonly IConfiguration _configuration;

    private string RpId => _configuration["WebAuthn:RpId"] ?? "localhost";
    private string RpName => _configuration["WebAuthn:RpName"] ?? "LagerSystem";
    private string Origin => _configuration["WebAuthn:Origin"] ?? "https://localhost";

    public WebAuthnService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<WebAuthnService> logger,
        IConfiguration configuration,
        IAuditService? auditService = null)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _configuration = configuration;
        _auditService = auditService;
    }

    public async Task<PasskeyRegistrationOptions> GenerateRegistrationOptionsAsync(int userId, string deviceName, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FindAsync(userId);
        if (user == null)
        {
            return new PasskeyRegistrationOptions { Success = false, Error = "Benutzer nicht gefunden" };
        }

        var challenge = GenerateChallenge();
        var sessionId = Guid.NewGuid().ToString("N");

        var challengeEntity = new WebAuthnChallenge
        {
            UserId = userId,
            Challenge = challenge,
            OperationType = "register",
            SessionId = sessionId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };

        context.WebAuthnChallenges.Add(challengeEntity);
        await context.SaveChangesAsync(cancellationToken);

        // Get existing credentials for exclusion
        var existingCredentials = await context.UserPasskeys
            .Where(p => p.UserId == userId && p.IsActive)
            .Select(p => new { p.CredentialId, p.Transports })
            .ToListAsync(cancellationToken);

        var excludeCredentials = existingCredentials.Select(c => new PublicKeyCredentialDescriptor
        {
            Type = "public-key",
            Id = c.CredentialId,
            Transports = string.IsNullOrEmpty(c.Transports)
                ? new[] { "internal", "usb", "ble", "nfc" }
                : c.Transports.Split(',')
        }).ToList();

        _logger.LogInformation("WebAuthn registration options generated for user {UserId}", userId);

        return new PasskeyRegistrationOptions
        {
            Success = true,
            SessionId = sessionId,
            Challenge = challenge,
            RpId = RpId,
            RpName = RpName,
            UserId = Base64UrlEncode(Encoding.UTF8.GetBytes(userId.ToString())),
            UserName = user.Username,
            UserDisplayName = user.DisplayName ?? user.Username,
            ExcludeCredentials = excludeCredentials,
            AuthenticatorSelection = new AuthenticatorSelectionCriteria
            {
                AuthenticatorAttachment = null,
                ResidentKey = "preferred",
                UserVerification = "preferred"
            },
            Timeout = 300000,
            Attestation = "none"
        };
    }

    public async Task<PasskeyRegistrationResult> VerifyRegistrationAsync(int userId, string credentialJson, string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("VerifyRegistrationAsync called for user {UserId}, sessionId: {SessionId}", userId, sessionId);
            _logger.LogDebug("Credential JSON length: {Length}, content: {Json}",
                credentialJson?.Length ?? 0,
                credentialJson?[..Math.Min(200, credentialJson?.Length ?? 0)] ?? "null");

            if (string.IsNullOrWhiteSpace(credentialJson))
            {
                _logger.LogWarning("VerifyRegistrationAsync: credentialJson is null or empty");
                return new PasskeyRegistrationResult { Success = false, Error = "Keine Credential-Daten erhalten" };
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var challenge = await context.WebAuthnChallenges
                .FirstOrDefaultAsync(c => c.SessionId == sessionId
                    && c.UserId == userId
                    && c.OperationType == "register"
                    && !c.IsUsed
                    && c.ExpiresAt > DateTime.UtcNow, cancellationToken);

            if (challenge == null)
            {
                _logger.LogWarning("WebAuthn registration failed: Invalid or expired challenge for user {UserId}", userId);
                return new PasskeyRegistrationResult { Success = false, Error = "Challenge ung\u00fcltig oder abgelaufen" };
            }

            var credential = JsonSerializer.Deserialize<WebAuthnRegistrationCredential>(credentialJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (credential == null)
            {
                _logger.LogWarning("WebAuthn registration failed: Could not deserialize credential JSON");
                return new PasskeyRegistrationResult { Success = false, Error = "Ung\u00fcltige Credential-Daten" };
            }

            _logger.LogInformation("Credential parsed successfully. Id: {Id}, Type: {Type}",
                credential.Id?[..Math.Min(20, credential.Id?.Length ?? 0)] ?? "null",
                credential.Type ?? "null");

            // Validate client data
            var clientDataJson = Base64UrlDecode(credential.Response.ClientDataJSON);
            var clientData = JsonSerializer.Deserialize<WebAuthnClientData>(clientDataJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (clientData == null)
            {
                return new PasskeyRegistrationResult { Success = false, Error = "Ung\u00fcltige Client-Daten" };
            }

            // Verify challenge
            if (clientData.Challenge != challenge.Challenge)
            {
                _logger.LogWarning("WebAuthn registration failed: Challenge mismatch for user {UserId}", userId);
                return new PasskeyRegistrationResult { Success = false, Error = "Challenge stimmt nicht \u00fcberein" };
            }

            // Verify origin
            if (!clientData.Origin.Equals(Origin, StringComparison.OrdinalIgnoreCase) &&
                !clientData.Origin.StartsWith("https://localhost") &&
                !clientData.Origin.StartsWith("http://localhost"))
            {
                _logger.LogWarning("WebAuthn registration failed: Origin mismatch. Expected {Expected}, got {Actual}", Origin, clientData.Origin);
                return new PasskeyRegistrationResult { Success = false, Error = "Origin stimmt nicht überein" };
            }

            // Verify type
            if (clientData.Type != "webauthn.create")
            {
                return new PasskeyRegistrationResult { Success = false, Error = "Ung\u00fcltiger Operation-Typ" };
            }

            // Parse authenticator data
            var attestationObjectBytes = Base64UrlDecodeToBytes(credential.Response.AttestationObject);
            var (authData, publicKey, aaguid) = ParseAttestationObject(attestationObjectBytes);

            if (authData == null || publicKey == null)
            {
                return new PasskeyRegistrationResult { Success = false, Error = "Fehler beim Parsen der Attestation" };
            }

            var passkey = new UserPasskey
            {
                UserId = userId,
                CredentialId = credential.Id,
                PublicKey = Convert.ToBase64String(publicKey),
                SignatureCounter = 0,
                AaGuid = aaguid,
                DeviceName = credential.DeviceName ?? "Passkey",
                CredentialType = credential.Type ?? "public-key",
                Transports = credential.Response.Transports != null
                    ? string.Join(",", credential.Response.Transports)
                    : null,
                IsDiscoverable = true,
                UserVerified = authData.UserVerified,
                RegisteredFromIp = credential.RegisteredFromIp,
                RegisteredUserAgent = credential.RegisteredUserAgent
            };

            context.UserPasskeys.Add(passkey);

            challenge.IsUsed = true;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("WebAuthn passkey registered successfully for user {UserId}, CredentialId: {CredentialId}",
                userId, credential.Id[..Math.Min(20, credential.Id.Length)] + "...");

            if (_auditService != null)
            {
                await _auditService.LogAsync("PASSKEY_REGISTERED", "UserPasskey", passkey.Id,
                    new { UserId = userId, DeviceName = passkey.DeviceName }, AuditSeverity.Info);
            }

            return new PasskeyRegistrationResult
            {
                Success = true,
                PasskeyId = passkey.Id,
                DeviceName = passkey.DeviceName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebAuthn registration for user {UserId}", userId);
            return new PasskeyRegistrationResult { Success = false, Error = "Interner Fehler bei der Registrierung" };
        }
    }

    public async Task<PasskeyAuthenticationOptions> GenerateAuthenticationOptionsAsync(string? username = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var challenge = GenerateChallenge();
        var sessionId = Guid.NewGuid().ToString("N");

        int? userId = null;
        var allowCredentials = new List<PublicKeyCredentialDescriptor>();

        if (!string.IsNullOrEmpty(username))
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive && !u.IsDeleted, cancellationToken);
            if (user != null)
            {
                userId = user.Id;

                var credentials = await context.UserPasskeys
                    .Where(p => p.UserId == user.Id && p.IsActive)
                    .Select(p => new { p.CredentialId, p.Transports })
                    .ToListAsync(cancellationToken);

                allowCredentials = credentials.Select(c => new PublicKeyCredentialDescriptor
                {
                    Type = "public-key",
                    Id = c.CredentialId,
                    Transports = string.IsNullOrEmpty(c.Transports)
                        ? new[] { "internal", "usb", "ble", "nfc" }
                        : c.Transports.Split(',')
                }).ToList();
            }
        }

        var challengeEntity = new WebAuthnChallenge
        {
            UserId = userId,
            Challenge = challenge,
            OperationType = "authenticate",
            SessionId = sessionId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };

        context.WebAuthnChallenges.Add(challengeEntity);
        await context.SaveChangesAsync(cancellationToken);

        return new PasskeyAuthenticationOptions
        {
            Success = true,
            SessionId = sessionId,
            Challenge = challenge,
            RpId = RpId,
            Timeout = 300000,
            UserVerification = "preferred",
            AllowCredentials = allowCredentials.Any() ? allowCredentials : null
        };
    }

    public async Task<PasskeyAuthenticationResult> VerifyAuthenticationAsync(string credentialJson, string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("VerifyAuthenticationAsync called. SessionId: {SessionId}", sessionId);
            _logger.LogDebug("Credential JSON: {Json}", credentialJson?[..Math.Min(500, credentialJson?.Length ?? 0)] ?? "null");

            if (string.IsNullOrWhiteSpace(credentialJson))
            {
                _logger.LogWarning("VerifyAuthenticationAsync: credentialJson is null or empty");
                return new PasskeyAuthenticationResult { Success = false, Error = "Keine Credential-Daten erhalten" };
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var credential = JsonSerializer.Deserialize<WebAuthnAuthenticationCredential>(credentialJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (credential == null)
            {
                _logger.LogWarning("VerifyAuthenticationAsync: Could not deserialize credential JSON");
                return new PasskeyAuthenticationResult { Success = false, Error = "Ung\u00fcltige Credential-Daten" };
            }

            _logger.LogInformation("Credential parsed. Id: {Id}", credential.Id?[..Math.Min(30, credential.Id?.Length ?? 0)] ?? "null");

            var passkey = await context.UserPasskeys
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.CredentialId == credential.Id && p.IsActive, cancellationToken);

            if (passkey == null)
            {
                var allPasskeys = await context.UserPasskeys.Where(p => p.IsActive).Select(p => new { p.Id, CredIdStart = p.CredentialId.Substring(0, Math.Min(30, p.CredentialId.Length)) }).ToListAsync(cancellationToken);
                _logger.LogWarning("WebAuthn authentication failed: Passkey not found for CredentialId {CredentialId}. Active passkeys: {Passkeys}",
                    credential.Id?[..Math.Min(30, credential.Id?.Length ?? 0)] ?? "null",
                    string.Join(", ", allPasskeys.Select(p => $"[{p.Id}:{p.CredIdStart}...]")));
                return new PasskeyAuthenticationResult { Success = false, Error = "Passkey nicht gefunden" };
            }

            if (passkey.User == null)
            {
                _logger.LogWarning("WebAuthn authentication failed: Passkey found but User is null for PasskeyId {PasskeyId}", passkey.Id);
                return new PasskeyAuthenticationResult { Success = false, Error = "Benutzer nicht gefunden" };
            }

            _logger.LogInformation("Passkey found for user {UserId} ({Username})", passkey.UserId, passkey.User.Username);

            var challenge = await context.WebAuthnChallenges
                .FirstOrDefaultAsync(c => c.SessionId == sessionId
                    && c.OperationType == "authenticate"
                    && !c.IsUsed
                    && c.ExpiresAt > DateTime.UtcNow, cancellationToken);

            if (challenge == null)
            {
                var allChallenges = await context.WebAuthnChallenges
                    .Where(c => !c.IsUsed && c.ExpiresAt > DateTime.UtcNow)
                    .Select(c => new { c.SessionId, c.OperationType, c.Challenge })
                    .ToListAsync(cancellationToken);
                _logger.LogWarning("WebAuthn authentication failed: Challenge not found for SessionId {SessionId}. Available challenges: {Challenges}",
                    sessionId,
                    string.Join(", ", allChallenges.Select(c => $"[{c.SessionId?[..Math.Min(10, c.SessionId?.Length ?? 0)]}:{c.OperationType}]")));
                return new PasskeyAuthenticationResult { Success = false, Error = "Challenge ung\u00fcltig oder abgelaufen" };
            }

            _logger.LogInformation("Challenge found. Stored challenge: {StoredChallenge}", challenge.Challenge?[..Math.Min(20, challenge.Challenge?.Length ?? 0)]);

            // Validate client data
            var clientDataJson = Base64UrlDecode(credential.Response.ClientDataJSON);
            _logger.LogDebug("ClientDataJSON decoded: {ClientData}", clientDataJson);

            var clientData = JsonSerializer.Deserialize<WebAuthnClientData>(clientDataJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (clientData == null)
            {
                return new PasskeyAuthenticationResult { Success = false, Error = "Ung\u00fcltige Client-Daten" };
            }

            _logger.LogInformation("ClientData parsed. Challenge from client: {ClientChallenge}, Type: {Type}, Origin: {Origin}",
                clientData.Challenge?[..Math.Min(20, clientData.Challenge?.Length ?? 0)],
                clientData.Type,
                clientData.Origin);

            // Challenge comparison - both are Base64URL-encoded
            if (clientData.Challenge != challenge.Challenge)
            {
                _logger.LogWarning("WebAuthn authentication failed: Challenge mismatch. Stored: {Stored}, FromClient: {FromClient}",
                    challenge.Challenge,
                    clientData.Challenge);
                return new PasskeyAuthenticationResult { Success = false, Error = "Challenge-Validierung fehlgeschlagen" };
            }

            if (clientData.Type != "webauthn.get")
            {
                return new PasskeyAuthenticationResult { Success = false, Error = "Ung\u00fcltiger Operation-Typ" };
            }

            // Parse authenticator data
            var authDataBytes = Base64UrlDecodeToBytes(credential.Response.AuthenticatorData);
            var authData = ParseAuthenticatorData(authDataBytes);

            // Verify RP ID hash
            var expectedRpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(RpId));
            if (!authData.RpIdHash.SequenceEqual(expectedRpIdHash))
            {
                _logger.LogWarning("WebAuthn authentication failed: RP ID Hash mismatch. Expected RpId: {RpId}", RpId);
                return new PasskeyAuthenticationResult { Success = false, Error = "RP ID Hash stimmt nicht \u00fcberein" };
            }

            // Verify user present flag
            if (!authData.UserPresent)
            {
                return new PasskeyAuthenticationResult { Success = false, Error = "User Presence nicht best\u00e4tigt" };
            }

            // Verify signature counter (replay protection)
            if (authData.SignatureCounter <= passkey.SignatureCounter && passkey.SignatureCounter > 0)
            {
                _logger.LogWarning("WebAuthn authentication failed: Signature counter not incremented for user {UserId}. Possible cloned authenticator!", passkey.UserId);
                return new PasskeyAuthenticationResult { Success = false, Error = "Signaturzähler ungültig" };
            }

            // Verify signature
            var signatureValid = VerifySignature(
                passkey.PublicKey,
                authDataBytes,
                Encoding.UTF8.GetBytes(clientDataJson),
                Base64UrlDecodeToBytes(credential.Response.Signature));

            if (!signatureValid)
            {
                _logger.LogWarning("WebAuthn authentication failed: Invalid signature for user {UserId}", passkey.UserId);
                return new PasskeyAuthenticationResult { Success = false, Error = "Signatur ung\u00fcltig" };
            }

            // Update passkey
            passkey.LastUsedAt = DateTime.UtcNow;
            passkey.SignatureCounter = authData.SignatureCounter;
            passkey.UseCount++;

            challenge.IsUsed = true;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("WebAuthn authentication successful for user {UserId} ({Username})",
                passkey.UserId, passkey.User.Username);

            if (_auditService != null)
            {
                await _auditService.LogAsync("PASSKEY_LOGIN", "User", passkey.UserId,
                    new { PasskeyId = passkey.Id, DeviceName = passkey.DeviceName }, AuditSeverity.Info);
            }

            return new PasskeyAuthenticationResult
            {
                Success = true,
                UserId = passkey.UserId,
                Username = passkey.User.Username,
                User = passkey.User
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebAuthn authentication");
            return new PasskeyAuthenticationResult { Success = false, Error = "Interner Fehler bei der Authentifizierung" };
        }
    }

    public async Task<List<UserPasskey>> GetUserPasskeysAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.UserPasskeys
            .Where(p => p.UserId == userId && p.IsActive)
            .OrderByDescending(p => p.LastUsedAt ?? p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeletePasskeyAsync(int userId, int passkeyId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var passkey = await context.UserPasskeys
            .FirstOrDefaultAsync(p => p.Id == passkeyId && p.UserId == userId, cancellationToken);

        if (passkey == null)
            return false;

        passkey.IsActive = false;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Passkey {PasskeyId} deleted for user {UserId}", passkeyId, userId);

        if (_auditService != null)
        {
            await _auditService.LogAsync("PASSKEY_DELETED", "UserPasskey", passkeyId,
                new { UserId = userId, DeviceName = passkey.DeviceName }, AuditSeverity.Info);
        }

        return true;
    }

    public async Task<bool> RenamePasskeyAsync(int userId, int passkeyId, string newName, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var passkey = await context.UserPasskeys
            .FirstOrDefaultAsync(p => p.Id == passkeyId && p.UserId == userId && p.IsActive, cancellationToken);

        if (passkey == null)
            return false;

        passkey.DeviceName = newName;
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> HasPasskeysAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.UserPasskeys.AnyAsync(p => p.UserId == userId && p.IsActive, cancellationToken);
    }

    public async Task CleanupExpiredChallengesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var expiredChallenges = await context.WebAuthnChallenges
            .Where(c => c.ExpiresAt < DateTime.UtcNow || c.IsUsed)
            .ToListAsync(cancellationToken);

        if (expiredChallenges.Any())
        {
            context.WebAuthnChallenges.RemoveRange(expiredChallenges);
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Cleaned up {Count} expired WebAuthn challenges", expiredChallenges.Count);
        }
    }

    #region Helper Methods

    private string GenerateChallenge()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private string Base64UrlDecode(string input)
    {
        var bytes = Base64UrlDecodeToBytes(input);
        return Encoding.UTF8.GetString(bytes);
    }

    private byte[] Base64UrlDecodeToBytes(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        return Convert.FromBase64String(output);
    }

    private (AuthenticatorDataParsed? authData, byte[]? publicKey, string? aaguid) ParseAttestationObject(byte[] attestationObject)
    {
        try
        {
            // Simplified CBOR parsing for "none" attestation.
            // In production a full CBOR library should be used.
            var cbor = new CborReader(attestationObject);
            var authDataBytes = ExtractAuthDataFromCbor(attestationObject);

            if (authDataBytes == null)
            {
                _logger.LogWarning("Could not extract authData from attestation object");
                return (null, null, null);
            }

            var authData = ParseAuthenticatorData(authDataBytes);

            // Extract public key from attested credential data
            if (authDataBytes.Length > 55 && authData.AttestedCredentialDataIncluded)
            {
                // Skip: rpIdHash (32) + flags (1) + counter (4) + aaguid (16) + credIdLen (2) + credId
                var aaguidBytes = authDataBytes.Skip(37).Take(16).ToArray();
                var aaguid = new Guid(aaguidBytes).ToString();

                var credIdLen = (authDataBytes[53] << 8) | authDataBytes[54];
                var publicKeyStart = 55 + credIdLen;

                if (publicKeyStart < authDataBytes.Length)
                {
                    var publicKey = authDataBytes.Skip(publicKeyStart).ToArray();
                    return (authData, publicKey, aaguid);
                }
            }

            return (authData, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing attestation object");
            return (null, null, null);
        }
    }

    private byte[]? ExtractAuthDataFromCbor(byte[] cborData)
    {
        // Simple CBOR map extraction for authData.
        // Format: {fmt: "none", authData: bytes, attStmt: {}}
        try
        {
            for (int i = 0; i < cborData.Length - 10; i++)
            {
                // Search for "authData" string in CBOR
                if (cborData[i] == 0x68 &&
                    cborData[i + 1] == 'a' &&
                    cborData[i + 2] == 'u' &&
                    cborData[i + 3] == 't' &&
                    cborData[i + 4] == 'h' &&
                    cborData[i + 5] == 'D' &&
                    cborData[i + 6] == 'a' &&
                    cborData[i + 7] == 't' &&
                    cborData[i + 8] == 'a')
                {
                    var dataStart = i + 9;
                    if (dataStart < cborData.Length)
                    {
                        var majorType = cborData[dataStart] >> 5;
                        if (majorType == 2) // Byte string
                        {
                            var additionalInfo = cborData[dataStart] & 0x1F;
                            int length;
                            int dataOffset;

                            if (additionalInfo < 24)
                            {
                                length = additionalInfo;
                                dataOffset = 1;
                            }
                            else if (additionalInfo == 24)
                            {
                                length = cborData[dataStart + 1];
                                dataOffset = 2;
                            }
                            else if (additionalInfo == 25)
                            {
                                length = (cborData[dataStart + 1] << 8) | cborData[dataStart + 2];
                                dataOffset = 3;
                            }
                            else
                            {
                                continue;
                            }

                            if (dataStart + dataOffset + length <= cborData.Length)
                            {
                                return cborData.Skip(dataStart + dataOffset).Take(length).ToArray();
                            }
                        }
                    }
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private AuthenticatorDataParsed ParseAuthenticatorData(byte[] authData)
    {
        if (authData.Length < 37)
        {
            return new AuthenticatorDataParsed();
        }

        var rpIdHash = authData.Take(32).ToArray();
        var flags = authData[32];
        var signatureCounter = (uint)((authData[33] << 24) | (authData[34] << 16) | (authData[35] << 8) | authData[36]);

        return new AuthenticatorDataParsed
        {
            RpIdHash = rpIdHash,
            UserPresent = (flags & 0x01) != 0,
            UserVerified = (flags & 0x04) != 0,
            AttestedCredentialDataIncluded = (flags & 0x40) != 0,
            ExtensionDataIncluded = (flags & 0x80) != 0,
            SignatureCounter = signatureCounter
        };
    }

    private bool VerifySignature(string publicKeyBase64, byte[] authData, byte[] clientDataJson, byte[] signature)
    {
        try
        {
            var publicKeyBytes = Convert.FromBase64String(publicKeyBase64);
            _logger.LogDebug("Public key bytes length: {Length}, first bytes: {Bytes}",
                publicKeyBytes.Length,
                BitConverter.ToString(publicKeyBytes.Take(20).ToArray()));

            var clientDataHash = SHA256.HashData(clientDataJson);

            // Concatenate authData + clientDataHash
            var signedData = new byte[authData.Length + clientDataHash.Length];
            authData.CopyTo(signedData, 0);
            clientDataHash.CopyTo(signedData, authData.Length);

            _logger.LogDebug("Signed data length: {Length}, signature length: {SigLength}",
                signedData.Length, signature.Length);

            var (x, y, algorithm) = ParseCoseKeyImproved(publicKeyBytes);

            if (x == null || y == null)
            {
                _logger.LogWarning("Could not parse COSE key - x or y is null");
                return false;
            }

            _logger.LogDebug("Parsed COSE key: X length={XLen}, Y length={YLen}, Algorithm={Alg}",
                x.Length, y.Length, algorithm);

            using var ecdsa = ECDsa.Create();

            var ecParams = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = x, Y = y }
            };

            ecdsa.ImportParameters(ecParams);

            // Try multiple signature formats

            // 1. Try raw format directly
            bool result = false;
            try
            {
                result = ecdsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256);
                if (result)
                {
                    _logger.LogDebug("Signature verified with raw format");
                    return true;
                }
            }
            catch (CryptographicException ex)
            {
                _logger.LogDebug("Raw signature verification failed: {Error}", ex.Message);
            }

            // 2. Try DER to raw conversion
            if (signature.Length > 64 && signature[0] == 0x30)
            {
                var rawSig = ConvertDerToRaw(signature);
                if (rawSig != null)
                {
                    try
                    {
                        result = ecdsa.VerifyData(signedData, rawSig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                        if (result)
                        {
                            _logger.LogDebug("Signature verified after DER to raw conversion");
                            return true;
                        }
                    }
                    catch (CryptographicException ex)
                    {
                        _logger.LogDebug("DER to raw signature verification failed: {Error}", ex.Message);
                    }
                }
            }

            // 3. Try raw to DER conversion
            if (signature.Length == 64)
            {
                var derSig = ConvertSignatureToDer(signature);
                if (derSig != null)
                {
                    try
                    {
                        result = ecdsa.VerifyData(signedData, derSig, HashAlgorithmName.SHA256);
                        if (result)
                        {
                            _logger.LogDebug("Signature verified after raw to DER conversion");
                            return true;
                        }
                    }
                    catch (CryptographicException ex)
                    {
                        _logger.LogDebug("Raw to DER signature verification failed: {Error}", ex.Message);
                    }
                }
            }

            // 4. Try IeeeP1363 format
            try
            {
                result = ecdsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                if (result)
                {
                    _logger.LogDebug("Signature verified with IeeeP1363 format");
                    return true;
                }
            }
            catch (CryptographicException ex)
            {
                _logger.LogDebug("IeeeP1363 signature verification failed: {Error}", ex.Message);
            }

            _logger.LogWarning("All signature verification attempts failed");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying WebAuthn signature");
            return false;
        }
    }

    private (byte[]? x, byte[]? y, int algorithm) ParseCoseKeyImproved(byte[] coseKey)
    {
        try
        {
            _logger.LogDebug("Parsing COSE key of length {Length}", coseKey.Length);

            // CBOR Map Format for EC2 P-256:
            // A5       # map(5)
            //   01     # unsigned(1) - kty
            //   02     # unsigned(2) - EC2
            //   03     # unsigned(3) - alg
            //   26     # negative(-7) - ES256
            //   20     # negative(-1) - crv
            //   01     # unsigned(1) - P-256
            //   21     # negative(-2) - x
            //   58 20  # bytes(32)
            //     [32 bytes]
            //   22     # negative(-3) - y
            //   58 20  # bytes(32)
            //     [32 bytes]

            byte[]? x = null, y = null;
            int algorithm = -7; // Default ES256

            int i = 0;

            // Skip map header
            if (coseKey.Length > 0)
            {
                var majorType = coseKey[0] >> 5;
                var additionalInfo = coseKey[0] & 0x1F;

                if (majorType == 5) // Map
                {
                    i = 1;
                    if (additionalInfo >= 24)
                    {
                        if (additionalInfo == 24) i = 2;
                        else if (additionalInfo == 25) i = 3;
                    }
                }
            }

            while (i < coseKey.Length - 1)
            {
                var key = coseKey[i];
                i++;

                // Handle CBOR integer keys (positive and negative)
                int keyValue;
                if (key <= 0x17)
                {
                    keyValue = key;
                }
                else if (key >= 0x20 && key <= 0x37)
                {
                    keyValue = -(key - 0x20 + 1);
                }
                else
                {
                    continue;
                }

                if (keyValue == -2) // x coordinate
                {
                    x = ReadCborByteString(coseKey, ref i);
                    _logger.LogDebug("Found x coordinate at position {Pos}, length {Len}", i, x?.Length ?? 0);
                }
                else if (keyValue == -3) // y coordinate
                {
                    y = ReadCborByteString(coseKey, ref i);
                    _logger.LogDebug("Found y coordinate at position {Pos}, length {Len}", i, y?.Length ?? 0);
                }
                else if (keyValue == 3) // algorithm
                {
                    algorithm = ReadCborInteger(coseKey, ref i);
                    _logger.LogDebug("Found algorithm: {Alg}", algorithm);
                }
                else
                {
                    SkipCborValue(coseKey, ref i);
                }
            }

            return (x, y, algorithm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing COSE key");
            return (null, null, -7);
        }
    }

    private byte[]? ReadCborByteString(byte[] data, ref int pos)
    {
        if (pos >= data.Length) return null;

        var header = data[pos];
        var majorType = header >> 5;

        if (majorType != 2) // Not a byte string
        {
            pos++;
            return null;
        }

        var additionalInfo = header & 0x1F;
        int length;

        if (additionalInfo < 24)
        {
            length = additionalInfo;
            pos++;
        }
        else if (additionalInfo == 24)
        {
            length = data[pos + 1];
            pos += 2;
        }
        else if (additionalInfo == 25)
        {
            length = (data[pos + 1] << 8) | data[pos + 2];
            pos += 3;
        }
        else
        {
            return null;
        }

        if (pos + length > data.Length) return null;

        var result = data.Skip(pos).Take(length).ToArray();
        pos += length;
        return result;
    }

    private int ReadCborInteger(byte[] data, ref int pos)
    {
        if (pos >= data.Length) return 0;

        var header = data[pos];
        var majorType = header >> 5;
        var additionalInfo = header & 0x1F;

        int value;
        if (additionalInfo < 24)
        {
            value = additionalInfo;
            pos++;
        }
        else if (additionalInfo == 24)
        {
            value = data[pos + 1];
            pos += 2;
        }
        else
        {
            pos++;
            return 0;
        }

        // Handle negative integers
        if (majorType == 1)
        {
            value = -(value + 1);
        }

        return value;
    }

    private void SkipCborValue(byte[] data, ref int pos)
    {
        if (pos >= data.Length) return;

        var header = data[pos];
        var majorType = header >> 5;
        var additionalInfo = header & 0x1F;

        int length = 0;
        int headerLen = 1;

        if (additionalInfo < 24)
        {
            length = additionalInfo;
        }
        else if (additionalInfo == 24)
        {
            length = data[pos + 1];
            headerLen = 2;
        }
        else if (additionalInfo == 25)
        {
            length = (data[pos + 1] << 8) | data[pos + 2];
            headerLen = 3;
        }

        switch (majorType)
        {
            case 0: // Unsigned integer
            case 1: // Negative integer
                pos += headerLen;
                break;
            case 2: // Byte string
            case 3: // Text string
                pos += headerLen + length;
                break;
            default:
                pos++;
                break;
        }
    }

    private byte[]? ConvertDerToRaw(byte[] derSignature)
    {
        try
        {
            if (derSignature.Length < 8 || derSignature[0] != 0x30)
                return null;

            int pos = 2; // Skip SEQUENCE header

            // Read R
            if (derSignature[pos] != 0x02) return null;
            pos++;
            var rLen = derSignature[pos];
            pos++;
            var r = derSignature.Skip(pos).Take(rLen).ToArray();
            pos += rLen;

            // Pad or trim R to 32 bytes
            if (r.Length > 32)
                r = r.Skip(r.Length - 32).ToArray();
            else if (r.Length < 32)
                r = new byte[32 - r.Length].Concat(r).ToArray();

            // Read S
            if (derSignature[pos] != 0x02) return null;
            pos++;
            var sLen = derSignature[pos];
            pos++;
            var s = derSignature.Skip(pos).Take(sLen).ToArray();

            // Pad or trim S to 32 bytes
            if (s.Length > 32)
                s = s.Skip(s.Length - 32).ToArray();
            else if (s.Length < 32)
                s = new byte[32 - s.Length].Concat(s).ToArray();

            return r.Concat(s).ToArray();
        }
        catch
        {
            return null;
        }
    }

    private byte[]? ConvertSignatureToDer(byte[] signature)
    {
        // WebAuthn signatures can be DER-encoded or raw (r||s)
        if (signature.Length == 64)
        {
            // Raw signature: convert to DER
            var r = signature.Take(32).ToArray();
            var s = signature.Skip(32).Take(32).ToArray();

            // Add leading zero if needed (for positive integers)
            if ((r[0] & 0x80) != 0) r = new byte[] { 0 }.Concat(r).ToArray();
            if ((s[0] & 0x80) != 0) s = new byte[] { 0 }.Concat(s).ToArray();

            var der = new List<byte> { 0x30 }; // SEQUENCE
            var totalLen = 2 + r.Length + 2 + s.Length;
            der.Add((byte)totalLen);

            der.Add(0x02); // INTEGER
            der.Add((byte)r.Length);
            der.AddRange(r);

            der.Add(0x02); // INTEGER
            der.Add((byte)s.Length);
            der.AddRange(s);

            return der.ToArray();
        }

        return signature; // Already DER-encoded
    }

    #endregion
}

#region Helper Classes

/// <summary>
/// Simple CBOR reader (only for WebAuthn parsing purposes).
/// </summary>
internal class CborReader
{
    private readonly byte[] _data;
    private int _position;

    public CborReader(byte[] data)
    {
        _data = data;
        _position = 0;
    }
}

public sealed class AuthenticatorDataParsed
{
    public byte[] RpIdHash { get; set; } = Array.Empty<byte>();
    public bool UserPresent { get; set; }
    public bool UserVerified { get; set; }
    public bool AttestedCredentialDataIncluded { get; set; }
    public bool ExtensionDataIncluded { get; set; }
    public uint SignatureCounter { get; set; }
}

public sealed class PasskeyRegistrationOptions
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Challenge { get; set; } = string.Empty;
    public string RpId { get; set; } = string.Empty;
    public string RpName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;
    public List<PublicKeyCredentialDescriptor> ExcludeCredentials { get; set; } = new();
    public AuthenticatorSelectionCriteria AuthenticatorSelection { get; set; } = new();
    public int Timeout { get; set; } = 300000;
    public string Attestation { get; set; } = "none";
}

public sealed class PasskeyRegistrationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int PasskeyId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
}

public sealed class PasskeyAuthenticationOptions
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Challenge { get; set; } = string.Empty;
    public string RpId { get; set; } = string.Empty;
    public int Timeout { get; set; } = 300000;
    public string UserVerification { get; set; } = "preferred";
    public List<PublicKeyCredentialDescriptor>? AllowCredentials { get; set; }
}

public sealed class PasskeyAuthenticationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public User? User { get; set; }
}

public sealed class PublicKeyCredentialDescriptor
{
    public string Type { get; set; } = "public-key";
    public string Id { get; set; } = string.Empty;
    public string[]? Transports { get; set; }
}

public sealed class AuthenticatorSelectionCriteria
{
    public string? AuthenticatorAttachment { get; set; }
    public string ResidentKey { get; set; } = "preferred";
    public string UserVerification { get; set; } = "preferred";
}

public sealed class WebAuthnRegistrationCredential
{
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public WebAuthnRegistrationResponse Response { get; set; } = new();
    public string? DeviceName { get; set; }
    public string? RegisteredFromIp { get; set; }
    public string? RegisteredUserAgent { get; set; }
}

public sealed class WebAuthnRegistrationResponse
{
    public string ClientDataJSON { get; set; } = string.Empty;
    public string AttestationObject { get; set; } = string.Empty;
    public string[]? Transports { get; set; }
}

public sealed class WebAuthnAuthenticationCredential
{
    public string Id { get; set; } = string.Empty;
    public WebAuthnAuthenticationResponse Response { get; set; } = new();
}

public sealed class WebAuthnAuthenticationResponse
{
    public string ClientDataJSON { get; set; } = string.Empty;
    public string AuthenticatorData { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string? UserHandle { get; set; }
}

public sealed class WebAuthnClientData
{
    public string Type { get; set; } = string.Empty;
    public string Challenge { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public bool? CrossOrigin { get; set; }
}

#endregion
