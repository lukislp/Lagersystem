using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services.BackupProviders;

/// <summary>
/// Cloudflare R2 backup provider implementation (S3-compatible).
/// Simple configuration using Access Key + Secret Key.
/// 10 GB free storage + 10 million requests/month, no egress costs.
/// </summary>
public sealed class CloudflareR2ProviderUploader : IBackupProviderUploader
{
    private readonly ILogger<CloudflareR2ProviderUploader> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public BackupProviderType SupportedProviderType => BackupProviderType.CloudflareR2;

    public CloudflareR2ProviderUploader(
        ILogger<CloudflareR2ProviderUploader> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> UploadAsync(BackupProvider provider, string filePath, CancellationToken ct = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<CloudflareR2Config>(provider.Configuration);
            if (config == null || string.IsNullOrEmpty(config.AccessKeyId))
            {
                _logger.LogError("Cloudflare R2 config missing or invalid");
                return false;
            }

            var fileName = Path.GetFileName(filePath);
            var objectKey = string.IsNullOrEmpty(config.Prefix)
                ? fileName
                : $"{config.Prefix.TrimEnd('/')}/{fileName}";

            await using var stream = File.OpenRead(filePath);

            using var httpClient = _httpClientFactory.CreateClient();

            // S3-compatible PUT request
            var url = $"{config.Endpoint}/{config.BucketName}/{objectKey}";
            var dateTime = DateTime.UtcNow;

            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Headers.ContentLength = stream.Length;

            // AWS Signature V4 for authentication
            var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = content
            };

            SignRequest(request, config, dateTime, "PUT", objectKey, stream.Length);

            var response = await httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Backup uploaded to Cloudflare R2: {BucketName}/{ObjectKey} ({Size} bytes)",
                    config.BucketName, objectKey, stream.Length);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Cloudflare R2 upload failed: {StatusCode} - {Error}",
                response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload to Cloudflare R2");
            return false;
        }
    }

    public async Task<bool> ValidateAsync(BackupHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<CloudflareR2Config>(history.BackupProvider.Configuration);
            if (config == null) return false;

            var objectKey = string.IsNullOrEmpty(config.Prefix)
                ? history.FileName
                : $"{config.Prefix.TrimEnd('/')}/{history.FileName}";

            using var httpClient = _httpClientFactory.CreateClient();

            // HEAD request to fetch metadata without downloading
            var url = $"{config.Endpoint}/{config.BucketName}/{objectKey}";
            var dateTime = DateTime.UtcNow;

            var request = new HttpRequestMessage(HttpMethod.Head, url);
            SignRequest(request, config, dateTime, "HEAD", objectKey, 0);

            var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var contentLength = response.Content.Headers.ContentLength;

                if (contentLength == history.SizeBytes)
                {
                    _logger.LogInformation("Backup validated in Cloudflare R2: {BucketName}/{ObjectKey}",
                        config.BucketName, objectKey);
                    return true;
                }

                _logger.LogWarning("Backup size mismatch: Expected {Expected}, Got {Actual}",
                    history.SizeBytes, contentLength);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Cloudflare R2 backup");
            return false;
        }
    }

    public async Task<bool> DeleteAsync(BackupHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<CloudflareR2Config>(history.BackupProvider.Configuration);
            if (config == null) return false;

            var objectKey = string.IsNullOrEmpty(config.Prefix)
                ? history.FileName
                : $"{config.Prefix.TrimEnd('/')}/{history.FileName}";

            using var httpClient = _httpClientFactory.CreateClient();

            var url = $"{config.Endpoint}/{config.BucketName}/{objectKey}";
            var dateTime = DateTime.UtcNow;

            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            SignRequest(request, config, dateTime, "DELETE", objectKey, 0);

            var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                _logger.LogInformation("Deleted backup from Cloudflare R2: {BucketName}/{ObjectKey}",
                    config.BucketName, objectKey);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Cloudflare R2 backup");
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync(BackupProvider provider, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = JsonSerializer.Deserialize<CloudflareR2Config>(provider.Configuration);
            if (config == null || string.IsNullOrEmpty(config.AccessKeyId))
                return false;

            using var httpClient = _httpClientFactory.CreateClient();

            // List bucket to verify access
            var url = $"{config.Endpoint}/{config.BucketName}?max-keys=1";
            var dateTime = DateTime.UtcNow;

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            SignRequest(request, config, dateTime, "GET", "", 0, "max-keys=1");

            var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Cloudflare R2 connection test successful: {BucketName}",
                    config.BucketName);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Cloudflare R2 connection test failed: {StatusCode} - {Error}",
                response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloudflare R2 connection test failed");
            return false;
        }
    }

    /// <summary>
    /// Signs a request using AWS Signature Version 4.
    /// </summary>
    private void SignRequest(
        HttpRequestMessage request,
        CloudflareR2Config config,
        DateTime dateTime,
        string method,
        string objectKey,
        long contentLength,
        string? queryString = null)
    {
        var region = "auto"; // Cloudflare R2 uses "auto" as region
        var service = "s3";

        var dateStamp = dateTime.ToString("yyyyMMdd");
        var amzDate = dateTime.ToString("yyyyMMddTHHmmssZ");

        // 1. Canonical Request
        var canonicalUri = $"/{config.BucketName}/{objectKey}";
        var canonicalQueryString = queryString ?? "";

        var canonicalHeaders =
            $"host:{new Uri(config.Endpoint).Host}\n" +
            $"x-amz-content-sha256:UNSIGNED-PAYLOAD\n" +
            $"x-amz-date:{amzDate}\n";

        var signedHeaders = "host;x-amz-content-sha256;x-amz-date";

        var canonicalRequest =
            $"{method}\n" +
            $"{canonicalUri}\n" +
            $"{canonicalQueryString}\n" +
            $"{canonicalHeaders}\n" +
            $"{signedHeaders}\n" +
            "UNSIGNED-PAYLOAD";

        // 2. String to Sign
        var credentialScope = $"{dateStamp}/{region}/{service}/aws4_request";
        var stringToSign =
            "AWS4-HMAC-SHA256\n" +
            $"{amzDate}\n" +
            $"{credentialScope}\n" +
            HashSHA256(canonicalRequest);

        // 3. Signing Key
        var signingKey = GetSigningKey(config.SecretAccessKey, dateStamp, region, service);
        var signature = HmacSHA256Hex(signingKey, stringToSign);

        // 4. Authorization Header
        var authorization =
            $"AWS4-HMAC-SHA256 " +
            $"Credential={config.AccessKeyId}/{credentialScope}," +
            $"SignedHeaders={signedHeaders}," +
            $"Signature={signature}";

        // Set headers
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.Add("x-amz-date", amzDate);
        request.Headers.Add("x-amz-content-sha256", "UNSIGNED-PAYLOAD");

        if (contentLength > 0)
        {
            request.Content!.Headers.ContentLength = contentLength;
        }
    }

    private static string HashSHA256(string text)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = sha256.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static byte[] GetSigningKey(string secretKey, string dateStamp, string region, string service)
    {
        var kDate = HmacSHA256($"AWS4{secretKey}", dateStamp);
        var kRegion = HmacSHA256(kDate, region);
        var kService = HmacSHA256(kRegion, service);
        var kSigning = HmacSHA256(kService, "aws4_request");
        return kSigning;
    }

    private static byte[] HmacSHA256(string key, string data)
    {
        return HmacSHA256(Encoding.UTF8.GetBytes(key), data);
    }

    private static byte[] HmacSHA256(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string HmacSHA256Hex(byte[] key, string data)
    {
        var hash = HmacSHA256(key, data);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
