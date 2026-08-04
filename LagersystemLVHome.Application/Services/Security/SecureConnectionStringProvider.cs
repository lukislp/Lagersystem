using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LagersystemLVHome.Application.Services;

public sealed class SecureConnectionStringProvider : ISecureConnectionStringProvider
{
    private readonly ILogger<SecureConnectionStringProvider> _logger;
    private readonly string _passDirectory;
    private readonly string _passwordFile;
    private readonly string _keyFile;
    private string? _cachedPassword;

    public SecureConnectionStringProvider(
        IWebHostEnvironment environment,
        ILogger<SecureConnectionStringProvider> logger)
    {
        _logger = logger;
        _passDirectory = Path.Combine(environment.ContentRootPath, "Pass");
        _passwordFile = Path.Combine(_passDirectory, "db.password.enc");
        _keyFile = Path.Combine(_passDirectory, "encryption.key");
    }

    public bool HasSecureSecrets()
    {
        return File.Exists(_passwordFile) && File.Exists(_keyFile);
    }

    /// <summary>
    /// Replaces placeholder in the connection string with the decrypted password.
    /// </summary>
    public string GetSecureConnectionString(string connectionStringTemplate)
    {
        if (!HasSecureSecrets())
        {
            _logger.LogWarning("No encrypted secrets found in the Pass/ directory");
            _logger.LogInformation("Run 'setup-database-password.ps1' to encrypt the password");
            return connectionStringTemplate;
        }

        if (_cachedPassword == null)
        {
            _cachedPassword = DecryptPassword();
        }

        var secureConnectionString = ReplacePasswordInConnectionString(
            connectionStringTemplate,
            _cachedPassword
        );

        _logger.LogInformation("Connection string loaded with encrypted password (from Pass/)");
        return secureConnectionString;
    }

    /// <summary>
    /// Decrypts the password using AES-256.
    /// </summary>
    private string DecryptPassword()
    {
        try
        {
            _logger.LogInformation("Loading encrypted database password from Pass/...");

            // 1. Load encryption key
            var keyBase64 = File.ReadAllText(_keyFile).Trim();
            var key = Convert.FromBase64String(keyBase64);

            if (key.Length != 32)
            {
                throw new InvalidOperationException(
                    $"Invalid encryption key (expected: 32 bytes, found: {key.Length} bytes)");
            }

            // 2. Load encrypted password
            var encryptedBase64 = File.ReadAllText(_passwordFile).Trim();
            var encryptedData = Convert.FromBase64String(encryptedBase64);

            // 3. Extract IV (first 16 bytes) and encrypted data
            if (encryptedData.Length < 16)
            {
                throw new InvalidOperationException(
                    "Encrypted data is too short (no IV present)");
            }

            var iv = new byte[16];
            Array.Copy(encryptedData, 0, iv, 0, 16);

            var cipherText = new byte[encryptedData.Length - 16];
            Array.Copy(encryptedData, 16, cipherText, 0, cipherText.Length);

            // 4. Decrypt with AES-256
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
            var password = Encoding.UTF8.GetString(decryptedBytes);

            _logger.LogInformation("Password decrypted successfully");

            return password;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting the database password");
            _logger.LogError("Make sure that:");
            _logger.LogError("  1. setup-database-password.ps1 has been run");
            _logger.LogError("  2. Pass/db.password.enc exists");
            _logger.LogError("  3. Pass/encryption.key exists");
            _logger.LogError("  4. The files are not corrupted");
            throw new InvalidOperationException(
                "Unable to decrypt the database password", ex);
        }
    }

    /// <summary>
    /// Replaces the password in the connection string (supports PostgreSQL, MySQL, SQL Server).
    /// </summary>
    private string ReplacePasswordInConnectionString(string connectionString, string password)
    {
        // PostgreSQL: Password=...
        if (connectionString.Contains("Password=", StringComparison.OrdinalIgnoreCase))
        {
            var parts = connectionString.Split(';');
            var updatedParts = parts.Select(part =>
            {
                if (part.Trim().StartsWith("Password=", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Password={password}";
                }
                return part;
            });
            return string.Join(";", updatedParts);
        }

        // MySQL: Password=... or Pwd=...
        if (connectionString.Contains("Pwd=", StringComparison.OrdinalIgnoreCase))
        {
            var parts = connectionString.Split(';');
            var updatedParts = parts.Select(part =>
            {
                if (part.Trim().StartsWith("Pwd=", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Pwd={password}";
                }
                return part;
            });
            return string.Join(";", updatedParts);
        }

        // No password field found; append one
        _logger.LogWarning("No Password field found in the connection string. Appending one...");
        return $"{connectionString};Password={password}";
    }
}
