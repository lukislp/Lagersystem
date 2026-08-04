using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;

namespace LagersystemLVHome.Application.Services;

public sealed class EncryptionService : IEncryptionService
{
    private readonly ILogger<EncryptionService> _logger;
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private byte[]? _encryptionKey;
    private byte[]? _iv;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized = false;

    public EncryptionService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<EncryptionService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the encryption keys from the database or generates new ones.
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var keyRecord = await context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "EncryptionKey", cancellationToken);
            var ivRecord = await context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "EncryptionIV", cancellationToken);

            if (keyRecord != null && ivRecord != null)
            {
                try
                {
                    _encryptionKey = Convert.FromBase64String(keyRecord.Value);
                    _iv = Convert.FromBase64String(ivRecord.Value);
                    _logger.LogInformation("Encryption keys loaded from database");
                }
                catch (FormatException ex)
                {
                    _logger.LogError(ex, "Failed to parse encryption keys from database - generating new keys");
                    await GenerateAndStoreNewKeysAsync(context);
                }
            }
            else
            {
                _logger.LogWarning("No encryption keys found in database - generating new keys");
                await GenerateAndStoreNewKeysAsync(context);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Generates new encryption keys and stores them in the database.
    /// </summary>
    private async Task GenerateAndStoreNewKeysAsync(InventoryDbContext context, CancellationToken cancellationToken = default)
    {
        using var aes = Aes.Create();
        aes.GenerateKey();
        aes.GenerateIV();

        _encryptionKey = aes.Key;
        _iv = aes.IV;

        var keyBase64 = Convert.ToBase64String(_encryptionKey);
        var ivBase64 = Convert.ToBase64String(_iv);

        var keyRecord = await context.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == "EncryptionKey", cancellationToken);

        if (keyRecord == null)
        {
            context.SystemSettings.Add(new Domain.Models.SystemSetting
            {
                Key = "EncryptionKey",
                Value = keyBase64,
                Description = "AES-256 Encryption Key (Auto-generated)",
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            keyRecord.Value = keyBase64;
            keyRecord.UpdatedAt = DateTime.UtcNow;
        }

        var ivRecord = await context.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == "EncryptionIV", cancellationToken);

        if (ivRecord == null)
        {
            context.SystemSettings.Add(new Domain.Models.SystemSetting
            {
                Key = "EncryptionIV",
                Value = ivBase64,
                Description = "AES-256 Initialization Vector (Auto-generated)",
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            ivRecord.Value = ivBase64;
            ivRecord.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("New encryption keys generated and stored in database");
        _logger.LogWarning("Backup your database! Losing these keys means data cannot be decrypted!");
    }

    // Version tag for the current ciphertext format (random per-value IV).
    // Legacy ciphertext (single static IV, no tag/IV prefix) has no such
    // marker byte, which is how Decrypt tells the two formats apart.
    private const byte FormatVersionRandomIv = 0x02;

    /// <summary>
    /// Encrypts a plain text string using AES-256. A fresh random IV is
    /// generated for every call and prepended to the ciphertext (behind a
    /// version tag byte), since reusing a single IV across encryptions
    /// (the pre-existing scheme) leaks plaintext-equality patterns in CBC
    /// mode.
    /// </summary>
    public async Task<string> Encrypt(string plainText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        await EnsureInitializedAsync();

        try
        {
            using var aes = Aes.Create();
            aes.Key = _encryptionKey!;
            aes.GenerateIV();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            ms.WriteByte(FormatVersionRandomIv);
            ms.Write(aes.IV, 0, aes.IV.Length);
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }

            return Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting data");
            throw;
        }
    }

    // Synchronous wrapper for backwards compatibility
    string IEncryptionService.Encrypt(string plainText)
    {
        return Encrypt(plainText).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Decrypts a cipher text string. A leading version-tag byte
    /// (<see cref="FormatVersionRandomIv"/>) identifies the current format
    /// (random per-value IV, stored right after the tag); anything else is
    /// treated as the legacy format (whole payload under the single static
    /// IV stored separately in the database), so old data keeps decrypting
    /// without a separate migration step. The tag makes the two formats
    /// unambiguous - unlike a try-current-then-fall-back-on-error approach,
    /// which can misinterpret legacy multi-block ciphertext as valid
    /// current-format data and silently return the wrong plaintext.
    /// </summary>
    public async Task<string> Decrypt(string cipherText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(cipherText))
            return cipherText;

        await EnsureInitializedAsync();

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(cipherText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting data");
            throw;
        }

        try
        {
            using var aes = Aes.Create();
            aes.Key = _encryptionKey!;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            byte[] payload;
            if (raw.Length > 17 && raw[0] == FormatVersionRandomIv)
            {
                aes.IV = raw[1..17];
                payload = raw[17..];
            }
            else
            {
                aes.IV = _iv!;
                payload = raw;
            }

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(payload);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);

            return sr.ReadToEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting data");
            throw;
        }
    }

    // Synchronous wrapper for backwards compatibility
    string IEncryptionService.Decrypt(string cipherText)
    {
        return Decrypt(cipherText).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Hashes a password using BCrypt.
    /// </summary>
    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    /// <summary>
    /// Verifies a password against a BCrypt hash.
    /// </summary>
    public bool VerifyPassword(string password, string hashedPassword)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
        }
        catch
        {
            return false;
        }
    }

    public string GenerateRandomKey(int length = 32)
    {
        using var rng = RandomNumberGenerator.Create();
        var key = new byte[length];
        rng.GetBytes(key);
        return Convert.ToBase64String(key);
    }

    public async Task<(string Key, string IV)> GetOrCreateEncryptionKeysAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        return (
            Convert.ToBase64String(_encryptionKey!),
            Convert.ToBase64String(_iv!)
        );
    }

    /// <summary>
    /// Checks whether encryption keys are configured in the database.
    /// </summary>
    public async Task<bool> HasKeysConfiguredAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var hasKey = await context.SystemSettings.AnyAsync(s => s.Key == "EncryptionKey", cancellationToken);
        var hasIV = await context.SystemSettings.AnyAsync(s => s.Key == "EncryptionIV", cancellationToken);

        return hasKey && hasIV;
    }
}
