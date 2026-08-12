using LagersystemLVHome.Domain.Models;
using System.Globalization;
using System.Text.Json;

namespace LagersystemLVHome.Application.Services.BackupProviders;

/// <summary>
/// Local backup provider implementation.
/// </summary>
public sealed class LocalBackupProviderUploader : IBackupProviderUploader
{
    private readonly ILogger<LocalBackupProviderUploader> _logger;

    public BackupProviderType SupportedProviderType => BackupProviderType.Local;

    public LocalBackupProviderUploader(ILogger<LocalBackupProviderUploader> logger)
    {
        _logger = logger;
    }

    public async Task<bool> UploadAsync(BackupProvider provider, string filePath, CancellationToken ct = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<LocalBackupConfig>(provider.Configuration);
            if (config == null || !config.Paths.Any())
            {
                _logger.LogError("Local backup config missing or has no paths");
                return false;
            }

            bool anySuccess = false;

            foreach (var path in config.Paths)
            {
                try
                {
                    string targetPath = path;

                    // Hierarchical folder structure
                    if (config.CreateDateSubfolders)
                    {
                        var now = DateTime.UtcNow;
                        var dateFolder = now.ToString("yyyy-MM");
                        targetPath = Path.Combine(path, dateFolder);

                        if (config.CreateWeekSubfolders)
                        {
                            var weekNumber = GetWeekOfYear(now);
                            var weekFolder = $"Woche-{weekNumber:D2}";
                            targetPath = Path.Combine(targetPath, weekFolder);
                        }
                    }

                    Directory.CreateDirectory(targetPath);

                    var fileName = Path.GetFileName(filePath);
                    var destPath = Path.Combine(targetPath, fileName);

                    await using var sourceStream = File.OpenRead(filePath);
                    await using var destStream = File.Create(destPath);
                    await sourceStream.CopyToAsync(destStream, ct);

                    _logger.LogInformation("Backup uploaded to local path: {Path}", destPath);
                    anySuccess = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload to local path: {Path}", path);
                }
            }

            return anySuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload to local provider");
            return false;
        }
    }

    public async Task<bool> ValidateAsync(BackupHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<LocalBackupConfig>(history.BackupProvider.Configuration);
            if (config == null) return false;

            foreach (var path in config.Paths)
            {
                var filePaths = GetPossibleFilePaths(path, history, config);

                foreach (var filePath in filePaths)
                {
                    if (File.Exists(filePath))
                    {
                        var fileInfo = new FileInfo(filePath);
                        if (fileInfo.Length == history.SizeBytes)
                        {
                            _logger.LogInformation("Backup validated in: {Path}", filePath);
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating local backup");
            return false;
        }
    }

    public async Task<bool> DeleteAsync(BackupHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<LocalBackupConfig>(history.BackupProvider.Configuration);
            if (config == null) return false;

            bool deleted = false;

            foreach (var path in config.Paths)
            {
                var filePaths = GetPossibleFilePaths(path, history, config);

                foreach (var filePath in filePaths)
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        _logger.LogInformation("Deleted backup file: {Path}", filePath);
                        deleted = true;
                        break;
                    }
                }

                if (deleted) break;
            }

            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting local backup");
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync(BackupProvider provider, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<LocalBackupConfig>(provider.Configuration);
            if (config == null || !config.Paths.Any())
                return false;

            // Check whether at least one path exists
            foreach (var path in config.Paths)
            {
                if (Directory.Exists(path))
                    return true;

                // Try to create the directory
                try
                {
                    Directory.CreateDirectory(path);
                    return true;
                }
                catch
                {
                    continue;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private List<string> GetPossibleFilePaths(string basePath, BackupHistory history, LocalBackupConfig config)
    {
        var paths = new List<string>
    {
        Path.Combine(basePath, history.FileName)
    };

        if (config.CreateDateSubfolders)
        {
            var dateFolder = history.BackupDate.ToString("yyyy-MM");
            paths.Add(Path.Combine(basePath, dateFolder, history.FileName));

            if (config.CreateWeekSubfolders)
            {
                var weekNumber = GetWeekOfYear(history.BackupDate);
                paths.Add(Path.Combine(basePath, dateFolder, $"Woche-{weekNumber:D2}", history.FileName));
            }
        }

        return paths;
    }

    private int GetWeekOfYear(DateTime date)
    {
        var culture = CultureInfo.CurrentCulture;
        var calendar = culture.Calendar;
        var dateTimeFormat = culture.DateTimeFormat;

        return calendar.GetWeekOfYear(
                date,
                dateTimeFormat.CalendarWeekRule,
        dateTimeFormat.FirstDayOfWeek
            );
    }
}
