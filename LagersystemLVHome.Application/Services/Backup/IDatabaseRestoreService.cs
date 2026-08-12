namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Service for database restore operations (completely separate from backup system).
/// </summary>
public interface IDatabaseRestoreService
{
    Task<RestoreValidationResult> ValidateBackupAsync(Stream backupStream, CancellationToken cancellationToken = default);

    Task<bool> IsBackupEncryptedAsync(Stream backupStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores the database from a backup.
    /// </summary>
    Task<RestoreResult> RestoreFromBackupAsync(
        Stream backupStream,
        string? password = null,
        IProgress<RestoreProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<DatabaseInfo> GetCurrentDatabaseInfoAsync(CancellationToken cancellationToken = default);

    Task<RestoreBackupInfo> GetBackupInfoAsync(Stream backupStream, string? password = null, CancellationToken cancellationToken = default);

    Task<string> CreateSafetyBackupAsync(CancellationToken cancellationToken = default);
}

public sealed class RestoreValidationResult
{
    public bool IsValid { get; set; }
    public bool IsEncrypted { get; set; }
    public bool RequiresPassword { get; set; }
    public string? ErrorMessage { get; set; }
    public BackupMetadata? Metadata { get; set; }
}

public sealed class RestoreResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int TablesRestored { get; set; }
    public int RecordsRestored { get; set; }
    public TimeSpan Duration { get; set; }
    public string? SafetyBackupPath { get; set; }
}

public sealed class RestoreProgress
{
    public int Percentage { get; set; }
    public string CurrentStep { get; set; } = "";
    public RestoreStep Step { get; set; }
}

public enum RestoreStep
{
    Validating = 0,
    CreatingSafetyBackup = 1,
    Extracting = 2,
    Decrypting = 3,
    ReplacingDatabase = 4,
    Reinitializing = 5,
    ValidatingRestore = 6,
    Complete = 7
}

public sealed class DatabaseInfo
{
    public string Provider { get; set; } = "";
    public int ProductCount { get; set; }
    public int CategoryCount { get; set; }
    public int UserCount { get; set; }
    public int WarehouseCount { get; set; }
    public DateTime LastModified { get; set; }
    public long SizeBytes { get; set; }
}

public sealed class RestoreBackupInfo
{
    public string FileName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public long SizeBytes { get; set; }
    public string Provider { get; set; } = "";
    public int ProductCount { get; set; }
    public int CategoryCount { get; set; }
    public int UserCount { get; set; }
    public int WarehouseCount { get; set; }
    public bool IsEncrypted { get; set; }
    public string? EncryptionMethod { get; set; }
}
