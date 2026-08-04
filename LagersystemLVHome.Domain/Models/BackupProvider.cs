using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// Backup provider (Local, Azure, AWS, FTP, etc.).
/// </summary>
public class BackupProvider
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public BackupProviderType Type { get; set; }

    public bool Enabled { get; set; } = true;

    // Provider-specific configuration (JSON)
    public string Configuration { get; set; } = "{}";

    // Encryption
    public string? EncryptedPassword { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastBackupAt { get; set; }

    // Stats
    public int TotalBackups { get; set; } = 0;
    public long TotalSizeBytes { get; set; } = 0;
    public int FailedBackups { get; set; } = 0;

    // Navigation
    public virtual ICollection<BackupHistory> BackupHistories { get; set; } = [];
}

public enum BackupProviderType
{
    Local = 0,
    AzureBlob = 1,
    AWSS3 = 2,
    NetworkShare = 3,
    FTP = 4,
    SFTP = 5,
    GoogleDrive = 6,
    Dropbox = 7,
    OneDrive = 8,
    CloudflareR2 = 9
}

/// <summary>
/// Backup history entry.
/// </summary>
public class BackupHistory
{
    public int Id { get; set; }

    public int BackupProviderId { get; set; }
    public virtual BackupProvider BackupProvider { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
    public bool IsEncrypted { get; set; }
    public bool IsCompressed { get; set; }

    public DateTime BackupDate { get; set; } = DateTime.UtcNow;

    public BackupStatus Status { get; set; } = BackupStatus.Success;
    public string? ErrorMessage { get; set; }

    public TimeSpan Duration { get; set; }

    // Backup type (Daily, Weekly, Monthly)
    public BackupRetentionType RetentionType { get; set; }

    // Verification
    public bool IsVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

public enum BackupStatus
{
    Success = 0,
    Failed = 1,
    InProgress = 2,
    Cancelled = 3
}

public enum BackupRetentionType
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Manual = 3
}

/// <summary>
/// Backup settings entity (stored in database).
/// </summary>
public class BackupSettings
{
    public int Id { get; set; }
    public bool Enabled { get; set; } = true;
    public int BackupHour { get; set; } = 2;
    public int RetentionDays { get; set; } = 30;
    public int WeeklyBackups { get; set; } = 4;
    public int MonthlyBackups { get; set; } = 12;
    public bool CompressBackups { get; set; } = true;
    public bool EncryptBackups { get; set; } = true;
    public string? EncryptionPassword { get; set; }
    public bool VerifyBackups { get; set; } = true;
    public bool EmailOnSuccess { get; set; } = false;
    public bool EmailOnFailure { get; set; } = true;
    public string? EmailRecipients { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
