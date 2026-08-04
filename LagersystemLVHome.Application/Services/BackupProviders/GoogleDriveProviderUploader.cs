using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using LagersystemLVHome.Domain.Models;
using System.Text.Json;
using GoogleFile = Google.Apis.Drive.v3.Data.File;

namespace LagersystemLVHome.Application.Services.BackupProviders;

/// <summary>
/// Google Drive backup provider implementation.
/// </summary>
public sealed class GoogleDriveProviderUploader : IBackupProviderUploader
{
    private readonly ILogger<GoogleDriveProviderUploader> _logger;

    public BackupProviderType SupportedProviderType => BackupProviderType.GoogleDrive;

    public GoogleDriveProviderUploader(ILogger<GoogleDriveProviderUploader> logger)
    {
        _logger = logger;
    }

    public async Task<bool> UploadAsync(BackupProvider provider, string filePath, CancellationToken ct = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<GoogleDriveConfig>(provider.Configuration);
            if (config == null || string.IsNullOrEmpty(config.ClientId))
            {
                _logger.LogError("Google Drive config missing or invalid");
                return false;
            }

            var service = await CreateDriveServiceAsync(config);

            var fileMetadata = new GoogleFile
            {
                Name = Path.GetFileName(filePath),
                MimeType = "application/zip"
            };

            // Set folder ID if available
            if (!string.IsNullOrEmpty(config.FolderId))
            {
                fileMetadata.Parents = new List<string> { config.FolderId };
            }

            await using var stream = System.IO.File.OpenRead(filePath);
            var request = service.Files.Create(fileMetadata, stream, "application/zip");
            request.Fields = "id, name, size, createdTime";

            var uploadProgress = await request.UploadAsync(ct);

            if (uploadProgress.Status == UploadStatus.Completed)
            {
                var file = request.ResponseBody;
                _logger.LogInformation("Backup uploaded to Google Drive: {FolderName}/{FileName} (ID: {FileId})",
                    config.FolderName, file.Name, file.Id);
                return true;
            }

            _logger.LogError("Google Drive upload failed with status: {Status}", uploadProgress.Status);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload to Google Drive");
            return false;
        }
    }

    public async Task<bool> ValidateAsync(BackupHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<GoogleDriveConfig>(history.BackupProvider.Configuration);
            if (config == null) return false;

            var service = await CreateDriveServiceAsync(config);

            // Search for file
            var request = service.Files.List();
            request.Q = $"name='{history.FileName}' and trashed=false";
            request.Fields = "files(id, name, size)";

            if (!string.IsNullOrEmpty(config.FolderId))
            {
                request.Q += $" and '{config.FolderId}' in parents";
            }

            var result = await request.ExecuteAsync();

            if (result.Files?.Any() == true)
            {
                var file = result.Files.First();
                if (file.Size == history.SizeBytes)
                {
                    _logger.LogInformation("Backup validated in Google Drive: {FileName} (ID: {FileId})",
                        file.Name, file.Id);
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Google Drive backup");
            return false;
        }
    }

    public async Task<bool> DeleteAsync(BackupHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<GoogleDriveConfig>(history.BackupProvider.Configuration);
            if (config == null) return false;

            var service = await CreateDriveServiceAsync(config);

            // Search for file
            var listRequest = service.Files.List();
            listRequest.Q = $"name='{history.FileName}' and trashed=false";
            listRequest.Fields = "files(id, name)";

            if (!string.IsNullOrEmpty(config.FolderId))
            {
                listRequest.Q += $" and '{config.FolderId}' in parents";
            }

            var result = await listRequest.ExecuteAsync();

            if (result.Files?.Any() == true)
            {
                var file = result.Files.First();
                await service.Files.Delete(file.Id).ExecuteAsync();

                _logger.LogInformation("Deleted backup from Google Drive: {FileName} (ID: {FileId})",
                    file.Name, file.Id);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Google Drive backup");
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync(BackupProvider provider, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<GoogleDriveConfig>(provider.Configuration);
            if (config == null || string.IsNullOrEmpty(config.ClientId))
                return false;

            var service = await CreateDriveServiceAsync(config);

            // Retrieve account info (includes storage details)
            var about = await service.About.Get().ExecuteAsync();

            // Create folder if name is specified but no ID
            if (!string.IsNullOrEmpty(config.FolderName) && string.IsNullOrEmpty(config.FolderId))
            {
                // Search for folder
                var request = service.Files.List();
                request.Q = $"name='{config.FolderName}' and mimeType='application/vnd.google-apps.folder' and trashed=false";
                request.Fields = "files(id, name)";

                var result = await request.ExecuteAsync();
                if (!result.Files.Any())
                {
                    // Create folder
                    var folderMetadata = new GoogleFile
                    {
                        Name = config.FolderName,
                        MimeType = "application/vnd.google-apps.folder"
                    };

                    var folder = await service.Files.Create(folderMetadata).ExecuteAsync();
                    _logger.LogInformation("Created Google Drive folder: {FolderName} (ID: {FolderId})",
                        folder.Name, folder.Id);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Drive connection test failed");
            return false;
        }
    }

    private async Task<DriveService> CreateDriveServiceAsync(GoogleDriveConfig config, CancellationToken cancellationToken = default)
    {
        var tokenResponse = new TokenResponse
        {
            RefreshToken = config.RefreshToken
        };

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = config.ClientId,
                ClientSecret = config.ClientSecret
            },
            Scopes = new[] { DriveService.Scope.DriveFile }
        });

        var credential = new UserCredential(flow, "user", tokenResponse);

        // Refresh token if needed
        await credential.RefreshTokenAsync(CancellationToken.None);

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "LagerSystem Backup"
        });
    }
}
