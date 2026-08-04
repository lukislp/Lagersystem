using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using LagersystemLVHome.Domain.Models;
using System.Text.Json;

namespace LagersystemLVHome.Application.Services.BackupProviders;

/// <summary>
/// AWS S3 backup provider implementation.
/// </summary>
public sealed class AwsS3ProviderUploader : IBackupProviderUploader
{
    private readonly ILogger<AwsS3ProviderUploader> _logger;

    public BackupProviderType SupportedProviderType => BackupProviderType.AWSS3;

    public AwsS3ProviderUploader(ILogger<AwsS3ProviderUploader> logger)
    {
        _logger = logger;
    }

    public async Task<bool> UploadAsync(BackupProvider provider, string filePath, CancellationToken ct = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AwsS3Config>(provider.Configuration);
            if (config == null || string.IsNullOrEmpty(config.BucketName))
            {
                _logger.LogError("AWS S3 config missing or invalid");
                return false;
            }

            var s3Client = new AmazonS3Client(
                config.AccessKey,
                config.SecretKey,
                RegionEndpoint.GetBySystemName(config.Region)
            );

            var fileName = Path.GetFileName(filePath);

            var putRequest = new PutObjectRequest
            {
                BucketName = config.BucketName,
                Key = fileName,
                FilePath = filePath,
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            };

            var response = await s3Client.PutObjectAsync(putRequest, ct);

            if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                _logger.LogInformation("Backup uploaded to AWS S3: {Bucket}/{FileName}",
                    config.BucketName, fileName);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload to AWS S3");
            return false;
        }
    }

    public async Task<bool> ValidateAsync(BackupHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AwsS3Config>(history.BackupProvider.Configuration);
            if (config == null) return false;

            var s3Client = new AmazonS3Client(
                config.AccessKey,
                config.SecretKey,
                RegionEndpoint.GetBySystemName(config.Region)
            );

            var metadataRequest = new GetObjectMetadataRequest
            {
                BucketName = config.BucketName,
                Key = history.FileName
            };

            var metadata = await s3Client.GetObjectMetadataAsync(metadataRequest);

            if (metadata.ContentLength == history.SizeBytes)
            {
                _logger.LogInformation("Backup validated in AWS S3: {Bucket}/{FileName}",
                    config.BucketName, history.FileName);
                return true;
            }

            return false;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Backup not found in AWS S3: {FileName}", history.FileName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating AWS S3 backup");
            return false;
        }
    }

    public async Task<bool> DeleteAsync(BackupHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AwsS3Config>(history.BackupProvider.Configuration);
            if (config == null) return false;

            var s3Client = new AmazonS3Client(
                config.AccessKey,
                config.SecretKey,
                RegionEndpoint.GetBySystemName(config.Region)
            );

            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = config.BucketName,
                Key = history.FileName
            };

            var response = await s3Client.DeleteObjectAsync(deleteRequest);

            if (response.HttpStatusCode == System.Net.HttpStatusCode.NoContent)
            {
                _logger.LogInformation("Deleted backup from AWS S3: {Bucket}/{FileName}",
                    config.BucketName, history.FileName);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting AWS S3 backup");
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync(BackupProvider provider, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AwsS3Config>(provider.Configuration);
            if (config == null || string.IsNullOrEmpty(config.BucketName))
                return false;

            var s3Client = new AmazonS3Client(
                config.AccessKey,
                config.SecretKey,
                RegionEndpoint.GetBySystemName(config.Region)
            );

            // Verify bucket exists
            var bucketLocation = await s3Client.GetBucketLocationAsync(config.BucketName);

            // Upload a small test file
            var testKey = $"test_{Guid.NewGuid()}.txt";
            var putRequest = new PutObjectRequest
            {
                BucketName = config.BucketName,
                Key = testKey,
                ContentBody = "Test"
            };

            await s3Client.PutObjectAsync(putRequest);

            // Cleanup
            await s3Client.DeleteObjectAsync(config.BucketName, testKey);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AWS S3 connection test failed");
            return false;
        }
    }
}
