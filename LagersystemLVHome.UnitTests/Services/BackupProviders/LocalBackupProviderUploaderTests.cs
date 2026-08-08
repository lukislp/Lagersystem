using LagersystemLVHome.Application.Services.BackupProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace LagersystemLVHome.UnitTests.Services.BackupProviders;

/// <summary>
/// Covers <see cref="LocalBackupProviderUploader"/>. Everything here is exercised against
/// the real filesystem (temp directories created/torn down per test) since the class has
/// no external seam - it only ever touches <see cref="System.IO"/>.
/// </summary>
public sealed class LocalBackupProviderUploaderTests : IDisposable
{
    private readonly List<string> _tempDirs = new();
    private readonly List<string> _tempFiles = new();

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
        GC.SuppressFinalize(this);
    }

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "local_uploader_" + Guid.NewGuid());
        _tempDirs.Add(dir);
        return dir;
    }

    private string NewSourceFile(string content = "backup-content")
    {
        var path = Path.Combine(Path.GetTempPath(), "local_uploader_src_" + Guid.NewGuid() + ".zip");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private static LocalBackupProviderUploader CreateSut()
        => new(NullLogger<LocalBackupProviderUploader>.Instance);

    private static BackupProvider CreateProvider(string configuration)
        => new() { Name = "P", Type = BackupProviderType.Local, Configuration = configuration };

    // ----- SupportedProviderType -----

    [Fact]
    public void SupportedProviderType_IsLocal()
    {
        CreateSut().SupportedProviderType.Should().Be(BackupProviderType.Local);
    }

    // ----- UploadAsync -----

    [Fact]
    public async Task UploadAsync_SinglePath_CopiesFileToDestination()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        var source = NewSourceFile("hello-world");
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { target } });

        var result = await sut.UploadAsync(CreateProvider(config), source);

        result.Should().BeTrue();
        var destFile = Path.Combine(target, Path.GetFileName(source));
        File.Exists(destFile).Should().BeTrue();
        (await File.ReadAllTextAsync(destFile)).Should().Be("hello-world");
    }

    [Fact]
    public async Task UploadAsync_MultiplePaths_CopiesToAll()
    {
        var sut = CreateSut();
        var target1 = NewTempDir();
        var target2 = NewTempDir();
        var source = NewSourceFile();
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { target1, target2 } });

        var result = await sut.UploadAsync(CreateProvider(config), source);

        result.Should().BeTrue();
        File.Exists(Path.Combine(target1, Path.GetFileName(source))).Should().BeTrue();
        File.Exists(Path.Combine(target2, Path.GetFileName(source))).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_NoPathsConfigured_ReturnsFalse()
    {
        var sut = CreateSut();
        var source = NewSourceFile();
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() });

        (await sut.UploadAsync(CreateProvider(config), source)).Should().BeFalse();
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
        (await sut.UploadAsync(CreateProvider("not-json-at-all"), NewSourceFile())).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_CreateDateSubfolders_PlacesFileUnderYearMonthFolder()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        var source = NewSourceFile();
        var config = JsonSerializer.Serialize(new LocalBackupConfig
        {
            Paths = new() { target },
            CreateDateSubfolders = true
        });

        var result = await sut.UploadAsync(CreateProvider(config), source);

        result.Should().BeTrue();
        var dateFolder = DateTime.UtcNow.ToString("yyyy-MM");
        var destFile = Path.Combine(target, dateFolder, Path.GetFileName(source));
        File.Exists(destFile).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_CreateWeekSubfolders_PlacesFileUnderWeekFolder()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        var source = NewSourceFile();
        var config = JsonSerializer.Serialize(new LocalBackupConfig
        {
            Paths = new() { target },
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
        var destFile = Path.Combine(target, dateFolder, weekFolder, Path.GetFileName(source));
        File.Exists(destFile).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_OnePathInvalid_OtherSucceeds_ReturnsTrue()
    {
        var sut = CreateSut();
        var badTarget = "C:\\invalid|path?<>";
        var goodTarget = NewTempDir();
        var source = NewSourceFile();
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { badTarget, goodTarget } });

        var result = await sut.UploadAsync(CreateProvider(config), source);

        result.Should().BeTrue("at least one of the two configured paths must have succeeded");
        File.Exists(Path.Combine(goodTarget, Path.GetFileName(source))).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_AllPathsInvalid_ReturnsFalse()
    {
        var sut = CreateSut();
        var badTarget = "C:\\invalid|path?<>";
        var source = NewSourceFile();
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { badTarget } });

        (await sut.UploadAsync(CreateProvider(config), source)).Should().BeFalse();
    }

    // ----- ValidateAsync -----

    [Fact]
    public async Task ValidateAsync_FileExistsWithMatchingSize_ReturnsTrue()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        Directory.CreateDirectory(target);
        var fileName = "backup1.zip";
        var content = "12345";
        File.WriteAllText(Path.Combine(target, fileName), content);
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { target } });
        var history = new BackupHistory
        {
            FileName = fileName,
            SizeBytes = content.Length,
            BackupProvider = CreateProvider(config)
        };

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
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { target } });
        var history = new BackupHistory
        {
            FileName = fileName,
            SizeBytes = 999,
            BackupProvider = CreateProvider(config)
        };

        (await sut.ValidateAsync(history)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_FileMissing_ReturnsFalse()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        Directory.CreateDirectory(target);
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { target } });
        var history = new BackupHistory { FileName = "missing.zip", SizeBytes = 5, BackupProvider = CreateProvider(config) };

        (await sut.ValidateAsync(history)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WithDateSubfolder_FindsFile()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        var backupDate = new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var dateFolder = backupDate.ToString("yyyy-MM");
        var dir = Path.Combine(target, dateFolder);
        Directory.CreateDirectory(dir);
        var fileName = "backup1.zip";
        File.WriteAllText(Path.Combine(dir, fileName), "abc");
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { target }, CreateDateSubfolders = true });
        var history = new BackupHistory
        {
            FileName = fileName,
            SizeBytes = 3,
            BackupDate = backupDate,
            BackupProvider = CreateProvider(config)
        };

        (await sut.ValidateAsync(history)).Should().BeTrue();
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
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { target } });
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
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { target } });
        var history = new BackupHistory { FileName = "gone.zip", BackupProvider = CreateProvider(config) };

        (await sut.DeleteAsync(history)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_MultiplePaths_StopsAfterFirstMatch()
    {
        var sut = CreateSut();
        var target1 = NewTempDir();
        var target2 = NewTempDir();
        Directory.CreateDirectory(target1);
        Directory.CreateDirectory(target2);
        var fileName = "dup.zip";
        File.WriteAllText(Path.Combine(target1, fileName), "a");
        File.WriteAllText(Path.Combine(target2, fileName), "b");
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { target1, target2 } });
        var history = new BackupHistory { FileName = fileName, BackupProvider = CreateProvider(config) };

        (await sut.DeleteAsync(history)).Should().BeTrue();
        File.Exists(Path.Combine(target1, fileName)).Should().BeFalse();
        File.Exists(Path.Combine(target2, fileName)).Should().BeTrue("deletion stops at the first match");
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
    public async Task TestConnectionAsync_ExistingDirectory_ReturnsTrue()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        Directory.CreateDirectory(target);
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { target } });

        (await sut.TestConnectionAsync(CreateProvider(config))).Should().BeTrue();
    }

    [Fact]
    public async Task TestConnectionAsync_MissingDirectory_CreatesItAndReturnsTrue()
    {
        var sut = CreateSut();
        var target = NewTempDir();
        Directory.Exists(target).Should().BeFalse();
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { target } });

        (await sut.TestConnectionAsync(CreateProvider(config))).Should().BeTrue();
        Directory.Exists(target).Should().BeTrue();
    }

    [Fact]
    public async Task TestConnectionAsync_NoPathsConfigured_ReturnsFalse()
    {
        var sut = CreateSut();
        var config = JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() });

        (await sut.TestConnectionAsync(CreateProvider(config))).Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectionAsync_InvalidJson_ReturnsFalse()
    {
        var sut = CreateSut();
        (await sut.TestConnectionAsync(CreateProvider("garbage"))).Should().BeFalse();
    }
}
