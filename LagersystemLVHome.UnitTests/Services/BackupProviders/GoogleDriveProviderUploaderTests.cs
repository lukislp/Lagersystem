using LagersystemLVHome.Application.Services.BackupProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace LagersystemLVHome.UnitTests.Services.BackupProviders;

/// <summary>
/// Covers <see cref="GoogleDriveProviderUploader"/>.
///
/// This provider builds a real <c>Google.Apis.Drive.v3.DriveService</c> internally with no
/// injectable HTTP seam - <c>CreateDriveServiceAsync</c> always calls
/// <c>credential.RefreshTokenAsync</c>, which makes a genuine network request to Google's
/// OAuth token endpoint. That call fires unconditionally the moment a non-null config is
/// deserialized (Validate/Delete/TestConnection only guard on <c>config == null</c>, not on
/// any credential field), so exercising the "valid config" path here would mean a real,
/// slow, non-deterministic network call in every test run - unavailable/undesirable in this
/// sandbox, equivalent to the pg_dump/external-process seam called out in the task brief.
/// Coverage is therefore intentionally limited to the branches that return *before*
/// <c>CreateDriveServiceAsync</c> is ever reached: a null-deserialized config, malformed
/// JSON (throws during <c>Deserialize</c> itself), and (for Upload/TestConnection, which
/// explicitly check it) a missing ClientId.
/// </summary>
public sealed class GoogleDriveProviderUploaderTests
{
    private static GoogleDriveProviderUploader CreateSut()
        => new(NullLogger<GoogleDriveProviderUploader>.Instance);

    private static BackupProvider CreateProvider(string configuration)
        => new() { Name = "GD", Type = BackupProviderType.GoogleDrive, Configuration = configuration };

    private static string ConfigWithoutClientId() => JsonSerializer.Serialize(new GoogleDriveConfig { ClientId = "" });

    [Fact]
    public void SupportedProviderType_IsGoogleDrive()
    {
        CreateSut().SupportedProviderType.Should().Be(BackupProviderType.GoogleDrive);
    }

    // ----- UploadAsync -----

    [Fact]
    public async Task UploadAsync_NullConfig_ReturnsFalse()
    {
        (await CreateSut().UploadAsync(CreateProvider("null"), "any-path.zip")).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_MissingClientId_ReturnsFalse()
    {
        (await CreateSut().UploadAsync(CreateProvider(ConfigWithoutClientId()), "any-path.zip")).Should().BeFalse();
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
    public async Task TestConnectionAsync_MissingClientId_ReturnsFalse()
    {
        (await CreateSut().TestConnectionAsync(CreateProvider(ConfigWithoutClientId()))).Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectionAsync_InvalidJson_ReturnsFalse()
    {
        (await CreateSut().TestConnectionAsync(CreateProvider("garbage"))).Should().BeFalse();
    }
}
