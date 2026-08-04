namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Backup metadata for JSON and binary backups.
/// </summary>
public sealed class BackupMetadata
{
    public DateTime BackupDate { get; set; }
    public string DatabaseProvider { get; set; } = string.Empty;
    public string ApplicationVersion { get; set; } = string.Empty;
    public Dictionary<string, int> TableCounts { get; set; } = new();

    // JSON Backup Support
    public string Version { get; set; } = string.Empty;
    public string BackupType { get; set; } = string.Empty;
}
