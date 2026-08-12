using System.IO.Compression;
using LagersystemLVHome.Application.Configuration;
using BackupSettings = LagersystemLVHome.Application.Configuration.BackupSettings;

namespace LagersystemLVHome.Application.Services;

public sealed class BackupInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long SizeBytes { get; set; }
    public string SizeFormatted => FormatBytes(SizeBytes);
    public bool IsCompressed { get; set; }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

public sealed class BackupService : IBackupService
{
    private readonly IDatabaseProviderService _dbProvider;
    private readonly BackupSettings _settings;
    private readonly ILogger<BackupService> _logger;
    private readonly string _backupDirectory;

    public BackupService(
        IDatabaseProviderService dbProvider,
        BackupSettings settings,
        ILogger<BackupService> logger,
        IWebHostEnvironment environment)
    {
        _dbProvider = dbProvider;
        _settings = settings;
        _logger = logger;
        _backupDirectory = Path.Combine(environment.ContentRootPath, settings.BackupDirectory);

        Directory.CreateDirectory(_backupDirectory);
    }

    public async Task CreateBackupAsync(string? customName = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var backupName = customName ?? $"backup_{timestamp}";
            var extension = _dbProvider.Provider switch
            {
                DatabaseProvider.SQLite => ".db",
                DatabaseProvider.PostgreSQL => ".backup",
                DatabaseProvider.MySQL => ".sql",
                _ => ".bak"
            };

            var backupPath = Path.Combine(_backupDirectory, $"{backupName}{extension}");

            // Create backup
            await _dbProvider.BackupDatabaseAsync(backupPath);

            // Compress if enabled
            if (_settings.CompressBackups)
            {
                var compressedPath = $"{backupPath}.gz";
                await CompressFileAsync(backupPath, compressedPath);
                File.Delete(backupPath);
                _logger.LogInformation("Backup created and compressed: {CompressedPath}", compressedPath);
            }
            else
            {
                _logger.LogInformation("Backup created: {BackupPath}", backupPath);
            }

            // Cleanup old backups
            await CleanupOldBackupsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backup");
            throw;
        }
    }

    public async Task<IEnumerable<BackupInfo>> GetBackupsAsync(CancellationToken cancellationToken = default)
    {
        var backups = new List<BackupInfo>();
        var extensions = new[] { ".db", ".backup", ".sql", ".bak", ".gz" };

        foreach (var file in Directory.GetFiles(_backupDirectory)
            .Where(f => extensions.Any(ext => f.EndsWith(ext))))
        {
            var fileInfo = new FileInfo(file);
            backups.Add(new BackupInfo
            {
                FileName = fileInfo.Name,
                FullPath = fileInfo.FullName,
                CreatedAt = fileInfo.CreationTime,
                SizeBytes = fileInfo.Length,
                IsCompressed = fileInfo.Extension == ".gz"
            });
        }

        return await Task.FromResult(backups.OrderByDescending(b => b.CreatedAt));
    }

    public async Task RestoreBackupAsync(string backupFileName, CancellationToken cancellationToken = default)
    {
        try
        {
            var backupPath = Path.Combine(_backupDirectory, backupFileName);

            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException($"Backup file not found: {backupFileName}");
            }

            // Decompress if needed
            if (backupFileName.EndsWith(".gz"))
            {
                var decompressedPath = backupPath.Replace(".gz", "");
                await DecompressFileAsync(backupPath, decompressedPath);
                backupPath = decompressedPath;
            }

            // Create safety backup before restore
            await CreateBackupAsync($"pre_restore_{DateTime.UtcNow:yyyyMMdd_HHmmss}");

            // Restore database
            await _dbProvider.RestoreDatabaseAsync(backupPath);

            // Cleanup temporary decompressed file
            if (backupFileName.EndsWith(".gz"))
            {
                File.Delete(backupPath);
            }

            _logger.LogInformation("Database restored from backup: {BackupFileName}", backupFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore backup: {BackupFileName}", backupFileName);
            throw;
        }
    }

    public async Task DeleteBackupAsync(string backupFileName, CancellationToken cancellationToken = default)
    {
        var backupPath = Path.Combine(_backupDirectory, backupFileName);

        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
            _logger.LogInformation("Backup deleted: {BackupFileName}", backupFileName);
        }

        await Task.CompletedTask;
    }

    public async Task CleanupOldBackupsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var backups = await GetBackupsAsync();
            var backupsToDelete = backups
                .OrderByDescending(b => b.CreatedAt)
                .Skip(_settings.MaxBackupCount)
                .ToList();

            foreach (var backup in backupsToDelete)
            {
                await DeleteBackupAsync(backup.FileName);
            }

            if (backupsToDelete.Any())
            {
                _logger.LogInformation("Cleaned up {Count} old backups", backupsToDelete.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old backups");
        }
    }

    private static async Task CompressFileAsync(string sourceFile, string destinationFile, CancellationToken cancellationToken = default)
    {
        using var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read);
        using var destinationStream = new FileStream(destinationFile, FileMode.Create, FileAccess.Write);
        using var compressionStream = new GZipStream(destinationStream, CompressionMode.Compress);
        await sourceStream.CopyToAsync(compressionStream);
    }

    private static async Task DecompressFileAsync(string sourceFile, string destinationFile, CancellationToken cancellationToken = default)
    {
        using var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read);
        using var decompressionStream = new GZipStream(sourceStream, CompressionMode.Decompress);
        using var destinationStream = new FileStream(destinationFile, FileMode.Create, FileAccess.Write);
        await decompressionStream.CopyToAsync(destinationStream);
    }
}

public sealed class BackupHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly BackupSettings _settings;
    private readonly ILogger<BackupHostedService> _logger;

    public BackupHostedService(
        IServiceProvider serviceProvider,
        BackupSettings settings,
        ILogger<BackupHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.EnableAutoBackup)
        {
            _logger.LogInformation("Automatic backups are disabled");
            return;
        }

        _logger.LogInformation("Automatic backup service started. Interval: {Hours} hours", _settings.BackupIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(_settings.BackupIntervalHours), stoppingToken);

                using var scope = _serviceProvider.CreateScope();
                var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();

                await backupService.CreateBackupAsync();
                _logger.LogInformation("Automatic backup completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automatic backup failed");
            }
        }
    }
}
