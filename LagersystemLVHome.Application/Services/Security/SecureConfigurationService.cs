using Microsoft.AspNetCore.DataProtection;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Secure encryption of backup provider secrets
/// using ASP.NET Core Data Protection API with AES-256.
/// </summary>
public sealed class SecureConfigurationService : ISecureConfigurationService
{
    private readonly IDataProtector _protector;
    private readonly ILogger<SecureConfigurationService> _logger;

    // Data Protection format prefix
    private const string DP_PREFIX = "CfDJ8";

    public SecureConfigurationService(
        IDataProtectionProvider provider,
        ILogger<SecureConfigurationService> logger)
    {
        // Purpose string for key isolation; different purpose = different keys
        _protector = provider.CreateProtector("BackupProvider.Configuration.v1");
        _logger = logger;
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            _logger.LogWarning("Attempted to encrypt empty string");
            return plaintext;
        }

        try
        {
            var encrypted = _protector.Protect(plaintext);
            _logger.LogDebug("Configuration encrypted successfully (length: {Length})", encrypted.Length);
            return encrypted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt configuration");
            throw new InvalidOperationException("Encryption failed. Check Data Protection setup.", ex);
        }
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            _logger.LogWarning("Attempted to decrypt empty string");
            return ciphertext;
        }

        // If not encrypted, return as-is (for migration scenarios)
        if (!IsEncrypted(ciphertext))
        {
            _logger.LogWarning("Configuration is not encrypted, returning as-is");
            return ciphertext;
        }

        try
        {
            var decrypted = _protector.Unprotect(ciphertext);
            _logger.LogDebug("Configuration decrypted successfully");
            return decrypted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt configuration");
            throw new InvalidOperationException(
                "Decryption failed. This could be due to:\n" +
                "1. Missing or corrupted encryption keys in 'keys/' folder\n" +
                "2. Configuration encrypted with different keys\n" +
                "3. Corrupted data", ex);
        }
    }

    public bool IsEncrypted(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Data Protection API uses Base64 with a specific prefix
        return value.StartsWith(DP_PREFIX);
    }
}
