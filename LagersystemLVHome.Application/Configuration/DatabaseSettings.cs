namespace LagersystemLVHome.Application.Configuration;

public enum DatabaseProvider
{
    SQLite,
    PostgreSQL,
    MySQL
}

public class DatabaseSettings
{
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SQLite;
    public string ConnectionString { get; set; } = string.Empty;
    public bool EnableRetryOnFailure { get; set; } = true;
    public int MaxRetryCount { get; set; } = 3;
    public int CommandTimeout { get; set; } = 30;
}

public class CacheSettings
{
    public bool EnableMemoryCache { get; set; } = true;
    public int DefaultExpirationMinutes { get; set; } = 30;
    public int SlidingExpirationMinutes { get; set; } = 10;
    public bool EnableDistributedCache { get; set; } = false;
    public string RedisConnection { get; set; } = "localhost:6379";
}

public class BackupSettings
{
    public bool EnableAutoBackup { get; set; } = true;
    public int BackupIntervalHours { get; set; } = 24;
    public string BackupDirectory { get; set; } = "Backups";
    public int MaxBackupCount { get; set; } = 30;
    public bool CompressBackups { get; set; } = true;
}

public class PerformanceSettings
{
    public bool EnableResponseCompression { get; set; } = true;
    public bool EnableOutputCaching { get; set; } = true;
    public int MaxPageSize { get; set; } = 100;
    public int DefaultPageSize { get; set; } = 25;
}

public class UISettings
{
    public bool EnableDragDrop { get; set; } = true;
    public bool EnableKeyboardShortcuts { get; set; } = true;
    public bool EnableTouchGestures { get; set; } = true;
    public int ToastDurationMs { get; set; } = 5000;
    public bool EnableHapticFeedback { get; set; } = true;
}

public class DashboardSettings
{
    public int RefreshIntervalSeconds { get; set; } = 30;
    public bool EnableRealTimeUpdates { get; set; } = true;
    public int DefaultPeriodDays { get; set; } = 30;
    public int MaxTopMovers { get; set; } = 10;
    public int CacheDurationMinutes { get; set; } = 5;
}
