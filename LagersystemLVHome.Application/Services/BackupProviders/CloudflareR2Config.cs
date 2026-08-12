namespace LagersystemLVHome.Application.Services.BackupProviders;

/// <summary>
/// Cloudflare R2 backup configuration.
/// </summary>
public sealed class CloudflareR2Config
{
    /// <summary>
    /// R2 Access Key ID (e.g. "a1b2c3d4e5f6g7h8i9j0").
    /// </summary>
    public string AccessKeyId { get; set; } = "";

    /// <summary>
    /// R2 Secret Access Key.
    /// </summary>
    public string SecretAccessKey { get; set; } = "";

    /// <summary>
    /// R2 Bucket Name (e.g. "lagersystem-backups").
    /// </summary>
    public string BucketName { get; set; } = "";

    /// <summary>
    /// Cloudflare Account ID (e.g. "1234567890abcdef1234567890abcdef").
    /// </summary>
    public string AccountId { get; set; } = "";

    /// <summary>
    /// Optional custom endpoint (auto-generated from Account ID if not set).
    /// </summary>
    public string? CustomEndpoint { get; set; }

    /// <summary>
    /// Optional prefix for backup files (e.g. "backups/").
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Maximum number of backups to retain (oldest are deleted).
    /// Must be named "MaxBackups" for consistency with local providers.
    /// Default: 30.
    /// </summary>
    public int MaxBackups { get; set; } = 30;

    /// <summary>
    /// Auto-generated R2 endpoint.
    /// </summary>
    public string Endpoint =>
        CustomEndpoint ?? $"https://{AccountId}.r2.cloudflarestorage.com";
}
