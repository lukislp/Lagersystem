using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Database restore service, fully separated from the backup system.
/// </summary>
public sealed class DatabaseRestoreService : IDatabaseRestoreService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<DatabaseRestoreService> _logger;
    private readonly DatabaseSettings _databaseSettings;
    private readonly IEncryptionService _encryptionService;
    private readonly IBackupManagementService _backupService;
    private readonly IWebHostEnvironment _environment;
    private readonly IDatabaseProviderService _databaseProviderService;
    private readonly JsonBackupHelper _jsonBackupHelper;

    public DatabaseRestoreService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<DatabaseRestoreService> logger,
        IOptions<DatabaseSettings> databaseSettings,
        IEncryptionService encryptionService,
        IBackupManagementService backupService,
        IWebHostEnvironment environment,
        IDatabaseProviderService databaseProviderService,
        JsonBackupHelper jsonBackupHelper)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _databaseSettings = databaseSettings.Value;
        _encryptionService = encryptionService;
        _backupService = backupService;
        _environment = environment;
        _databaseProviderService = databaseProviderService;
        _jsonBackupHelper = jsonBackupHelper;
    }

    // Validation

    public async Task<RestoreValidationResult> ValidateBackupAsync(Stream backupStream, CancellationToken cancellationToken = default)
    {
        var result = new RestoreValidationResult();

        try
        {
            // BrowserFileStream does not support setting Position;
            // copy to MemoryStream for full stream support.
            MemoryStream memoryStream;

            if (backupStream.CanSeek)
            {
                backupStream.Position = 0;
                memoryStream = null!;
            }
            else
            {
                memoryStream = new MemoryStream();
                await backupStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                backupStream = memoryStream;
            }

            try
            {
                // 1. Verify ZIP structure
                if (!await IsValidZipAsync(backupStream))
                {
                    // Not a ZIP - this is exactly the shape BackupManagementService.EncryptBackupAsync
                    // produces (a 16-byte IV followed by opaque AES ciphertext; the plaintext ZIP only
                    // exists again after decryption). Treat it as an encrypted-backup candidate instead
                    // of rejecting outright; a wrong password or genuinely corrupt upload still surfaces
                    // as a clear error once decryption is attempted.
                    backupStream.Position = 0;
                    if (backupStream.Length <= 16)
                    {
                        result.ErrorMessage = "Keine gueltige ZIP-Datei";
                        return result;
                    }

                    result.IsEncrypted = true;
                    result.RequiresPassword = true;
                    result.IsValid = true;
                    return result;
                }

                // 2. Check encryption
                backupStream.Position = 0;
                result.IsEncrypted = await IsBackupEncryptedAsync(backupStream);
                result.RequiresPassword = result.IsEncrypted;

                // 3. Read metadata (if present)
                backupStream.Position = 0;
                result.Metadata = await TryReadMetadataAsync(backupStream);

                result.IsValid = true;
                return result;
            }
            finally
            {
                memoryStream?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed");
            result.ErrorMessage = $"Validierung fehlgeschlagen: {ex.Message}";
            return result;
        }
    }

    public async Task<bool> IsBackupEncryptedAsync(Stream backupStream, CancellationToken cancellationToken = default)
    {
        try
        {
            MemoryStream? memoryStream = null;

            if (!backupStream.CanSeek)
            {
                memoryStream = new MemoryStream();
                await backupStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                backupStream = memoryStream;
            }
            else
            {
                backupStream.Position = 0;
            }

            try
            {
                using var archive = new ZipArchive(backupStream, ZipArchiveMode.Read, true);

                // 1. Check for .encrypted marker (from BackupManagementService)
                var encryptedMarker = archive.GetEntry(".encrypted");
                if (encryptedMarker != null)
                {
                    return true;
                }

                // 2. Check for metadata.json (normal backups have this - see JsonBackupHelper)
                var metadataEntry = archive.GetEntry("metadata.json");
                if (metadataEntry != null)
                {
                    return false;
                }

                // 3. Check first entry
                var firstEntry = archive.Entries.FirstOrDefault();
                if (firstEntry == null)
                {
                    return false;
                }

                // 4. Check for known database file extensions
                var dbExtensions = new[] { ".db", ".sqlite", ".sql", ".backup" };
                if (dbExtensions.Any(ext => firstEntry.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                // 5. Read first bytes to determine content type
                try
                {
                    using var entryStream = firstEntry.Open();
                    var buffer = new byte[512];
                    var bytesRead = await entryStream.ReadAsync(buffer);

                    if (bytesRead == 0)
                    {
                        return false;
                    }

                    // SQLite magic header (53 51 4C 69 74 65 20 66 6F 72 6D 61 74)
                    if (buffer[0] == 0x53 && buffer[1] == 0x51 && buffer[2] == 0x4C)
                    {
                        return false;
                    }

                    // PostgreSQL dump header (PGDMP)
                    if (buffer[0] == 0x50 && buffer[1] == 0x47 && buffer[2] == 0x44)
                    {
                        return false;
                    }

                    // JSON content
                    var text = Encoding.UTF8.GetString(buffer, 0, Math.Min(100, bytesRead));
                    if (text.TrimStart().StartsWith("{") || text.TrimStart().StartsWith("["))
                    {
                        return false;
                    }

                    // Heuristic: if > 90% non-printable characters, treat as encrypted
                    int nonPrintable = 0;
                    for (int i = 0; i < Math.Min(256, bytesRead); i++)
                    {
                        if (buffer[i] < 32 && buffer[i] != 0x0A && buffer[i] != 0x0D && buffer[i] != 0x09)
                        {
                            nonPrintable++;
                        }
                    }

                    return (nonPrintable / (double)Math.Min(256, bytesRead)) > 0.9;
                }
                catch
                {
                    return false;
                }
            }
            finally
            {
                memoryStream?.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }

    // Restore

    public async Task<RestoreResult> RestoreFromBackupAsync(
        Stream backupStream,
        string? password = null,
        IProgress<RestoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new RestoreResult();
        var stopwatch = Stopwatch.StartNew();

        // Convert BrowserFileStream to MemoryStream
        MemoryStream? memoryStream = null;

        if (!backupStream.CanSeek)
        {
            memoryStream = new MemoryStream();
            await backupStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            backupStream = memoryStream;
        }

        try
        {
            ReportProgress(progress, 5, "Validiere Backup...", RestoreStep.Validating);

            var validation = await ValidateBackupAsync(backupStream);
            if (!validation.IsValid)
            {
                result.ErrorMessage = validation.ErrorMessage;
                return result;
            }

            if (validation.IsEncrypted && string.IsNullOrEmpty(password))
            {
                result.ErrorMessage = "Backup ist verschluesselt, aber kein Passwort angegeben";
                return result;
            }

            ReportProgress(progress, 15, "Erstelle Sicherheits-Backup...", RestoreStep.CreatingSafetyBackup);

            result.SafetyBackupPath = await CreateSafetyBackupAsync();
            _logger.LogInformation("Safety backup created: {Path}", result.SafetyBackupPath);

            ReportProgress(progress, 30, "Entpacke Backup...", RestoreStep.Extracting);

            var tempDir = Path.Combine(Path.GetTempPath(), $"restore_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                backupStream.Position = 0;

                if (validation.IsEncrypted && !string.IsNullOrEmpty(password))
                {
                    ReportProgress(progress, 50, "Entschluessele Backup...", RestoreStep.Decrypting);
                    await DecryptAndExtractAsync(backupStream, tempDir, password);
                }
                else
                {
                    using var archive = new ZipArchive(backupStream, ZipArchiveMode.Read);
                    archive.ExtractToDirectory(tempDir);
                }

                ReportProgress(progress, 70, "Ersetze Datenbank...", RestoreStep.ReplacingDatabase);
                await ReplaceDatabaseAsync(tempDir, cancellationToken);

                ReportProgress(progress, 85, "Initialisiere Datenbank...", RestoreStep.Reinitializing);
                await ReInitializeDatabaseConnectionAsync();

                ReportProgress(progress, 95, "Validiere Wiederherstellung...", RestoreStep.ValidatingRestore);

                result.TablesRestored = await CountTablesAsync();
                result.RecordsRestored = await CountRecordsAsync();
                result.Success = true;

                ReportProgress(progress, 100, "Fertig!", RestoreStep.Complete);

                _logger.LogInformation(
                    "Restore completed: {Tables} tables, {Records} records in {Duration}",
                    result.TablesRestored,
                    result.RecordsRestored,
                    stopwatch.Elapsed);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not delete temp directory: {Path}", tempDir);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore failed");
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            memoryStream?.Dispose();
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
        }

        return result;
    }

    // Info

    public async Task<DatabaseInfo> GetCurrentDatabaseInfoAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var info = new DatabaseInfo
        {
            Provider = _databaseProviderService.Provider.ToString(),
            ProductCount = await context.Products.CountAsync(cancellationToken),
            CategoryCount = await context.Categories.CountAsync(cancellationToken),
            UserCount = await context.Users.CountAsync(cancellationToken),
            WarehouseCount = await context.Warehouses.CountAsync(cancellationToken),
            LastModified = DateTime.UtcNow
        };

        if (_databaseProviderService.Provider == DatabaseProvider.SQLite)
        {
            var dbPath = GetDatabasePath();
            if (File.Exists(dbPath))
            {
                info.SizeBytes = new FileInfo(dbPath).Length;
            }
        }

        return info;
    }

    public async Task<RestoreBackupInfo> GetBackupInfoAsync(Stream backupStream, string? password = null, CancellationToken cancellationToken = default)
    {
        MemoryStream? memoryStream = null;

        if (!backupStream.CanSeek)
        {
            memoryStream = new MemoryStream();
            await backupStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            backupStream = memoryStream;
        }

        try
        {
            var info = new RestoreBackupInfo
            {
                SizeBytes = backupStream.Length,
                IsEncrypted = await IsBackupEncryptedAsync(backupStream)
            };

            try
            {
                backupStream.Position = 0;
                var metadata = await TryReadMetadataAsync(backupStream);

                if (metadata != null)
                {
                    info.CreatedAt = metadata.BackupDate;
                    info.Provider = metadata.DatabaseProvider;
                    info.ProductCount = metadata.TableCounts.GetValueOrDefault("Products", 0);
                    info.CategoryCount = metadata.TableCounts.GetValueOrDefault("Categories", 0);
                    info.UserCount = metadata.TableCounts.GetValueOrDefault("Users", 0);
                    info.WarehouseCount = metadata.TableCounts.GetValueOrDefault("Warehouses", 0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read backup metadata");
            }

            return info;
        }
        finally
        {
            memoryStream?.Dispose();
        }
    }

    // Safety

    public async Task<string> CreateSafetyBackupAsync(CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var safetyDir = Path.Combine(_environment.WebRootPath, "backups", "safety");
        Directory.CreateDirectory(safetyDir);

        var safetyName = $"pre_restore_{timestamp}";

        await _backupService.CreateBackupAsync();

        return Path.Combine(safetyDir, $"{safetyName}.zip");
    }

    private string GenerateRandomPassword()
    {
        return Guid.NewGuid().ToString("N");
    }

    // Private helpers

    private async Task<bool> IsValidZipAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
            return archive.Entries.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<BackupMetadata?> TryReadMetadataAsync(Stream backupStream, CancellationToken cancellationToken = default)
    {
        try
        {
            backupStream.Position = 0;
            using var archive = new ZipArchive(backupStream, ZipArchiveMode.Read, true);

            var metadataEntry = archive.GetEntry("metadata.json");
            if (metadataEntry == null)
            {
                return null;
            }

            using var metadataStream = metadataEntry.Open();
            return await JsonSerializer.DeserializeAsync<BackupMetadata>(metadataStream);
        }
        catch
        {
            return null;
        }
    }

    private async Task DecryptAndExtractAsync(Stream encryptedStream, string targetDir, string password, CancellationToken cancellationToken = default)
    {
        encryptedStream.Position = 0;

        var tempZip = Path.Combine(Path.GetTempPath(), $"decrypted_{Guid.NewGuid()}.zip");

        try
        {
            // Mirrors BackupManagementService.EncryptBackupAsync's exact on-disk format: a
            // 16-byte random IV followed by AES-CBC ciphertext, key derived from the password
            // the same way (SHA256). leaveOpen on the CryptoStream since encryptedStream is
            // owned by the caller.
            var iv = new byte[16];
            var ivBytesRead = await encryptedStream.ReadAsync(iv.AsMemory(0, 16), cancellationToken);
            if (ivBytesRead != 16)
            {
                throw new InvalidOperationException("Verschluesselte Datei ist zu kurz, um einen gueltigen IV zu enthalten.");
            }

            try
            {
                using var aes = Aes.Create();
                aes.Key = DeriveKeyFromPassword(password);
                aes.IV = iv;

                await using (var fileStream = File.Create(tempZip))
                await using (var cryptoStream = new CryptoStream(encryptedStream, aes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: true))
                {
                    await cryptoStream.CopyToAsync(fileStream, cancellationToken);
                }

                ZipFile.ExtractToDirectory(tempZip, targetDir);
            }
            catch (Exception ex) when (ex is CryptographicException or InvalidDataException)
            {
                // A wrong password decrypts to garbage: CryptoStream's PKCS7 unpadding rejects it
                // (CryptographicException), or the "decrypted" bytes simply aren't a ZIP (InvalidDataException).
                throw new InvalidOperationException("Entschluesselung fehlgeschlagen - falsches Passwort?", ex);
            }
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    // Must stay byte-for-byte identical to BackupManagementService.DeriveKeyFromPassword -
    // this is the decrypt half of the same encrypt/decrypt pair.
    private static byte[] DeriveKeyFromPassword(string password)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
    }

    private async Task ReplaceDatabaseAsync(string backupDirectory, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting JSON-based database restore for {Provider}...", _databaseSettings.Provider);

        var progress = new Progress<string>(msg =>
        {
            _logger.LogInformation(msg);
            ReportProgress(null, 0, msg, RestoreStep.ReplacingDatabase);
        });

        await _jsonBackupHelper.RestoreFromJsonBackupAsync(backupDirectory, progress, cancellationToken);

        _logger.LogInformation("JSON restore completed successfully for {Provider}", _databaseSettings.Provider);
    }

    private async Task ReInitializeDatabaseConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.CanConnectAsync();
    }

    private async Task<int> CountTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        if (_databaseProviderService.Provider == DatabaseProvider.SQLite)
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        else if (_databaseProviderService.Provider == DatabaseProvider.PostgreSQL)
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public'";

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        else if (_databaseProviderService.Provider == DatabaseProvider.MySQL)
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE()";

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        return 0;
    }

    private async Task<int> CountRecordsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var productCount = await context.Products.CountAsync(cancellationToken);
        var categoryCount = await context.Categories.CountAsync(cancellationToken);
        var userCount = await context.Users.CountAsync(cancellationToken);
        var warehouseCount = await context.Warehouses.CountAsync(cancellationToken);
        var movementCount = await context.StockMovements.CountAsync(cancellationToken);

        return productCount + categoryCount + userCount + warehouseCount + movementCount;
    }

    private string GetDatabasePath()
    {
        if (_databaseSettings.Provider != DatabaseProvider.SQLite)
        {
            throw new InvalidOperationException("GetDatabasePath only works for SQLite");
        }

        var connectionString = _databaseSettings.ConnectionString;
        var dataSourceIndex = connectionString.IndexOf("Data Source=", StringComparison.OrdinalIgnoreCase);

        if (dataSourceIndex >= 0)
        {
            var startIndex = dataSourceIndex + "Data Source=".Length;
            var endIndex = connectionString.IndexOf(';', startIndex);

            var path = endIndex >= 0
                ? connectionString.Substring(startIndex, endIndex - startIndex)
                : connectionString.Substring(startIndex);

            return path.Trim();
        }

        return "inventory.db";
    }

    private void ReportProgress(IProgress<RestoreProgress>? progress, int percentage, string step, RestoreStep restoreStep)
    {
        progress?.Report(new RestoreProgress
        {
            Percentage = percentage,
            CurrentStep = step,
            Step = restoreStep
        });
    }
}
