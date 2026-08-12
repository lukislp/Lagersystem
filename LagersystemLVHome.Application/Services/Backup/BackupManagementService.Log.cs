using Microsoft.Extensions.Logging;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Stufe 10 — LoggerMessage source generator catalog for BackupManagementService.
/// EventId range 3000–3099.
/// </summary>
public sealed partial class BackupManagementService
{
    // --- Backup creation (3000-3019) ---

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information,
        Message = "Creating JSON database backup to {TempPath}")]
    private static partial void LogCreatingJsonBackup(ILogger logger, string? tempPath);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Database backup created: {Size:N0} bytes")]
    private static partial void LogDatabaseBackupCreated(ILogger logger, long size);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information,
        Message = "Backup encrypted with password")]
    private static partial void LogBackupEncrypted(ILogger logger);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning,
        Message = "Encryption enabled but no password set - backup will NOT be encrypted")]
    private static partial void LogEncryptionEnabledNoPassword(ILogger logger);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Error,
        Message = "Failed to upload backup to provider {Provider}")]
    private static partial void LogUploadFailed(ILogger logger, Exception ex, string? provider);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Error,
        Message = "Backup failed")]
    private static partial void LogBackupFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Error,
        Message = "Failed to decrypt configuration for upload")]
    private static partial void LogDecryptConfigForUploadFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Information,
        Message = "Backup uploaded to {Provider}: {FileName} ({Size} bytes)")]
    private static partial void LogBackupUploaded(ILogger logger, string? provider, string? fileName, long size);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Warning,
        Message = "Backup upload to {Provider} failed")]
    private static partial void LogBackupUploadProviderFailed(ILogger logger, string? provider);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Error,
        Message = "Upload to {Provider} failed")]
    private static partial void LogUploadProviderError(ILogger logger, Exception ex, string? provider);

    // --- Validation (3020-3029) ---

    [LoggerMessage(EventId = 3020, Level = LogLevel.Information,
        Message = "Starting automatic backup validation for {Count} backup(s)...")]
    private static partial void LogValidationStarting(ILogger logger, int count);

    [LoggerMessage(EventId = 3021, Level = LogLevel.Information,
        Message = "Backup {HistoryId} validated successfully")]
    private static partial void LogBackupValidated(ILogger logger, int historyId);

    [LoggerMessage(EventId = 3022, Level = LogLevel.Warning,
        Message = "Backup {HistoryId} validation failed")]
    private static partial void LogBackupValidationFailed(ILogger logger, int historyId);

    [LoggerMessage(EventId = 3023, Level = LogLevel.Error,
        Message = "Error validating backup {HistoryId}")]
    private static partial void LogBackupValidationError(ILogger logger, Exception ex, int historyId);

    [LoggerMessage(EventId = 3024, Level = LogLevel.Information,
        Message = "Validation complete: {Valid} validated, {Failed} failed")]
    private static partial void LogValidationComplete(ILogger logger, int valid, int failed);

    [LoggerMessage(EventId = 3025, Level = LogLevel.Information,
        Message = "Automatic validation skipped (disabled in settings)")]
    private static partial void LogValidationSkipped(ILogger logger);

    [LoggerMessage(EventId = 3026, Level = LogLevel.Information,
        Message = "Backup {HistoryId} successfully validated")]
    private static partial void LogBackupSuccessfullyValidated(ILogger logger, int historyId);

    [LoggerMessage(EventId = 3027, Level = LogLevel.Warning,
        Message = "Backup {HistoryId} validation failed - file not found")]
    private static partial void LogBackupValidationFileNotFound(ILogger logger, int historyId);

    // --- Provider config (3030-3049) ---

    [LoggerMessage(EventId = 3030, Level = LogLevel.Error,
        Message = "Failed to decrypt configuration for provider {Provider}")]
    private static partial void LogDecryptConfigFailed(ILogger logger, Exception ex, string? provider);

    [LoggerMessage(EventId = 3031, Level = LogLevel.Debug,
        Message = "Provider {Name}: {Total} backups, {Size} bytes, {Failed} failed")]
    private static partial void LogProviderStats(ILogger logger, string? name, int total, long size, int failed);

    [LoggerMessage(EventId = 3032, Level = LogLevel.Information,
        Message = "Configuration encrypted for provider {Provider}")]
    private static partial void LogConfigEncrypted(ILogger logger, string? provider);

    [LoggerMessage(EventId = 3033, Level = LogLevel.Error,
        Message = "Failed to encrypt configuration for provider {Provider}")]
    private static partial void LogConfigEncryptFailed(ILogger logger, Exception ex, string? provider);

    [LoggerMessage(EventId = 3034, Level = LogLevel.Error,
        Message = "Provider test failed for {Provider}")]
    private static partial void LogProviderTestFailed(ILogger logger, Exception ex, string? provider);

    // --- Cleanup (3050-3069) ---

    [LoggerMessage(EventId = 3050, Level = LogLevel.Information,
        Message = "Cleaned up {Count} old backups")]
    private static partial void LogOldBackupsCleanedUp(ILogger logger, int count);

    [LoggerMessage(EventId = 3051, Level = LogLevel.Warning,
        Message = "Backup {HistoryId} not found")]
    private static partial void LogBackupNotFound(ILogger logger, int historyId);

    [LoggerMessage(EventId = 3052, Level = LogLevel.Information,
        Message = "Backup {HistoryId} deleted from {Provider}: {FileName}")]
    private static partial void LogBackupDeleted(ILogger logger, int historyId, string? provider, string? fileName);

    [LoggerMessage(EventId = 3053, Level = LogLevel.Warning,
        Message = "Backup {HistoryId} deleted from database but file deletion failed")]
    private static partial void LogBackupDeletedDbOnly(ILogger logger, int historyId);

    [LoggerMessage(EventId = 3054, Level = LogLevel.Error,
        Message = "Error deleting backup {HistoryId}")]
    private static partial void LogBackupDeleteError(ILogger logger, Exception ex, int historyId);

    [LoggerMessage(EventId = 3055, Level = LogLevel.Information,
        Message = "Starting automatic backup cleanup...")]
    private static partial void LogCleanupStarting(ILogger logger);

    [LoggerMessage(EventId = 3056, Level = LogLevel.Information,
        Message = "Cleanup complete: {Count} backups deleted")]
    private static partial void LogCleanupComplete(ILogger logger, int count);

    // --- Notifications (3070-3079) ---

    [LoggerMessage(EventId = 3070, Level = LogLevel.Error,
        Message = "Failed to send notification to {Recipient}")]
    private static partial void LogNotificationSendFailed(ILogger logger, Exception ex, string? recipient);
}
