using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services.BackupProviders;

/// <summary>
/// OneDrive backup provider implementation (via REST API).
/// Requires manual OAuth setup - no automatic OAuth flow.
/// </summary>
public sealed class OneDriveProviderUploader : IBackupProviderUploader
{
    private readonly ILogger<OneDriveProviderUploader> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public BackupProviderType SupportedProviderType => BackupProviderType.OneDrive;

    public OneDriveProviderUploader(
        ILogger<OneDriveProviderUploader> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> UploadAsync(BackupProvider provider, string filePath, CancellationToken ct = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<OneDriveConfig>(provider.Configuration);
            if (config == null || string.IsNullOrEmpty(config.ClientId))
            {
                _logger.LogError("OneDrive config missing or invalid");
                return false;
            }

            var accessToken = await GetAccessTokenAsync(config);
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogError("Failed to obtain OneDrive access token");
                return false;
            }

            var fileName = Path.GetFileName(filePath);
            var uploadPath = string.IsNullOrEmpty(config.FolderId)
                ? $"/me/drive/root:/{config.FolderPath.TrimEnd('/')}/{fileName}:/content"
                : $"/me/drive/items/{config.FolderId}:/{fileName}:/content";

            await using var stream = System.IO.File.OpenRead(filePath);

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // Simple upload for small files (< 4 MB)
            if (stream.Length < 4 * 1024 * 1024)
            {
                using var content = new StreamContent(stream);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                var response = await httpClient.PutAsync(
                    $"https://graph.microsoft.com/v1.0{uploadPath}",
                    content,
                    ct);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Backup uploaded to OneDrive: {FolderPath}/{FileName}",
                        config.FolderPath, fileName);
                    return true;
                }

                _logger.LogError("OneDrive upload failed: {StatusCode}", response.StatusCode);
                return false;
            }
            // Upload session for large files
            else
            {
                var sessionUrl = await CreateUploadSessionAsync(httpClient, uploadPath.Replace(":/content", ""), ct);
                if (string.IsNullOrEmpty(sessionUrl))
                {
                    _logger.LogError("Failed to create upload session");
                    return false;
                }

                // Upload in chunks
                var maxChunkSize = 320 * 1024; // 320 KB chunks
                var buffer = new byte[maxChunkSize];
                long position = 0;

                while (position < stream.Length)
                {
                    var chunkSize = (int)Math.Min(maxChunkSize, stream.Length - position);
                    await stream.ReadAsync(buffer, 0, chunkSize, ct);

                    using var chunkContent = new ByteArrayContent(buffer, 0, chunkSize);
                    chunkContent.Headers.Add("Content-Range",
                        $"bytes {position}-{position + chunkSize - 1}/{stream.Length}");

                    var response = await httpClient.PutAsync(sessionUrl, chunkContent, ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError("OneDrive chunked upload failed at position {Position}", position);
                        return false;
                    }

                    position += chunkSize;
                }

                _logger.LogInformation("Backup uploaded to OneDrive (chunked): {FolderPath}/{FileName}",
                    config.FolderPath, fileName);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload to OneDrive");
            return false;
        }
    }

    public async Task<bool> ValidateAsync(BackupHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<OneDriveConfig>(history.BackupProvider.Configuration);
            if (config == null) return false;

            var accessToken = await GetAccessTokenAsync(config);
            if (string.IsNullOrEmpty(accessToken)) return false;

            var itemPath = string.IsNullOrEmpty(config.FolderId)
                ? $"/me/drive/root:/{config.FolderPath.TrimEnd('/')}/{history.FileName}"
                : $"/me/drive/items/{config.FolderId}:/{history.FileName}";

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await httpClient.GetAsync($"https://graph.microsoft.com/v1.0{itemPath}");

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var driveItem = JsonSerializer.Deserialize<DriveItemResponse>(json);

                if (driveItem?.Size == history.SizeBytes)
                {
                    _logger.LogInformation("Backup validated in OneDrive: {FolderPath}/{FileName}",
                        config.FolderPath, history.FileName);
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex.Message.Contains("404") || ex.Message.Contains("NotFound"))
        {
            _logger.LogWarning("Backup not found in OneDrive: {FileName}", history.FileName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating OneDrive backup");
            return false;
        }
    }

    public async Task<bool> DeleteAsync(BackupHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<OneDriveConfig>(history.BackupProvider.Configuration);
            if (config == null) return false;

            var accessToken = await GetAccessTokenAsync(config);
            if (string.IsNullOrEmpty(accessToken)) return false;

            var itemPath = string.IsNullOrEmpty(config.FolderId)
                ? $"/me/drive/root:/{config.FolderPath.TrimEnd('/')}/{history.FileName}"
                : $"/me/drive/items/{config.FolderId}:/{history.FileName}";

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await httpClient.DeleteAsync($"https://graph.microsoft.com/v1.0{itemPath}");

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Deleted backup from OneDrive: {FolderPath}/{FileName}",
                    config.FolderPath, history.FileName);
                return true;
            }

            return false;
        }
        catch (Exception ex) when (ex.Message.Contains("404") || ex.Message.Contains("NotFound"))
        {
            _logger.LogWarning("Backup not found for deletion in OneDrive: {FileName}", history.FileName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting OneDrive backup");
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync(BackupProvider provider, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<OneDriveConfig>(provider.Configuration);
            if (config == null || string.IsNullOrEmpty(config.ClientId))
                return false;

            var accessToken = await GetAccessTokenAsync(config);
            if (string.IsNullOrEmpty(accessToken)) return false;

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // Retrieve drive info
            var response = await httpClient.GetAsync("https://graph.microsoft.com/v1.0/me/drive");

            if (!response.IsSuccessStatusCode)
                return false;

            // Create folder if it does not exist
            if (!string.IsNullOrEmpty(config.FolderPath))
            {
                await EnsureFolderExistsAsync(httpClient, config.FolderPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OneDrive connection test failed");
            return false;
        }
    }

    private async Task<string?> GetAccessTokenAsync(OneDriveConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient();

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["refresh_token"] = config.RefreshToken,
                ["grant_type"] = "refresh_token"
            });

            var response = await httpClient.PostAsync(
                "https://login.microsoftonline.com/common/oauth2/v2.0/token",
                content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to refresh OneDrive token: {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json);

            return tokenResponse?.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obtaining OneDrive access token");
            return null;
        }
    }

    private async Task<string?> CreateUploadSessionAsync(HttpClient httpClient, string itemPath, CancellationToken ct)
    {
        try
        {
            var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(
                $"https://graph.microsoft.com/v1.0{itemPath}:/createUploadSession",
                content,
                ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            var sessionResponse = JsonSerializer.Deserialize<UploadSessionResponse>(json);

            return sessionResponse?.UploadUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating upload session");
            return null;
        }
    }

    private async Task EnsureFolderExistsAsync(HttpClient httpClient, string folderPath, CancellationToken cancellationToken = default)
    {
        var folderParts = folderPath.Trim('/').Split('/');
        string currentPath = "/me/drive/root";

        foreach (var folderName in folderParts)
        {
            currentPath += $":/{folderName}";

            var checkResponse = await httpClient.GetAsync($"https://graph.microsoft.com/v1.0{currentPath}");

            if (!checkResponse.IsSuccessStatusCode)
            {
                // Create folder
                var parentPath = currentPath.Substring(0, currentPath.LastIndexOf(':'));
                var createContent = new StringContent(
                    $"{{\"name\":\"{folderName}\",\"folder\":{{}}}}",
                    System.Text.Encoding.UTF8,
                    "application/json");

                await httpClient.PostAsync(
                    $"https://graph.microsoft.com/v1.0{parentPath}/children",
                    createContent);

                _logger.LogInformation("Created OneDrive folder: {FolderName}", folderName);
            }
        }
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }

    private class DriveItemResponse
    {
        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    private class UploadSessionResponse
    {
        [JsonPropertyName("uploadUrl")]
        public string? UploadUrl { get; set; }
    }
}
