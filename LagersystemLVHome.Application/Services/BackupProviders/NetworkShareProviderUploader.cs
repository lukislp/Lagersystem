using LagersystemLVHome.Domain.Models;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LagersystemLVHome.Application.Services.BackupProviders;

/// <summary>
/// Network share backup provider implementation.
/// </summary>
public sealed class NetworkShareProviderUploader : IBackupProviderUploader
{
    private readonly ILogger<NetworkShareProviderUploader> _logger;

    public BackupProviderType SupportedProviderType => BackupProviderType.NetworkShare;

    public NetworkShareProviderUploader(ILogger<NetworkShareProviderUploader> logger)
    {
        _logger = logger;
    }

    // Windows API for network share authentication
    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetAddConnection2(ref NETRESOURCE lpNetResource, string lpPassword, string lpUsername, int dwFlags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetCancelConnection2(string lpName, int dwFlags, bool fForce);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    private const int RESOURCETYPE_DISK = 0x1;
    private const int CONNECT_UPDATE_PROFILE = 0x1;

    public async Task<bool> UploadAsync(BackupProvider provider, string filePath, CancellationToken ct = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<NetworkShareConfig>(provider.Configuration);
            if (config == null || !config.Paths.Any(p => p.Enabled))
            {
                _logger.LogError("Network share config missing or has no enabled paths");
                return false;
            }

            bool anySuccess = false;

            foreach (var sharePath in config.Paths.Where(p => p.Enabled))
            {
                bool wasConnected = false;
                try
                {
                    // Authenticate to network share if credentials are provided
                    if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
                    {
                        wasConnected = ConnectToNetworkShare(sharePath.UncPath, config.Username, config.Password);

                        if (!wasConnected)
                        {
                            _logger.LogWarning("Failed to authenticate to network share: {Path} (trying anyway)", sharePath.UncPath);
                        }
                        else
                        {
                            _logger.LogInformation("Successfully authenticated to network share: {Path}", sharePath.UncPath);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("No credentials provided, using current Windows user authentication");
                    }

                    var fileName = Path.GetFileName(filePath);
                    string targetPath = sharePath.UncPath;

                    // Hierarchical folder structure
                    if (config.CreateDateSubfolders)
                    {
                        var now = DateTime.UtcNow;
                        var dateFolder = now.ToString("yyyy-MM");
                        targetPath = Path.Combine(sharePath.UncPath, dateFolder);

                        if (config.CreateWeekSubfolders)
                        {
                            var weekNumber = GetWeekOfYear(now);
                            var weekFolder = $"Woche-{weekNumber:D2}";
                            targetPath = Path.Combine(targetPath, weekFolder);
                        }
                    }

                    Directory.CreateDirectory(targetPath);

                    var destPath = Path.Combine(targetPath, fileName);

                    await using var sourceStream = File.OpenRead(filePath);
                    await using var destStream = File.Create(destPath);
                    await sourceStream.CopyToAsync(destStream, ct);

                    _logger.LogInformation("Backup uploaded to network share: {Path} ({Description})",
                        destPath, sharePath.Description);
                    anySuccess = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload to network share: {Path}", sharePath.UncPath);
                }
                finally
                {
                    // Disconnect after upload
                    if (wasConnected)
                    {
                        DisconnectFromNetworkShare(sharePath.UncPath);
                    }
                }
            }

            return anySuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload to network share provider");
            return false;
        }
    }

    public async Task<bool> ValidateAsync(BackupHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<NetworkShareConfig>(history.BackupProvider.Configuration);
            if (config == null) return false;

            foreach (var sharePath in config.Paths.Where(p => p.Enabled))
            {
                bool wasConnected = false;
                try
                {
                    // Authenticate for validation
                    if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
                    {
                        wasConnected = ConnectToNetworkShare(sharePath.UncPath, config.Username, config.Password);
                    }

                    var filePaths = GetPossibleFilePaths(sharePath.UncPath, history, config);

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
                finally
                {
                    if (wasConnected)
                    {
                        DisconnectFromNetworkShare(sharePath.UncPath);
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating network share backup");
            return false;
        }
    }

    public async Task<bool> DeleteAsync(BackupHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<NetworkShareConfig>(history.BackupProvider.Configuration);
            if (config == null) return false;

            bool deleted = false;

            foreach (var sharePath in config.Paths.Where(p => p.Enabled))
            {
                bool wasConnected = false;
                try
                {
                    // Authenticate for deletion
                    if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
                    {
                        wasConnected = ConnectToNetworkShare(sharePath.UncPath, config.Username, config.Password);
                    }

                    var filePaths = GetPossibleFilePaths(sharePath.UncPath, history, config);

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
                finally
                {
                    if (wasConnected)
                    {
                        DisconnectFromNetworkShare(sharePath.UncPath);
                    }
                }
            }

            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting network share backup");
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync(BackupProvider provider, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<NetworkShareConfig>(provider.Configuration);
            if (config == null || !config.Paths.Any(p => p.Enabled))
                return false;

            var testFile = Path.Combine(Path.GetTempPath(), "backup_test.txt");
            await File.WriteAllTextAsync(testFile, "Test");

            try
            {
                foreach (var sharePath in config.Paths.Where(p => p.Enabled))
                {
                    bool wasConnected = false;
                    try
                    {
                        // Authenticate for connection test
                        if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
                        {
                            wasConnected = ConnectToNetworkShare(sharePath.UncPath, config.Username, config.Password);

                            if (!wasConnected)
                            {
                                _logger.LogWarning("Authentication failed for: {Path}", sharePath.UncPath);
                                continue;
                            }
                        }

                        Directory.CreateDirectory(sharePath.UncPath);
                        var testDest = Path.Combine(sharePath.UncPath, Path.GetFileName(testFile));
                        File.Copy(testFile, testDest, overwrite: true);
                        File.Delete(testDest);
                        File.Delete(testFile);

                        _logger.LogInformation("Connection test successful for: {Path}", sharePath.UncPath);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Connection test failed for: {Path}", sharePath.UncPath);
                        continue;
                    }
                    finally
                    {
                        if (wasConnected)
                        {
                            DisconnectFromNetworkShare(sharePath.UncPath);
                        }
                    }
                }

                return false;
            }
            finally
            {
                if (File.Exists(testFile))
                    File.Delete(testFile);
            }
        }
        catch
        {
            return false;
        }
    }

    // Authenticate to network share
    private bool ConnectToNetworkShare(string uncPath, string username, string password)
    {
        try
        {
            // Extract server name from UNC path (\\server\share)
            var parts = uncPath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                _logger.LogError("Invalid UNC path format: {Path}", uncPath);
                return false;
            }

            var serverShare = $"\\\\{parts[0]}\\{parts[1]}";

            var netResource = new NETRESOURCE
            {
                dwType = RESOURCETYPE_DISK,
                lpRemoteName = serverShare
            };

            int result = WNetAddConnection2(ref netResource, password, username, 0);

            if (result == 0)
            {
                _logger.LogInformation("Connected to network share: {Path} as {Username}", serverShare, username);
                return true;
            }
            else
            {
                var errorMessage = Marshal.GetLastWin32Error();
                _logger.LogError("Failed to connect to network share: {Path} as {Username}. Win32 Error: {Error}",
                    serverShare, username, errorMessage);

                // Common error codes:
                // 86 = ERROR_INVALID_PASSWORD
                // 1326 = ERROR_LOGON_FAILURE (wrong username/password)
                // 1219 = ERROR_SESSION_CREDENTIAL_CONFLICT (already connected with different credentials)

                if (errorMessage == 1219)
                {
                    _logger.LogWarning("Already connected with different credentials. Trying to use existing connection...");
                    return true;
                }

                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception connecting to network share: {Path}", uncPath);
            return false;
        }
    }

    // Disconnect from network share
    private void DisconnectFromNetworkShare(string uncPath)
    {
        try
        {
            var parts = uncPath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var serverShare = $"\\\\{parts[0]}\\{parts[1]}";
                int result = WNetCancelConnection2(serverShare, 0, true);

                if (result == 0)
                {
                    _logger.LogDebug("Disconnected from network share: {Path}", serverShare);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to disconnect from network share: {Path}", uncPath);
        }
    }

    private List<string> GetPossibleFilePaths(string baseUncPath, BackupHistory history, NetworkShareConfig config)
    {
        var paths = new List<string>
        {
            Path.Combine(baseUncPath, history.FileName)
        };

        if (config.CreateDateSubfolders)
        {
            var dateFolder = history.BackupDate.ToString("yyyy-MM");
            paths.Add(Path.Combine(baseUncPath, dateFolder, history.FileName));

            if (config.CreateWeekSubfolders)
            {
                var weekNumber = GetWeekOfYear(history.BackupDate);
                paths.Add(Path.Combine(baseUncPath, dateFolder, $"Woche-{weekNumber:D2}", history.FileName));
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
