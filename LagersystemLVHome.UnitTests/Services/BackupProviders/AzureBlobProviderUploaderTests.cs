using LagersystemLVHome.Application.Services.BackupProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace LagersystemLVHome.UnitTests.Services.BackupProviders;

/// <summary>
/// Covers <see cref="AzureBlobProviderUploader"/>.
///
/// This provider constructs a real <c>BlobServiceClient</c> internally with no injectable
/// HTTP seam. <c>Validate</c>/<c>Delete</c> only guard on <c>config == null</c> before
/// creating the client and issuing a genuine blob-storage API call, so any test using a
/// non-null config - even with an empty/fake connection string - risks a real network
/// round-trip (or a client-construction failure that depends on Azure SDK internals we
/// should not couple a unit test to). That is unavailable/undesirable in this sandbox,
/// equivalent to the pg_dump/external-process seam called out in the task brief.
/// Coverage is therefore limited to the branches that return *before* any client or
/// network call: a null-deserialized config, malformed JSON (throws during
/// <c>Deserialize</c> itself), and (for Upload/TestConnection, which explicitly check it)
/// a missing ConnectionString.
/// </summary>
public sealed class AzureBlobProviderUploaderTests
{
    private static AzureBlobProviderUploader CreateSut()
        => new(NullLogger<AzureBlobProviderUploader>.Instance);

    private static BackupProvider CreateProvider(string configuration)
        => new() { Name = "Azure", Type = BackupProviderType.AzureBlob, Configuration = configuration };

    private static string ConfigWithoutConnectionString() => JsonSerializer.Serialize(new AzureBlobConfig { ConnectionString = "" });

    [Fact]
    public void SupportedProviderType_IsAzureBlob()
    {
        CreateSut().SupportedProviderType.Should().Be(BackupProviderType.AzureBlob);
    }

    // ----- UploadAsync -----

    [Fact]
    public async Task UploadAsync_NullConfig_ReturnsFalse()
    {
        (await CreateSut().UploadAsync(CreateProvider("null"), "any-path.zip")).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_MissingConnectionString_ReturnsFalse()
    {
        (await CreateSut().UploadAsync(CreateProvider(ConfigWithoutConnectionString()), "any-path.zip")).Should().BeFalse();
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
    public async Task TestConnectionAsync_MissingConnectionString_ReturnsFalse()
    {
        (await CreateSut().TestConnectionAsync(CreateProvider(ConfigWithoutConnectionString()))).Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectionAsync_InvalidJson_ReturnsFalse()
    {
        (await CreateSut().TestConnectionAsync(CreateProvider("garbage"))).Should().BeFalse();
    }
}
