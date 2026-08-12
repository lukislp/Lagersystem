using LagersystemLVHome.Application.Services.BackupProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace LagersystemLVHome.UnitTests.Services.BackupProviders;

/// <summary>
/// Covers <see cref="AwsS3ProviderUploader"/>.
///
/// This provider constructs a real <c>AmazonS3Client</c> internally with no injectable HTTP
/// seam. <c>Validate</c>/<c>Delete</c> only guard on <c>config == null</c> before creating
/// the client and issuing a genuine S3 API call (metadata lookup / delete), so any test
/// using a non-null config - even with empty/fake credentials - would trigger a real,
/// slow, non-deterministic network round-trip. That is unavailable/undesirable in this
/// sandbox, equivalent to the pg_dump/external-process seam called out in the task brief.
/// Coverage is therefore limited to the branches that return *before* any S3 client or
/// network call: a null-deserialized config, malformed JSON (throws during
/// <c>Deserialize</c> itself), and (for Upload/TestConnection, which explicitly check it)
/// a missing BucketName.
/// </summary>
public sealed class AwsS3ProviderUploaderTests
{
    private static AwsS3ProviderUploader CreateSut()
        => new(NullLogger<AwsS3ProviderUploader>.Instance);

    private static BackupProvider CreateProvider(string configuration)
        => new() { Name = "S3", Type = BackupProviderType.AWSS3, Configuration = configuration };

    private static string ConfigWithoutBucketName() => JsonSerializer.Serialize(new AwsS3Config { BucketName = "" });

    [Fact]
    public void SupportedProviderType_IsAWSS3()
    {
        CreateSut().SupportedProviderType.Should().Be(BackupProviderType.AWSS3);
    }

    // ----- UploadAsync -----

    [Fact]
    public async Task UploadAsync_NullConfig_ReturnsFalse()
    {
        (await CreateSut().UploadAsync(CreateProvider("null"), "any-path.zip")).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_MissingBucketName_ReturnsFalse()
    {
        (await CreateSut().UploadAsync(CreateProvider(ConfigWithoutBucketName()), "any-path.zip")).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_InvalidJson_ReturnsFalse()
    {
        (await CreateSut().UploadAsync(CreateProvider("not-json"), "any-path.zip")).Should().BeFalse();
    }

    // ----- ValidateAsync -----

    [Fact]
    public async Task ValidateAsync_NullConfig_ReturnsFalse()
    {
        var history = new BackupHistory { FileName = "x", BackupProvider = CreateProvider("null") };
        (await CreateSut().ValidateAsync(history)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_InvalidJson_ReturnsFalse()
    {
        var history = new BackupHistory { FileName = "x", BackupProvider = CreateProvider("garbage") };
        (await CreateSut().ValidateAsync(history)).Should().BeFalse();
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_NullConfig_ReturnsFalse()
    {
        var history = new BackupHistory { FileName = "x", BackupProvider = CreateProvider("null") };
        (await CreateSut().DeleteAsync(history)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_InvalidJson_ReturnsFalse()
    {
        var history = new BackupHistory { FileName = "x", BackupProvider = CreateProvider("garbage") };
        (await CreateSut().DeleteAsync(history)).Should().BeFalse();
    }

    // ----- TestConnectionAsync -----

    [Fact]
    public async Task TestConnectionAsync_NullConfig_ReturnsFalse()
    {
        (await CreateSut().TestConnectionAsync(CreateProvider("null"))).Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectionAsync_MissingBucketName_ReturnsFalse()
    {
        (await CreateSut().TestConnectionAsync(CreateProvider(ConfigWithoutBucketName()))).Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectionAsync_InvalidJson_ReturnsFalse()
    {
        (await CreateSut().TestConnectionAsync(CreateProvider("garbage"))).Should().BeFalse();
    }
}
