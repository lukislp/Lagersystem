using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using LagersystemLVHome.Domain.Models;
using System.Text.Json;

namespace LagersystemLVHome.Application.Services.BackupProviders;

/// <summary>
/// Azure Blob Storage backup provider implementation.
/// </summary>
public sealed class AzureBlobProviderUploader : IBackupProviderUploader
{
    private readonly ILogger<AzureBlobProviderUploader> _logger;

    public BackupProviderType SupportedProviderType => BackupProviderType.AzureBlob;

    public AzureBlobProviderUploader(ILogger<AzureBlobProviderUploader> logger)
    {
        _logger = logger;
    }

    public async Task<bool> UploadAsync(BackupProvider provider, string filePath, CancellationToken ct = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AzureBlobConfig>(provider.Configuration);
            if (config == null || string.IsNullOrEmpty(config.ConnectionString))
            {
                _logger.LogError("Azure Blob config missing or invalid");
                return false;
            }

            var blobServiceClient = new BlobServiceClient(config.ConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(config.ContainerName);

            // Create container if it does not exist
            await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

            var fileName = Path.GetFileName(filePath);
            var blobClient = containerClient.GetBlobClient(fileName);

            // Upload with overwrite
            await using var fileStream = File.OpenRead(filePath);
            await blobClient.UploadAsync(fileStream, overwrite: true, cancellationToken: ct);

            _logger.LogInformation("Backup uploaded to Azure Blob: {Container}/{FileName}",
                config.ContainerName, fileName);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload to Azure Blob");
            return false;
        }
    }

    public async Task<bool> ValidateAsync(BackupHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AzureBlobConfig>(history.BackupProvider.Configuration);
            if (config == null) return false;

            var blobServiceClient = new BlobServiceClient(config.ConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(config.ContainerName);
            var blobClient = containerClient.GetBlobClient(history.FileName);

            var exists = await blobClient.ExistsAsync();
            if (!exists.Value) return false;

            // Check size
            var properties = await blobClient.GetPropertiesAsync();
            if (properties.Value.ContentLength == history.SizeBytes)
            {
                _logger.LogInformation("Backup validated in Azure Blob: {Container}/{FileName}",
                    config.ContainerName, history.FileName);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Azure Blob backup");
            return false;
        }
    }

    public async Task<bool> DeleteAsync(BackupHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AzureBlobConfig>(history.BackupProvider.Configuration);
            if (config == null) return false;

            var blobServiceClient = new BlobServiceClient(config.ConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(config.ContainerName);
            var blobClient = containerClient.GetBlobClient(history.FileName);

            var response = await blobClient.DeleteIfExistsAsync();
            if (response.Value)
            {
                _logger.LogInformation("Deleted backup from Azure Blob: {Container}/{FileName}",
                    config.ContainerName, history.FileName);
            }

            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Azure Blob backup");
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync(BackupProvider provider, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AzureBlobConfig>(provider.Configuration);
            if (config == null || string.IsNullOrEmpty(config.ConnectionString))
                return false;

            var blobServiceClient = new BlobServiceClient(config.ConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(config.ContainerName);

            // Create container if it does not exist
            await containerClient.CreateIfNotExistsAsync();

            // Upload a small test file
            var testBlobName = $"test_{Guid.NewGuid()}.txt";
            var testBlobClient = containerClient.GetBlobClient(testBlobName);

            await using var testStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Test"));
            await testBlobClient.UploadAsync(testStream, overwrite: true);

            // Cleanup
            await testBlobClient.DeleteIfExistsAsync();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Blob connection test failed");
            return false;
        }
    }
}
