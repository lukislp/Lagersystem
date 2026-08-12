using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services.BackupProviders;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LagersystemLVHome.Application.Services;

public sealed partial class BackupManagementService : IBackupManagementService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IDatabaseProviderService _databaseProviderService;
    private readonly IEncryptionService _encryptionService;
    private readonly IEmailService _emailService;
    private readonly ILogger<BackupManagementService> _logger;
    private readonly JsonBackupHelper _jsonBackupHelper;
    private readonly BackupProviderFactory _providerFactory;
    private readonly ISecureConfigurationService _secureConfig;

    public BackupManagementService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        IDatabaseProviderService databaseProviderService,
        IEncryptionService encryptionService,
        IEmailService emailService,
        ILogger<BackupManagementService> logger,
        JsonBackupHelper jsonBackupHelper,
        BackupProviderFactory providerFactory,
        ISecureConfigurationService secureConfig)
    {
        _contextFactory = contextFactory;
        _databaseProviderService = databaseProviderService;
        _encryptionService = encryptionService;
        _emailService = emailService;
        _logger = logger;
        _jsonBackupHelper = jsonBackupHelper;
        _providerFactory = providerFactory;
        _secureConfig = secureConfig;
    }

    public async Task<BackupResult> CreateBackupAsync(CancellationToken ct = default)
    {
        var result = new BackupResult { StartTime = DateTime.UtcNow };

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            var settings = await GetSettingsAsync();

            if (!settings.Enabled)
            {
                result.Success = false;
                result.ErrorMessage = "Backup is disabled";
                return result;
            }

            // 1. Create temporary backup file
            var tempBackupPath = Path.Combine(Path.GetTempPath(), $"backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip");

            LogCreatingJsonBackup(_logger, tempBackupPath);

            // JSON backup (pure .NET, already compressed as ZIP)
            await _jsonBackupHelper.CreateJsonBackupAsync(tempBackupPath, ct);

            var fileInfo = new FileInfo(tempBackupPath);
            if (!fileInfo.Exists)
            {
                result.Success = false;
                result.ErrorMessage = "JSON backup file was not created";
                return result;
            }

            result.OriginalSizeBytes = fileInfo.Length;
            result.IsCompressed = true;

            LogDatabaseBackupCreated(_logger, result.OriginalSizeBytes);

            // 2. Encrypt (only if enabled and password is set)
            if (settings.EncryptBackups && !string.IsNullOrWhiteSpace(settings.EncryptionPassword))
            {
                tempBackupPath = await EncryptBackupAsync(tempBackupPath, settings.EncryptionPassword, ct);
                result.IsEncrypted = true;
                LogBackupEncrypted(_logger);
            }
            else if (settings.EncryptBackups && string.IsNullOrWhiteSpace(settings.EncryptionPassword))
            {
                LogEncryptionEnabledNoPassword(_logger);
            }

            result.FinalSizeBytes = new FileInfo(tempBackupPath).Length;
            result.FileName = Path.GetFileName(tempBackupPath);

            // 3. Upload to all enabled providers
            var providers = await GetProvidersAsync();
            var enabledProviders = providers.Where(p => p.Enabled).ToList();

            foreach (var provider in enabledProviders)
            {
                try
                {
                    await UploadToProviderAsync(provider, tempBackupPath, result, ct);
                }
                catch (Exception ex)
                {
                    LogUploadFailed(_logger, ex, provider.Name);
                    result.FailedProviders.Add(provider.Name);
                }
            }

            // 4. Clean up temporary file
            if (File.Exists(tempBackupPath))
            {
                File.Delete(tempBackupPath);
            }

            result.Success = result.SuccessfulProviders.Count > 0;
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;

            // 5. Automatic validation (if enabled)
            if (result.Success && settings.VerifyBackups && result.CreatedBackupHistoryIds.Any())
            {
                LogValidationStarting(_logger, result.CreatedBackupHistoryIds.Count);

                foreach (var historyId in result.CreatedBackupHistoryIds)
                {
                    try
                    {
                        var isValid = await ValidateBackupAsync(historyId);

                        if (isValid)
                        {
                            result.ValidatedBackups++;
                            LogBackupValidated(_logger, historyId);
                        }
                        else
                        {
                            result.FailedValidations++;
                            LogBackupValidationFailed(_logger, historyId);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.FailedValidations++;
                        LogBackupValidationError(_logger, ex, historyId);
                    }
                }

                LogValidationComplete(_logger, result.ValidatedBackups, result.FailedValidations);
            }
            else if (!settings.VerifyBackups)
            {
                LogValidationSkipped(_logger);
            }

            // 6. Email notification
            if ((result.Success && settings.EmailOnSuccess) || (!result.Success && settings.EmailOnFailure))
            {
                await SendBackupNotificationAsync(result, settings);
            }

            return result;
        }
        catch (Exception ex)
        {
            LogBackupFailed(_logger, ex);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }
    }

    private async Task UploadToProviderAsync(BackupProvider provider, string filePath, BackupResult result, CancellationToken ct)
    {
        var history = new BackupHistory
        {
            BackupProviderId = provider.Id,
            FileName = Path.GetFileName(filePath),
            SizeBytes = new FileInfo(filePath).Length,
            IsEncrypted = result.IsEncrypted,
            IsCompressed = result.IsCompressed,
            BackupDate = DateTime.UtcNow,
            RetentionType = GetRetentionType(),
            Status = BackupStatus.InProgress,
            Duration = TimeSpan.Zero
        };

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        context.BackupHistory.Add(history);
        await context.SaveChangesAsync(ct);

        try
        {
            // Decrypt configuration before upload
            if (!string.IsNullOrEmpty(provider.Configuration))
            {
                try
                {
                    provider.Configuration = _secureConfig.Decrypt(provider.Configuration);
                }
                catch (Exception ex)
                {
                    LogDecryptConfigForUploadFailed(_logger, ex);
                    throw new InvalidOperationException("Cannot upload - configuration is encrypted and cannot be decrypted", ex);
                }
            }

            // Resolve the correct uploader via factory pattern
            var uploader = _providerFactory.GetUploader(provider.Type);
            var uploadSuccess = await uploader.UploadAsync(provider, filePath, ct);

            if (uploadSuccess)
            {
                history.Status = BackupStatus.Success;
                history.Duration = DateTime.UtcNow - history.BackupDate;

                // Statistics are recalculated via UpdateProviderStatisticsAsync
                provider.LastBackupAt = DateTime.UtcNow;

                result.SuccessfulProviders.Add(provider.Name);
                result.CreatedBackupHistoryIds.Add(history.Id);

                LogBackupUploaded(_logger, provider.Name, history.FileName, history.SizeBytes);
            }
            else
            {
                history.Status = BackupStatus.Failed;
                history.ErrorMessage = "Upload failed";
                result.FailedProviders.Add(provider.Name);

                LogBackupUploadProviderFailed(_logger, provider.Name);
            }

            // Save history with status
            context.Entry(history).State = EntityState.Modified;
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            LogUploadProviderError(_logger, ex, provider.Name);
            history.Status = BackupStatus.Failed;
            history.ErrorMessage = ex.Message;
            context.Entry(history).State = EntityState.Modified;
            await context.SaveChangesAsync(ct);
            throw;
        }
    }

    private async Task<string> EncryptBackupAsync(string filePath, string password, CancellationToken ct)
    {
        var encryptedPath = $"{filePath}.enc";

        using var aes = Aes.Create();
        aes.Key = DeriveKeyFromPassword(password);
        aes.IV = GenerateRandomIV();

        await using (var outputStream = File.Create(encryptedPath))
        {
            await outputStream.WriteAsync(aes.IV, ct);

            await using (var cryptoStream = new CryptoStream(outputStream, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: false))
            {
                await using (var inputStream = File.OpenRead(filePath))
                {
                    await inputStream.CopyToAsync(cryptoStream, ct);
                }
            }
        }

        File.Delete(filePath);
        return encryptedPath;
    }

    private byte[] DeriveKeyFromPassword(string password)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
    }

    private byte[] GenerateRandomIV()
    {
        var iv = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(iv);
        return iv;
    }

    private BackupRetentionType GetRetentionType()
    {
        var now = DateTime.UtcNow;

        if (now.Day == 1)
            return BackupRetentionType.Monthly;

        if (now.DayOfWeek == DayOfWeek.Sunday)
            return BackupRetentionType.Weekly;

        return BackupRetentionType.Daily;
    }

    public async Task<List<BackupProvider>> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var providers = await context.BackupProviders
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        // Calculate real statistics from backup history
        foreach (var provider in providers)
        {
            await UpdateProviderStatisticsAsync(context, provider);

            // Decrypt configuration on load
            if (!string.IsNullOrEmpty(provider.Configuration))
            {
                try
                {
                    provider.Configuration = _secureConfig.Decrypt(provider.Configuration);
                }
                catch (Exception ex)
                {
                    LogDecryptConfigFailed(_logger, ex, provider.Name);
                }
            }
        }

        return providers;
    }

    private async Task UpdateProviderStatisticsAsync(InventoryDbContext context, BackupProvider provider, CancellationToken cancellationToken = default)
    {
        var backups = await context.BackupHistory
            .Where(h => h.BackupProviderId == provider.Id)
            .ToListAsync(cancellationToken);

        if (backups.Any())
        {
            var successfulBackups = backups.Where(b => b.Status == BackupStatus.Success).ToList();
            provider.TotalBackups = successfulBackups.Count;

            // Total size (successful backups only)
            provider.TotalSizeBytes = successfulBackups.Sum(b => b.SizeBytes);

            provider.FailedBackups = backups.Count(b => b.Status == BackupStatus.Failed);

            var lastBackup = backups.OrderByDescending(b => b.BackupDate).FirstOrDefault();
            if (lastBackup != null)
            {
                provider.LastBackupAt = lastBackup.BackupDate;
            }

            LogProviderStats(_logger, provider.Name, provider.TotalBackups, provider.TotalSizeBytes, provider.FailedBackups);
        }
        else
        {
            provider.TotalBackups = 0;
            provider.TotalSizeBytes = 0;
            provider.FailedBackups = 0;
            provider.LastBackupAt = null;
        }
    }

    public async Task<BackupProvider> AddProviderAsync(BackupProvider provider, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        provider.CreatedAt = DateTime.UtcNow;

        if (provider.LastBackupAt.HasValue && provider.LastBackupAt.Value.Kind != DateTimeKind.Utc)
        {
            provider.LastBackupAt = DateTime.SpecifyKind(provider.LastBackupAt.Value, DateTimeKind.Utc);
        }

        // Encrypt configuration before saving
        if (!string.IsNullOrEmpty(provider.Configuration))
        {
            try
            {
                provider.Configuration = _secureConfig.Encrypt(provider.Configuration);
                LogConfigEncrypted(_logger, provider.Name);
            }
            catch (Exception ex)
            {
                LogConfigEncryptFailed(_logger, ex, provider.Name);
                throw new InvalidOperationException("Failed to secure provider configuration", ex);
            }
        }

        context.BackupProviders.Add(provider);
        await context.SaveChangesAsync(cancellationToken);
        return provider;
    }

    public async Task<BackupProvider> UpdateProviderAsync(BackupProvider provider, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        if (provider.CreatedAt.Kind != DateTimeKind.Utc)
        {
            provider.CreatedAt = DateTime.SpecifyKind(provider.CreatedAt, DateTimeKind.Utc);
        }

        if (provider.LastBackupAt.HasValue && provider.LastBackupAt.Value.Kind != DateTimeKind.Utc)
        {
            provider.LastBackupAt = DateTime.SpecifyKind(provider.LastBackupAt.Value, DateTimeKind.Utc);
        }

        // Encrypt configuration before saving
        if (!string.IsNullOrEmpty(provider.Configuration))
        {
            try
            {
                // Only encrypt if not already encrypted
                if (!_secureConfig.IsEncrypted(provider.Configuration))
                {
                    provider.Configuration = _secureConfig.Encrypt(provider.Configuration);
                    LogConfigEncrypted(_logger, provider.Name);
                }
            }
            catch (Exception ex)
            {
                LogConfigEncryptFailed(_logger, ex, provider.Name);
                throw new InvalidOperationException("Failed to secure provider configuration", ex);
            }
        }

        context.BackupProviders.Update(provider);
        await context.SaveChangesAsync(cancellationToken);
        return provider;
    }

    public async Task DeleteProviderAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var provider = await context.BackupProviders.FindAsync(id);

        if (provider != null)
        {
            context.BackupProviders.Remove(provider);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<List<BackupHistory>> GetHistoryAsync(int? providerId = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.BackupHistory
            .Include(h => h.BackupProvider)
            .OrderByDescending(h => h.BackupDate)
            .AsQueryable();

        if (providerId.HasValue)
        {
            query = query.Where(h => h.BackupProviderId == providerId.Value);
        }

        return await query.Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<BackupSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await context.BackupSettings.FirstOrDefaultAsync(cancellationToken);

        if (settings == null)
        {
            settings = new BackupSettings
            {
                Enabled = true,
                BackupHour = 2,
                RetentionDays = 30,
                WeeklyBackups = 4,
                MonthlyBackups = 12,
                CompressBackups = true,
                EncryptBackups = true,
                VerifyBackups = true,
                EmailOnSuccess = false,
                EmailOnFailure = true
            };

            context.BackupSettings.Add(settings);
            await context.SaveChangesAsync(cancellationToken);
        }

        return settings;
    }

    public async Task<BackupSettings> UpdateSettingsAsync(BackupSettings settings, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        settings.UpdatedAt = DateTime.UtcNow;
        context.BackupSettings.Update(settings);
        await context.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task<bool> TestProviderAsync(int providerId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var provider = await context.BackupProviders.FindAsync(providerId);

        if (provider == null) return false;

        try
        {
            // Decrypt configuration before testing
            if (!string.IsNullOrEmpty(provider.Configuration))
            {
                provider.Configuration = _secureConfig.Decrypt(provider.Configuration);
            }

            var uploader = _providerFactory.GetUploader(provider.Type);
            return await uploader.TestConnectionAsync(provider);
        }
        catch (Exception ex)
        {
            LogProviderTestFailed(_logger, ex, provider.Name);
            return false;
        }
    }

    public async Task CleanupOldBackupsAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

        var oldDaily = await context.BackupHistory
            .Where(h => h.RetentionType == BackupRetentionType.Daily && h.BackupDate < cutoffDate.AddDays(7))
            .ToListAsync(cancellationToken);

        context.BackupHistory.RemoveRange(oldDaily);

        // Weekly/monthly backups should survive further into the past than daily ones -
        // AddDays needs a NEGATIVE offset here to push the cutoff further back in time
        // (a positive offset moved it forward, making weekly stricter than daily and
        // monthly land in the future, so every monthly backup was deleted unconditionally).
        var oldWeekly = await context.BackupHistory
            .Where(h => h.RetentionType == BackupRetentionType.Weekly && h.BackupDate < cutoffDate.AddDays(-28))
            .ToListAsync(cancellationToken);

        context.BackupHistory.RemoveRange(oldWeekly);

        var oldMonthly = await context.BackupHistory
            .Where(h => h.RetentionType == BackupRetentionType.Monthly && h.BackupDate < cutoffDate.AddDays(-365))
            .ToListAsync(cancellationToken);

        context.BackupHistory.RemoveRange(oldMonthly);

        await context.SaveChangesAsync(cancellationToken);

        LogOldBackupsCleanedUp(_logger, oldDaily.Count + oldWeekly.Count + oldMonthly.Count);
    }

    public async Task<bool> ValidateBackupAsync(int historyId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var history = await context.BackupHistory
                .Include(h => h.BackupProvider)
                .FirstOrDefaultAsync(h => h.Id == historyId, cancellationToken);

            if (history == null)
                return false;

            // Decrypt configuration before validation
            if (!string.IsNullOrEmpty(history.BackupProvider.Configuration))
            {
                history.BackupProvider.Configuration = _secureConfig.Decrypt(history.BackupProvider.Configuration);
            }

            var uploader = _providerFactory.GetUploader(history.BackupProvider.Type);
            var exists = await uploader.ValidateAsync(history);

            if (exists)
            {
                history.IsVerified = true;
                history.VerifiedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(cancellationToken);

                LogBackupSuccessfullyValidated(_logger, historyId);
                return true;
            }

            LogBackupValidationFileNotFound(_logger, historyId);
            return false;
        }
        catch (Exception ex)
        {
            LogBackupValidationError(_logger, ex, historyId);
            return false;
        }
    }

    public async Task DeleteBackupAsync(int historyId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var history = await context.BackupHistory
            .Include(h => h.BackupProvider)
            .FirstOrDefaultAsync(h => h.Id == historyId, cancellationToken);

        if (history == null)
        {
            LogBackupNotFound(_logger, historyId);
            return;
        }

        try
        {
            // Decrypt configuration before deletion
            if (!string.IsNullOrEmpty(history.BackupProvider.Configuration))
            {
                history.BackupProvider.Configuration = _secureConfig.Decrypt(history.BackupProvider.Configuration);
            }

            var uploader = _providerFactory.GetUploader(history.BackupProvider.Type);
            var deleted = await uploader.DeleteAsync(history);

            // Delete history entry (stats are recalculated on next GetProvidersAsync)
            context.BackupHistory.Remove(history);
            await context.SaveChangesAsync(cancellationToken);

            if (deleted)
            {
                LogBackupDeleted(_logger, historyId, history.BackupProvider.Name, history.FileName);
            }
            else
            {
                LogBackupDeletedDbOnly(_logger, historyId);
            }
        }
        catch (Exception ex)
        {
            LogBackupDeleteError(_logger, ex, historyId);
            throw;
        }
    }

    public async Task CleanupBackupsByProviderSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await GetSettingsAsync();

        LogCleanupStarting(_logger);

        var now = DateTime.UtcNow;
        var dailyCutoff = now.AddDays(-settings.RetentionDays);

        int totalDeleted = 0;
        totalDeleted += await CleanupBackupsOlderThanAsync(context, BackupRetentionType.Daily, dailyCutoff, cancellationToken);
        // Same "further into the past" reasoning as CleanupOldBackupsAsync: weekly/monthly
        // backups get an additional grace period on top of the daily retention window.
        totalDeleted += await CleanupBackupsOlderThanAsync(context, BackupRetentionType.Weekly, dailyCutoff.AddDays(-28), cancellationToken);
        totalDeleted += await CleanupBackupsOlderThanAsync(context, BackupRetentionType.Monthly, dailyCutoff.AddDays(-365), cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        LogCleanupComplete(_logger, totalDeleted);
    }

    private async Task<int> CleanupBackupsOlderThanAsync(
        InventoryDbContext context,
        BackupRetentionType retentionType,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var candidates = await context.BackupHistory
            .Include(h => h.BackupProvider)
            .Where(h => h.RetentionType == retentionType && h.BackupDate < cutoff)
            .ToListAsync(cancellationToken);

        var deletedCount = 0;
        foreach (var backup in candidates)
        {
            try
            {
                // Decrypt configuration before deletion
                if (!string.IsNullOrEmpty(backup.BackupProvider.Configuration))
                {
                    backup.BackupProvider.Configuration = _secureConfig.Decrypt(backup.BackupProvider.Configuration);
                }

                var uploader = _providerFactory.GetUploader(backup.BackupProvider.Type);
                await uploader.DeleteAsync(backup);

                // Only remove the DB row once the remote side is confirmed handled (deleted,
                // or DeleteAsync returned false because it was already gone - either way not
                // an exception). If DeleteAsync throws, leave the row in place so the app
                // doesn't "forget" a backup that may still exist at the provider; it's picked
                // up again on the next cleanup run.
                context.BackupHistory.Remove(backup);
                deletedCount++;
            }
            catch (Exception ex)
            {
                LogBackupDeleteError(_logger, ex, backup.Id);
            }
        }

        return deletedCount;
    }

    private async Task SendBackupNotificationAsync(BackupResult result, BackupSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(settings.EmailRecipients)) return;

        var subject = result.Success ? "Backup Erfolgreich" : "Backup Fehlgeschlagen";

        var validationInfo = "";
        if (result.ValidatedBackups > 0 || result.FailedValidations > 0)
        {
            validationInfo = $@"
<h3>Validierung</h3>
<p><strong>Erfolgreich validiert:</strong> {result.ValidatedBackups}</p>
<p><strong>Fehlgeschlagen:</strong> {result.FailedValidations}</p>";
        }

        var body = $@"
<h2>{subject}</h2>
<p><strong>Zeitstempel:</strong> {result.StartTime:dd.MM.yyyy HH:mm:ss}</p>
<p><strong>Dauer:</strong> {result.Duration.TotalSeconds:F1}s</p>
<p><strong>Datei:</strong> {result.FileName}</p>
<p><strong>Groesse:</strong> {result.FinalSizeBytes / 1024 / 1024:F2} MB (Original: {result.OriginalSizeBytes / 1024 / 1024:F2} MB)</p>
<p><strong>Komprimiert:</strong> {(result.IsCompressed ? "Ja" : "Nein")}</p>
<p><strong>Verschluesselt:</strong> {(result.IsEncrypted ? "Ja" : "Nein")}</p>
<p><strong>Erfolgreiche Provider:</strong> {string.Join(", ", result.SuccessfulProviders)}</p>
{(result.FailedProviders.Any() ? $"<p><strong>Fehlgeschlagene Provider:</strong> {string.Join(", ", result.FailedProviders)}</p>" : "")}
{validationInfo}
";

        var recipients = settings.EmailRecipients.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var recipient in recipients)
        {
            try
            {
                await _emailService.SendEmailAsync(recipient.Trim(), subject, body, isHtml: true);
            }
            catch (Exception ex)
            {
                LogNotificationSendFailed(_logger, ex, recipient);
            }
        }
    }
}

public sealed class BackupResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long OriginalSizeBytes { get; set; }
    public long FinalSizeBytes { get; set; }
    public bool IsCompressed { get; set; }
    public bool IsEncrypted { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> SuccessfulProviders { get; set; } = new();
    public List<string> FailedProviders { get; set; } = new();
    public List<int> CreatedBackupHistoryIds { get; set; } = new();
    public int ValidatedBackups { get; set; } = 0;
    public int FailedValidations { get; set; } = 0;
}
