namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// Settings for automatic key backup.
/// </summary>
public class KeyBackupSettings
{
    public bool Enabled { get; set; }
    public int BackupHour { get; set; }
    public int? BackupProviderId { get; set; }
    public int RetentionDays { get; set; }
    public bool RequirePassword { get; set; }
    public string? BackupPassword { get; set; }
    public bool EmailOnSuccess { get; set; }
    public bool EmailOnFailure { get; set; }
    public string? EmailRecipients { get; set; }
}
