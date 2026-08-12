namespace LagersystemLVHome.Application.Services;

public sealed class LocalBackupConfig
{
    public List<string> Paths { get; set; } = new() { "C:\\Backups\\LagerSystem" };
    public int MaxBackups { get; set; } = 7;
    public bool CreateDateSubfolders { get; set; } = false;
    public bool CreateWeekSubfolders { get; set; } = false;
}

public sealed class AzureBlobConfig
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "lagersystem-backups";

    /// <summary>
    /// Maximum number of backups to retain (default: 30).
    /// </summary>
    public int MaxBackups { get; set; } = 30;
}

public sealed class AwsS3Config
{
    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = "eu-central-1";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of backups to retain (default: 30).
    /// </summary>
    public int MaxBackups { get; set; } = 30;
}

public sealed class NetworkShareConfig
{
    public List<NetworkSharePath> Paths { get; set; } = new();
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool CreateDateSubfolders { get; set; } = false;
    public bool CreateWeekSubfolders { get; set; } = false;

    /// <summary>
    /// Maximum number of backups to retain (default: 30).
    /// </summary>
    public int MaxBackups { get; set; } = 30;
}

public sealed class NetworkSharePath
{
    public string UncPath { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Description { get; set; } = string.Empty;
}

public sealed class FtpConfig
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 21;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string RemotePath { get; set; } = "/backups";
    public bool UseSsl { get; set; } = false;

    /// <summary>
    /// Maximum number of backups to retain (default: 30).
    /// </summary>
    public int MaxBackups { get; set; } = 30;
}

public sealed class GoogleDriveConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string FolderId { get; set; } = string.Empty;
    public string FolderName { get; set; } = "LagerSystem Backups";

    /// <summary>
    /// Maximum number of backups to retain (default: 30).
    /// </summary>
    public int MaxBackups { get; set; } = 30;
}

public sealed class OneDriveConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string FolderId { get; set; } = string.Empty;
    public string FolderPath { get; set; } = "/LagerSystem/Backups";

    /// <summary>
    /// Maximum number of backups to retain (default: 30).
    /// </summary>
    public int MaxBackups { get; set; } = 30;
}
