namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// History for key backups.
/// </summary>
public class KeyBackupHistory
{
    public int Id { get; set; }
    public DateTime BackupDate { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Reference to the backup provider used.
    /// </summary>
    public int BackupProviderId { get; set; }
    public BackupProvider BackupProvider { get; set; } = null!;

    public int ProviderCount { get; set; }
    public long SizeBytes { get; set; }
    public bool IsEncrypted { get; set; }
    public BackupStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
}
