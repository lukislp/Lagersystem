using LagersystemLVHome.Application.Services.BackupProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace LagersystemLVHome.UnitTests.Services.BackupProviders;

/// <summary>
/// Covers <see cref="NetworkShareProviderUploader"/>.
///
/// Real Win32 networking (WNetAddConnection2/WNetCancelConnection2, P/Invoke into mpr.dll)
/// is intentionally never exercised with real credentials/hosts here - same reasoning as
/// KeyBackupServiceTests: it would hang on DNS resolution or require a real reachable SMB
/// share. Every test either omits credentials (production code then skips the connect
/// step entirely and just uses the current Windows user) or supplies a single-segment
/// "UNC" path with credentials, which makes ConnectToNetworkShare's own format guard
/// (parts.Length &lt; 2) return false *before* any P/Invoke call - a real branch, exercised
/// deterministically. Local directories (not real shares) stand in for "UNC paths" since
/// the class only ever calls plain System.IO APIs against whatever path string it is given.
/// </summary>
public sealed class NetworkShareProviderUploaderTests : IDisposable
{
    private readonly List<string> _tempDirs = new();
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _relativeDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { /* best effort */ }
        }
        foreach (var relDir in _relativeDirs)
        {
            var full = Path.Combine(Directory.GetCurrentDirectory(), relDir);
            try { if (Directory.Exists(full)) Directory.Delete(full, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netshare_uploader_" + Guid.NewGuid());
        _tempDirs.Add(dir);
        return dir;
    }

    private string NewSourceFile(string content = "backup-content")
    {
        var path = Path.Combine(Path.GetTempPath(), "netshare_uploader_src_" + Guid.NewGuid() + ".zip");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private static NetworkShareProviderUploader CreateSut()
        => new(NullLogger<NetworkShareProviderUploader>.Instance);

    private static BackupProvider CreateProvider(string configuration)
        => new() { Name = "P", Type = BackupProviderType.NetworkShare, Configuration = configuration };

    // ----- SupportedProviderType -----

    [Fact]
    public void SupportedProviderType_IsNetworkShare()
    {
        CreateSut().SupportedProviderType.Should().Be(BackupProviderType.NetworkShare);
    }

    // ----- UploadAsync -----

    [Fact]
    public async Task UploadAsync_NoCredentials_SkipsConnectAndSucceeds()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        var source = NewSourceFile("payload");
        var config = JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new() { new NetworkSharePath { UncPath = target, Enabled = true } }
        });

        var result = await sut.UploadAsync(CreateProvider(config), source);

        result.Should().BeTrue();
        File.Exists(Path.Combine(target, Path.GetFileName(source))).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_NoEnabledPaths_ReturnsFalse()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        var config = JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new() { new NetworkSharePath { UncPath = target, Enabled = false } }
        });

        (await sut.UploadAsync(CreateProvider(config), NewSourceFile())).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_NullConfig_ReturnsFalse()
    {
        var sut = CreateSut();
        (await sut.UploadAsync(CreateProvider("null"), NewSourceFile())).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_InvalidJson_ReturnsFalse()
    {
        var sut = CreateSut();
        (await sut.UploadAsync(CreateProvider("not-json"), NewSourceFile())).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_DisabledPathAmongEnabled_IsSkipped()
    {
        var sut = CreateSut();
        var enabledTarget = NewTempDir();
        var disabledTarget = NewTempDir();
        var source = NewSourceFile();
        var config = JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new()
            {
                new NetworkSharePath { UncPath = disabledTarget, Enabled = false },
                new NetworkSharePath { UncPath = enabledTarget, Enabled = true }
            }
        });

        var result = await sut.UploadAsync(CreateProvider(config), source);

        result.Should().BeTrue();
        Directory.Exists(disabledTarget).Should().BeFalse("a disabled path must never be touched");
        File.Exists(Path.Combine(enabledTarget, Path.GetFileName(source))).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_CreateDateAndWeekSubfolders_PlacesFileCorrectly()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        var source = NewSourceFile();
        var config = JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new() { new NetworkSharePath { UncPath = target, Enabled = true } },
            CreateDateSubfolders = true,
            CreateWeekSubfolders = true
        });

        var result = await sut.UploadAsync(CreateProvider(config), source);

        result.Should().BeTrue();
        var now = DateTime.UtcNow;
        var dateFolder = now.ToString("yyyy-MM");
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var weekNumber = culture.Calendar.GetWeekOfYear(now, culture.DateTimeFormat.CalendarWeekRule, culture.DateTimeFormat.FirstDayOfWeek);
        var weekFolder = $"Woche-{weekNumber:D2}";
        File.Exists(Path.Combine(target, dateFolder, weekFolder, Path.GetFileName(source))).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_InvalidUncFormatWithCredentials_LogsWarningButStillAttemptsUpload()
    {
        // Single-segment "UNC" path makes ConnectToNetworkShare's own guard (parts.Length < 2)
        // return false before any P/Invoke call - production code logs a warning and tries
        // the upload anyway (using the given path as a plain local directory in this test).
        var sut = CreateSut();
        var relativeName = "unittest_share_" + Guid.NewGuid().ToString("N");
        _relativeDirs.Add(relativeName);
        var source = NewSourceFile();
        var config = JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new() { new NetworkSharePath { UncPath = relativeName, Enabled = true } },
            Username = "someuser",
            Password = "somepass"
        });

        var result = await sut.UploadAsync(CreateProvider(config), source);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_OnePathInvalid_OtherSucceeds_ReturnsTrue()
    {
        var sut = CreateSut();
        var badTarget = "C:\\invalid|path?<>";
        var goodTarget = NewTempDir();
        var source = NewSourceFile();
        var config = JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new()
            {
                new NetworkSharePath { UncPath = badTarget, Enabled = true },
                new NetworkSharePath { UncPath = goodTarget, Enabled = true }
            }
        });

        var result = await sut.UploadAsync(CreateProvider(config), source);

        result.Should().BeTrue();
        File.Exists(Path.Combine(goodTarget, Path.GetFileName(source))).Should().BeTrue();
    }

    // ----- ValidateAsync -----

    [Fact]
    public async Task ValidateAsync_FileExistsWithMatchingSize_ReturnsTrue()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        Directory.CreateDirectory(target);
        var fileName = "backup1.zip";
        File.WriteAllText(Path.Combine(target, fileName), "12345");
        var config = JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new() { new NetworkSharePath { UncPath = target, Enabled = true } }
        });
        var history = new BackupHistory { FileName = fileName, SizeBytes = 5, BackupProvider = CreateProvider(config) };

        (await sut.ValidateAsync(history)).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_SizeMismatch_ReturnsFalse()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        Directory.CreateDirectory(target);
        var fileName = "backup1.zip";
        File.WriteAllText(Path.Combine(target, fileName), "12345");
        var config = JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new() { new NetworkSharePath { UncPath = target, Enabled = true } }
        });
        var history = new BackupHistory { FileName = fileName, SizeBytes = 999, BackupProvider = CreateProvider(config) };

        (await sut.ValidateAsync(history)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NullConfig_ReturnsFalse()
    {
        var sut = CreateSut();
        var history = new BackupHistory { FileName = "x", BackupProvider = CreateProvider("null") };

        (await sut.ValidateAsync(history)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_InvalidJson_ReturnsFalse()
    {
        var sut = CreateSut();
        var history = new BackupHistory { FileName = "x", BackupProvider = CreateProvider("garbage") };

        (await sut.ValidateAsync(history)).Should().BeFalse();
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_ExistingFile_DeletesAndReturnsTrue()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        Directory.CreateDirectory(target);
        var fileName = "todelete.zip";
        var filePath = Path.Combine(target, fileName);
        File.WriteAllText(filePath, "x");
        var config = JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new() { new NetworkSharePath { UncPath = target, Enabled = true } }
        });
        var history = new BackupHistory { FileName = fileName, BackupProvider = CreateProvider(config) };

        (await sut.DeleteAsync(history)).Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_FileNotFound_ReturnsFalse()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        Directory.CreateDirectory(target);
        var config = JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new() { new NetworkSharePath { UncPath = target, Enabled = true } }
        });
        var history = new BackupHistory { FileName = "gone.zip", BackupProvider = CreateProvider(config) };

        (await sut.DeleteAsync(history)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_InvalidJson_ReturnsFalse()
    {
        var sut = CreateSut();
        var history = new BackupHistory { FileName = "x", BackupProvider = CreateProvider("garbage") };

        (await sut.DeleteAsync(history)).Should().BeFalse();
    }

    // ----- TestConnectionAsync -----

    [Fact]
    public async Task TestConnectionAsync_ValidPath_ReturnsTrue()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        var config = JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new() { new NetworkSharePath { UncPath = target, Enabled = true } }
        });

        (await sut.TestConnectionAsync(CreateProvider(config))).Should().BeTrue();
    }

    [Fact]
    public async Task TestConnectionAsync_NoEnabledPaths_ReturnsFalse()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        var config = JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new() { new NetworkSharePath { UncPath = target, Enabled = false } }
        });

        (await sut.TestConnectionAsync(CreateProvider(config))).Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectionAsync_InvalidJson_ReturnsFalse()
    {
        var sut = CreateSut();
        (await sut.TestConnectionAsync(CreateProvider("garbage"))).Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectionAsync_PathFailsAndNoOtherPaths_ReturnsFalse()
    {
        var sut = CreateSut();
        // Cross-platform "invalid path": Windows-illegal characters like | ? < > are valid
        // filename characters on Linux, so that used to pass locally and fail in Linux CI. A real
        // FILE at the target, treated as a directory to write into, fails identically on both
        // platforms - same pattern already used for the File.Move-onto-a-directory fix elsewhere.
        var blockingFile = NewSourceFile();
        var badTarget = Path.Combine(blockingFile, "sub");
        var config = JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new() { new NetworkSharePath { UncPath = badTarget, Enabled = true } }
        });

        (await sut.TestConnectionAsync(CreateProvider(config))).Should().BeFalse();
    }
}
