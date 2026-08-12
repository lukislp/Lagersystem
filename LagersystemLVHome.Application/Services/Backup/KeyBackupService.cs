using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO.Compression;

namespace LagersystemLVHome.Application.Services;

public sealed class KeyBackupService : IKeyBackupService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<KeyBackupService> _logger;
    private readonly ISecureConfigurationService _secureConfig;

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetAddConnection2(ref NETRESOURCE lpNetResource, string lpPassword, string lpUsername, int dwFlags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetCancelConnection2(string lpName, int dwFlags, bool fForce);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    private const int RESOURCETYPE_DISK = 0x1;

    public KeyBackupService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<KeyBackupService> logger,
        ISecureConfigurationService secureConfig)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _secureConfig = secureConfig;
    }

    public async Task<Domain.Models.KeyBackupSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var settings = await context.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == "KeyBackupSettings", cancellationToken);

        if (settings == null)
        {
            return new Domain.Models.KeyBackupSettings
            {
                Enabled = false,
                BackupHour = 3,
                BackupProviderId = null,
                RetentionDays = 90,
                RequirePassword = false
            };
        }

        return JsonSerializer.Deserialize<Domain.Models.KeyBackupSettings>(settings.Value)
            ?? new Domain.Models.KeyBackupSettings();
    }

    public async Task UpdateSettingsAsync(Domain.Models.KeyBackupSettings settings, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var systemSetting = await context.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == "KeyBackupSettings", cancellationToken);

        var json = JsonSerializer.Serialize(settings);

        if (systemSetting == null)
        {
            systemSetting = new SystemSetting
            {
                Key = "KeyBackupSettings",
                Value = json,
                Description = "Einstellungen f\u00fcr automatisches Schl\u00fcssel-Backup"
            };
            context.SystemSettings.Add(systemSetting);
        }
        else
        {
            systemSetting.Value = json;
            systemSetting.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<KeyBackupResult> CreateKeyBackupAsync(CancellationToken cancellationToken = default)
    {
        var result = new KeyBackupResult();
        var settings = await GetSettingsAsync();

        if (!settings.Enabled || !settings.BackupProviderId.HasValue)
        {
            result.Success = false;
            result.ErrorMessage = "Key-Backup ist deaktiviert oder kein Provider konfiguriert";
            return result;
        }

        bool wasConnected = false;
        string? networkSharePath = null;

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Load backup provider
            var backupProvider = await context.BackupProviders
                .FirstOrDefaultAsync(p => p.Id == settings.BackupProviderId.Value, cancellationToken);

            if (backupProvider == null)
            {
                result.Success = false;
                result.ErrorMessage = "Backup-Provider nicht gefunden";
                return result;
            }

            // Determine keys directory path
            var keysDirectory = Path.Combine(Directory.GetCurrentDirectory(), "keys");

            if (!Directory.Exists(keysDirectory))
            {
                result.Success = false;
                result.ErrorMessage = "Keys-Directory nicht gefunden";
                return result;
            }

            // Create backup file
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = $"encryption_keys_backup_{timestamp}.zip";

            // Determine storage path based on provider type
            string backupFilePath;
            if (backupProvider.Type == BackupProviderType.Local)
            {
                var decryptedConfig = _secureConfig.Decrypt(backupProvider.Configuration);
                var localConfig = JsonSerializer.Deserialize<LocalBackupConfig>(decryptedConfig);
                var basePath = localConfig?.Paths.FirstOrDefault() ?? Path.GetTempPath();
                var keyBackupDir = Path.Combine(basePath, "KeyBackups");
                Directory.CreateDirectory(keyBackupDir);
                backupFilePath = Path.Combine(keyBackupDir, fileName);
            }
            else if (backupProvider.Type == BackupProviderType.NetworkShare)
            {
                var decryptedConfig = _secureConfig.Decrypt(backupProvider.Configuration);
                var networkConfig = JsonSerializer.Deserialize<NetworkShareConfig>(decryptedConfig);
                var basePath = networkConfig?.Paths.FirstOrDefault()?.UncPath ?? Path.GetTempPath();

                // Authenticate to network share
                if (!string.IsNullOrEmpty(networkConfig?.Username) && !string.IsNullOrEmpty(networkConfig?.Password))
                {
                    wasConnected = ConnectToNetworkShare(basePath, networkConfig.Username, networkConfig.Password);
                    networkSharePath = basePath;

                    if (!wasConnected)
                    {
                        _logger.LogWarning("Failed to authenticate to network share: {Path} (trying anyway)", basePath);
                    }
                    else
                    {
                        _logger.LogInformation("Successfully authenticated to network share: {Path}", basePath);
                    }
                }

                var keyBackupDir = Path.Combine(basePath, "KeyBackups");
                Directory.CreateDirectory(keyBackupDir);
                backupFilePath = Path.Combine(keyBackupDir, fileName);
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = "Nur Local Storage und Network Share Provider sind f\u00fcr Key-Backups erlaubt";
                return result;
            }

            // Create ZIP archive with all key files
            using (var archive = ZipFile.Open(backupFilePath, ZipArchiveMode.Create))
            {
                foreach (var keyFile in Directory.GetFiles(keysDirectory, "key-*.xml"))
                {
                    var entryName = Path.GetFileName(keyFile);
                    archive.CreateEntryFromFile(keyFile, entryName, CompressionLevel.Optimal);
                    _logger.LogInformation("Added key file to backup: {FileName}", entryName);
                }
            }

            // Optional: additional encryption with user password
            if (settings.RequirePassword && !string.IsNullOrEmpty(settings.BackupPassword))
            {
                var encryptedPath = backupFilePath + ".enc";
                await EncryptFileWithPasswordAsync(backupFilePath, encryptedPath, settings.BackupPassword);

                File.Delete(backupFilePath);
                backupFilePath = encryptedPath;
                fileName += ".enc";

                _logger.LogInformation("Backup encrypted with user password");
            }

            // Create history entry
            var keyFiles = Directory.GetFiles(keysDirectory, "key-*.xml");
            var history = new Domain.Models.KeyBackupHistory
            {
                BackupDate = DateTime.UtcNow,
                FileName = fileName,
                FilePath = backupFilePath,
                BackupProviderId = backupProvider.Id,
                ProviderCount = keyFiles.Length,
                SizeBytes = new FileInfo(backupFilePath).Length,
                IsEncrypted = settings.RequirePassword,
                Status = BackupStatus.Success
            };

            context.KeyBackupHistory.Add(history);
            await context.SaveChangesAsync(cancellationToken);

            await CleanupOldBackupsAsync(settings.RetentionDays);

            result.Success = true;
            result.FileName = fileName;
            _logger.LogInformation("Encryption keys backed up: {Count} key files in {FileName}", keyFiles.Length, fileName);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Key backup failed");
        }
        finally
        {
            // Disconnect after backup
            if (wasConnected && !string.IsNullOrEmpty(networkSharePath))
            {
                DisconnectFromNetworkShare(networkSharePath);
            }
        }

        return result;
    }

    public async Task<bool> RestoreKeysFromBackupAsync(int historyId, string? password, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var history = await context.KeyBackupHistory
                .Include(h => h.BackupProvider)
                .FirstOrDefaultAsync(h => h.Id == historyId, cancellationToken);

            if (history == null || !File.Exists(history.FilePath))
            {
                _logger.LogWarning("Backup file not found: {HistoryId}", historyId);
                return false;
            }

            bool wasConnected = false;
            string? networkSharePath = null;

            try
            {
                // Authenticate for network share restore
                if (history.BackupProvider.Type == BackupProviderType.NetworkShare)
                {
                    var decryptedConfig = _secureConfig.Decrypt(history.BackupProvider.Configuration);
                    var networkConfig = JsonSerializer.Deserialize<NetworkShareConfig>(decryptedConfig);

                    if (!string.IsNullOrEmpty(networkConfig?.Username) && !string.IsNullOrEmpty(networkConfig?.Password))
                    {
                        var basePath = networkConfig.Paths.FirstOrDefault()?.UncPath;
                        if (!string.IsNullOrEmpty(basePath))
                        {
                            wasConnected = ConnectToNetworkShare(basePath, networkConfig.Username, networkConfig.Password);
                            networkSharePath = basePath;
                        }
                    }
                }

                var keysDirectory = Path.Combine(Directory.GetCurrentDirectory(), "keys");
                var tempFile = history.FilePath;

                // Decrypt if necessary
                if (history.IsEncrypted)
                {
                    if (string.IsNullOrEmpty(password))
                    {
                        _logger.LogWarning("Password required for encrypted backup");
                        return false;
                    }

                    var decryptedFile = Path.Combine(Path.GetTempPath(), $"decrypted_{Guid.NewGuid()}.zip");
                    await DecryptFileWithPasswordAsync(history.FilePath, decryptedFile, password);
                    tempFile = decryptedFile;
                }

                // Create safety backup of current keys directory
                var safetyBackup = Path.Combine(Path.GetTempPath(), $"keys_safety_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip");
                if (Directory.Exists(keysDirectory))
                {
                    ZipFile.CreateFromDirectory(keysDirectory, safetyBackup);
                    _logger.LogInformation("Safety backup created: {Path}", safetyBackup);
                }

                // Extract ZIP to keys directory
                ZipFile.ExtractToDirectory(tempFile, keysDirectory, overwriteFiles: true);

                if (history.IsEncrypted && File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }

                _logger.LogInformation("Encryption keys restored from backup: {HistoryId}", historyId);
                _logger.LogWarning("Application restart required for new keys to take effect");
                return true;
            }
            finally
            {
                if (wasConnected && !string.IsNullOrEmpty(networkSharePath))
                {
                    DisconnectFromNetworkShare(networkSharePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Key restore failed: {HistoryId}", historyId);
            return false;
        }
    }

    private bool ConnectToNetworkShare(string uncPath, string username, string password)
    {
        try
        {
            var parts = uncPath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                _logger.LogError("Invalid UNC path format: {Path}", uncPath);
                return false;
            }

            var serverShare = $"\\\\{parts[0]}\\{parts[1]}";

            var netResource = new NETRESOURCE
            {
                dwType = RESOURCETYPE_DISK,
                lpRemoteName = serverShare
            };

            int result = WNetAddConnection2(ref netResource, password, username, 0);

            if (result == 0)
            {
                _logger.LogInformation("Connected to network share: {Path} as {Username}", serverShare, username);
                return true;
            }
            else
            {
                var errorMessage = Marshal.GetLastWin32Error();
                _logger.LogError("Failed to connect to network share: {Path} as {Username}. Win32 Error: {Error}",
                    serverShare, username, errorMessage);

                if (errorMessage == 1219)
                {
                    _logger.LogWarning("Already connected with different credentials. Trying to use existing connection");
                    return true;
                }

                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception connecting to network share: {Path}", uncPath);
            return false;
        }
    }

    private void DisconnectFromNetworkShare(string uncPath)
    {
        try
        {
            var parts = uncPath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var serverShare = $"\\\\{parts[0]}\\{parts[1]}";
                int result = WNetCancelConnection2(serverShare, 0, true);

                if (result == 0)
                {
                    _logger.LogDebug("Disconnected from network share: {Path}", serverShare);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to disconnect from network share: {Path}", uncPath);
        }
    }

    private async Task EncryptFileWithPasswordAsync(string inputFile, string outputFile, string password, CancellationToken cancellationToken = default)
    {
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.KeySize = 256;

        using var deriveBytes = new System.Security.Cryptography.Rfc2898DeriveBytes(
            password,
            new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 },
            10000,
            System.Security.Cryptography.HashAlgorithmName.SHA256);

        aes.Key = deriveBytes.GetBytes(32);
        aes.IV = deriveBytes.GetBytes(16);

        using var inputStream = File.OpenRead(inputFile);
        using var outputStream = File.Create(outputFile);
        using var cryptoStream = new System.Security.Cryptography.CryptoStream(
            outputStream,
            aes.CreateEncryptor(),
            System.Security.Cryptography.CryptoStreamMode.Write);

        await inputStream.CopyToAsync(cryptoStream);
    }

    private async Task DecryptFileWithPasswordAsync(string inputFile, string outputFile, string password, CancellationToken cancellationToken = default)
    {
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.KeySize = 256;

        using var deriveBytes = new System.Security.Cryptography.Rfc2898DeriveBytes(
            password,
            new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 },
            10000,
            System.Security.Cryptography.HashAlgorithmName.SHA256);

        aes.Key = deriveBytes.GetBytes(32);
        aes.IV = deriveBytes.GetBytes(16);

        using var inputStream = File.OpenRead(inputFile);
        using var outputStream = File.Create(outputFile);
        using var cryptoStream = new System.Security.Cryptography.CryptoStream(
            inputStream,
            aes.CreateDecryptor(),
            System.Security.Cryptography.CryptoStreamMode.Read);

        await cryptoStream.CopyToAsync(outputStream);
    }

    public async Task<List<Domain.Models.KeyBackupHistory>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.KeyBackupHistory
            .Include(h => h.BackupProvider)
            .OrderByDescending(h => h.BackupDate)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteKeyBackupAsync(int historyId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var history = await context.KeyBackupHistory
            .Include(h => h.BackupProvider)
            .FirstOrDefaultAsync(h => h.Id == historyId, cancellationToken);

        if (history == null)
        {
            return;
        }

        bool wasConnected = false;
        string? networkSharePath = null;

        try
        {
            // Authenticate for network share delete
            if (history.BackupProvider.Type == BackupProviderType.NetworkShare)
            {
                var decryptedConfig = _secureConfig.Decrypt(history.BackupProvider.Configuration);
                var networkConfig = JsonSerializer.Deserialize<NetworkShareConfig>(decryptedConfig);

                if (!string.IsNullOrEmpty(networkConfig?.Username) && !string.IsNullOrEmpty(networkConfig?.Password))
                {
                    var basePath = networkConfig.Paths.FirstOrDefault()?.UncPath;
                    if (!string.IsNullOrEmpty(basePath))
                    {
                        wasConnected = ConnectToNetworkShare(basePath, networkConfig.Username, networkConfig.Password);
                        networkSharePath = basePath;
                    }
                }
            }

            // Delete file
            if (File.Exists(history.FilePath))
            {
                File.Delete(history.FilePath);
            }

            // Delete database entry
            context.KeyBackupHistory.Remove(history);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Key backup deleted: {HistoryId}", historyId);
        }
        finally
        {
            if (wasConnected && !string.IsNullOrEmpty(networkSharePath))
            {
                DisconnectFromNetworkShare(networkSharePath);
            }
        }
    }

    public async Task<List<BackupProvider>> GetAvailableLocalProvidersAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var providers = await context.BackupProviders
            .Where(p => p.Enabled &&
                (p.Type == BackupProviderType.Local || p.Type == BackupProviderType.NetworkShare))
            .ToListAsync(cancellationToken);

        foreach (var provider in providers)
        {
            if (!string.IsNullOrEmpty(provider.Configuration))
            {
                try
                {
                    provider.Configuration = _secureConfig.Decrypt(provider.Configuration);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to decrypt configuration for provider {Provider}", provider.Name);
                }
            }
        }

        return providers;
    }

    private async Task CleanupOldBackupsAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

        var oldBackups = await context.KeyBackupHistory
            .Include(h => h.BackupProvider)
            .Where(h => h.BackupDate < cutoffDate)
            .ToListAsync(cancellationToken);

        foreach (var backup in oldBackups)
        {
            bool wasConnected = false;
            string? networkSharePath = null;

            try
            {
                // Authenticate for cleanup
                if (backup.BackupProvider.Type == BackupProviderType.NetworkShare)
                {
                    var decryptedConfig = _secureConfig.Decrypt(backup.BackupProvider.Configuration);
                    var networkConfig = JsonSerializer.Deserialize<NetworkShareConfig>(decryptedConfig);

                    if (!string.IsNullOrEmpty(networkConfig?.Username) && !string.IsNullOrEmpty(networkConfig?.Password))
                    {
                        var basePath = networkConfig.Paths.FirstOrDefault()?.UncPath;
                        if (!string.IsNullOrEmpty(basePath))
                        {
                            wasConnected = ConnectToNetworkShare(basePath, networkConfig.Username, networkConfig.Password);
                            networkSharePath = basePath;
                        }
                    }
                }

                if (File.Exists(backup.FilePath))
                {
                    File.Delete(backup.FilePath);
                }

                context.KeyBackupHistory.Remove(backup);
            }
            finally
            {
                if (wasConnected && !string.IsNullOrEmpty(networkSharePath))
                {
                    DisconnectFromNetworkShare(networkSharePath);
                }
            }
        }

        if (oldBackups.Any())
        {
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("{Count} old key backups deleted", oldBackups.Count);
        }
    }
}

/// <summary>
/// Result of a key backup operation.
/// </summary>
public sealed class KeyBackupResult
{
    public bool Success { get; set; }
    public string? FileName { get; set; }
    public string? ErrorMessage { get; set; }
}
