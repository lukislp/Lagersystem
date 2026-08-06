using System.IO.Compression;
using System.Text.Json;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Backup;

/// <summary>
/// Covers <see cref="KeyBackupService"/>.
///
/// The production code resolves its "keys" directory via
/// <c>Directory.GetCurrentDirectory()</c> (not injectable), which for a test run is the
/// test assembly's own bin/&lt;config&gt;/net10.0 output directory (verified empirically),
/// never real application data. Each test creates/tears down that directory itself so
/// tests stay isolated even though the path is shared, real, on-disk state. xUnit runs
/// test methods within a class sequentially by default (one collection per class), so
/// there is no cross-test race on that shared path.
///
/// Real Win32 networking (WNetAddConnection2/WNetCancelConnection2, P/Invoke into
/// mpr.dll) is intentionally never exercised with real credentials/hosts: it would
/// either hang on DNS resolution or require an actual reachable SMB share, which isn't
/// available in this sandbox. The "invalid UNC format" fast-fail branch inside
/// ConnectToNetworkShare (parts.Length &lt; 2, returns false before any P/Invoke call) is
/// covered instead, and NetworkShare happy-path tests omit credentials so the connect
/// step is skipped entirely (matches how the production method behaves when no
/// Username/Password are configured). The success/failure outcomes of the real
/// WNetAddConnection2 call itself, and DisconnectFromNetworkShare (only reached when
/// that call reports success), remain untested - equivalent in spirit to the
/// pg_dump/external-process seam called out in the task brief.
/// </summary>
public sealed class KeyBackupServiceTests : IDisposable
{
    private readonly string _keysDir = Path.Combine(Directory.GetCurrentDirectory(), "keys");
    private readonly bool _keysDirPreExisted;
    private readonly List<string> _ownedKeyFiles = new();
    private readonly List<string> _ownedTempDirs = new();

    public KeyBackupServiceTests()
    {
        _keysDirPreExisted = Directory.Exists(_keysDir);
        Directory.CreateDirectory(_keysDir);
    }

    public void Dispose()
    {
        foreach (var file in _ownedKeyFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { /* best effort */ }
        }

        if (Directory.Exists(_keysDir))
        {
            if (!_keysDirPreExisted)
            {
                try { Directory.Delete(_keysDir, recursive: true); } catch { /* best effort */ }
            }
        }

        foreach (var dir in _ownedTempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }

        // Best-effort sweep of incidental temp files the SUT itself writes directly
        // under Path.GetTempPath() (safety-backup zips, decrypted scratch files) that
        // cannot otherwise be targeted since their names are timestamp/Guid-based and
        // chosen internally by production code.
        foreach (var pattern in new[] { "keys_safety_backup_*.zip", "decrypted_*.zip" })
        {
            foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), pattern))
            {
                try { File.Delete(f); } catch { /* best effort */ }
            }
        }

        GC.SuppressFinalize(this);
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kbs_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        _ownedTempDirs.Add(dir);
        return dir;
    }

    private string WriteKeyFile(string name, string content)
    {
        var path = Path.Combine(_keysDir, name);
        File.WriteAllText(path, content);
        _ownedKeyFiles.Add(path);
        return path;
    }

    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static ISecureConfigurationService CreatePassthroughSecureConfig()
    {
        var secureConfig = Substitute.For<ISecureConfigurationService>();
        secureConfig.Decrypt(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        secureConfig.Encrypt(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        secureConfig.IsEncrypted(Arg.Any<string>()).Returns(false);
        return secureConfig;
    }

    private static KeyBackupService CreateSut(
        IDbContextFactory<InventoryDbContext> factory,
        ISecureConfigurationService? secureConfig = null)
        => new(factory, NullLogger<KeyBackupService>.Instance, secureConfig ?? CreatePassthroughSecureConfig());

    private static async Task SeedKeyBackupSettingsAsync(
        IDbContextFactory<InventoryDbContext> factory, LagersystemLVHome.Domain.Models.KeyBackupSettings settings)
    {
        await using var db = factory.CreateDbContext();
        db.SystemSettings.Add(new SystemSetting
        {
            Key = "KeyBackupSettings",
            Value = JsonSerializer.Serialize(settings)
        });
        await db.SaveChangesAsync();
    }

    private static async Task<BackupProvider> SeedProviderAsync(
        IDbContextFactory<InventoryDbContext> factory,
        int id,
        BackupProviderType type,
        string configuration,
        bool enabled = true)
    {
        await using var db = factory.CreateDbContext();
        var provider = new BackupProvider
        {
            Id = id,
            Name = $"provider-{id}",
            Type = type,
            Enabled = enabled,
            Configuration = configuration
        };
        db.BackupProviders.Add(provider);
        await db.SaveChangesAsync();
        return provider;
    }

    // ----- GetSettingsAsync / UpdateSettingsAsync -----

    [Fact]
    public async Task GetSettingsAsync_NoSettingsStored_ReturnsSafeDefaults()
    {
        var factory = CreateFactory(nameof(GetSettingsAsync_NoSettingsStored_ReturnsSafeDefaults));
        var sut = CreateSut(factory);

        var settings = await sut.GetSettingsAsync();

        settings.Enabled.Should().BeFalse();
        settings.BackupHour.Should().Be(3);
        settings.BackupProviderId.Should().BeNull();
        settings.RetentionDays.Should().Be(90);
        settings.RequirePassword.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateSettingsAsync_CreatesNewSystemSetting_WhenNoneExists()
    {
        var factory = CreateFactory(nameof(UpdateSettingsAsync_CreatesNewSystemSetting_WhenNoneExists));
        var sut = CreateSut(factory);

        await sut.UpdateSettingsAsync(new LagersystemLVHome.Domain.Models.KeyBackupSettings
        {
            Enabled = true,
            BackupHour = 5,
            BackupProviderId = 7,
            RetentionDays = 30,
            RequirePassword = true
        });

        var reloaded = await sut.GetSettingsAsync();
        reloaded.Enabled.Should().BeTrue();
        reloaded.BackupHour.Should().Be(5);
        reloaded.BackupProviderId.Should().Be(7);
        reloaded.RetentionDays.Should().Be(30);
        reloaded.RequirePassword.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateSettingsAsync_UpdatesExistingRow_SetsUpdatedAt()
    {
        var factory = CreateFactory(nameof(UpdateSettingsAsync_UpdatesExistingRow_SetsUpdatedAt));
        var sut = CreateSut(factory);
        await sut.UpdateSettingsAsync(new LagersystemLVHome.Domain.Models.KeyBackupSettings { Enabled = false, RetentionDays = 10 });

        await sut.UpdateSettingsAsync(new LagersystemLVHome.Domain.Models.KeyBackupSettings { Enabled = true, RetentionDays = 20 });

        await using var db = factory.CreateDbContext();
        var rows = await db.SystemSettings.Where(s => s.Key == "KeyBackupSettings").ToListAsync();
        rows.Should().ContainSingle();
        rows[0].UpdatedAt.Should().NotBeNull();
        (await sut.GetSettingsAsync()).RetentionDays.Should().Be(20);
    }

    // ----- CreateKeyBackupAsync -----

    [Fact]
    public async Task CreateKeyBackupAsync_Disabled_ReturnsFailureWithoutTouchingDisk()
    {
        var factory = CreateFactory(nameof(CreateKeyBackupAsync_Disabled_ReturnsFailureWithoutTouchingDisk));
        await SeedKeyBackupSettingsAsync(factory, new LagersystemLVHome.Domain.Models.KeyBackupSettings { Enabled = false, BackupProviderId = 1 });
        var sut = CreateSut(factory);

        var result = await sut.CreateKeyBackupAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateKeyBackupAsync_NoProviderConfigured_ReturnsFailure()
    {
        var factory = CreateFactory(nameof(CreateKeyBackupAsync_NoProviderConfigured_ReturnsFailure));
        await SeedKeyBackupSettingsAsync(factory, new LagersystemLVHome.Domain.Models.KeyBackupSettings { Enabled = true, BackupProviderId = null });
        var sut = CreateSut(factory);

        var result = await sut.CreateKeyBackupAsync();

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CreateKeyBackupAsync_ProviderNotFound_ReturnsFailure()
    {
        var factory = CreateFactory(nameof(CreateKeyBackupAsync_ProviderNotFound_ReturnsFailure));
        await SeedKeyBackupSettingsAsync(factory, new LagersystemLVHome.Domain.Models.KeyBackupSettings { Enabled = true, BackupProviderId = 999 });
        var sut = CreateSut(factory);

        var result = await sut.CreateKeyBackupAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Provider");
    }

    [Fact]
    public async Task CreateKeyBackupAsync_KeysDirectoryMissing_ReturnsFailure()
    {
        Directory.Delete(_keysDir, recursive: true);

        var factory = CreateFactory(nameof(CreateKeyBackupAsync_KeysDirectoryMissing_ReturnsFailure));
        await SeedKeyBackupSettingsAsync(factory, new LagersystemLVHome.Domain.Models.KeyBackupSettings { Enabled = true, BackupProviderId = 1 });
        await SeedProviderAsync(factory, 1, BackupProviderType.Local, JsonSerializer.Serialize(new LocalBackupConfig()));
        var sut = CreateSut(factory);

        var result = await sut.CreateKeyBackupAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Keys-Directory");
    }

    [Fact]
    public async Task CreateKeyBackupAsync_UnsupportedProviderType_ReturnsFailure()
    {
        WriteKeyFile("key-a.xml", "a");
        var factory = CreateFactory(nameof(CreateKeyBackupAsync_UnsupportedProviderType_ReturnsFailure));
        await SeedKeyBackupSettingsAsync(factory, new LagersystemLVHome.Domain.Models.KeyBackupSettings { Enabled = true, BackupProviderId = 1 });
        await SeedProviderAsync(factory, 1, BackupProviderType.AzureBlob, JsonSerializer.Serialize(new AzureBlobConfig()));
        var sut = CreateSut(factory);

        var result = await sut.CreateKeyBackupAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Local Storage");
    }

    [Fact]
    public async Task CreateKeyBackupAsync_LocalProvider_Success_CreatesZipAndHistoryAndCleansOldBackups()
    {
        WriteKeyFile("key-alpha.xml", "alpha-content");
        var localBase = CreateTempDir();
        var factory = CreateFactory(nameof(CreateKeyBackupAsync_LocalProvider_Success_CreatesZipAndHistoryAndCleansOldBackups));
        await SeedKeyBackupSettingsAsync(factory, new LagersystemLVHome.Domain.Models.KeyBackupSettings { Enabled = true, BackupProviderId = 1, RetentionDays = 30 });
        var provider = await SeedProviderAsync(factory, 1, BackupProviderType.Local,
            JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { localBase } }));

        // Seed an old key backup (past retention) referencing a real file so cleanup can delete it.
        var oldFile = Path.Combine(localBase, "old_key_backup.zip");
        await File.WriteAllTextAsync(oldFile, "stale");
        await using (var db = factory.CreateDbContext())
        {
            db.KeyBackupHistory.Add(new KeyBackupHistory
            {
                BackupDate = DateTime.UtcNow.AddDays(-60),
                FileName = "old_key_backup.zip",
                FilePath = oldFile,
                BackupProviderId = provider.Id,
                ProviderCount = 1,
                SizeBytes = 5,
                Status = BackupStatus.Success
            });
            await db.SaveChangesAsync();
        }

        var sut = CreateSut(factory);

        var result = await sut.CreateKeyBackupAsync();

        result.Success.Should().BeTrue();
        result.FileName.Should().NotBeNullOrEmpty();
        result.FileName.Should().StartWith("encryption_keys_backup_").And.EndWith(".zip");

        var expectedFile = Path.Combine(localBase, "KeyBackups", result.FileName!);
        File.Exists(expectedFile).Should().BeTrue();

        using (var archive = ZipFile.OpenRead(expectedFile))
        {
            archive.Entries.Should().ContainSingle(e => e.Name == "key-alpha.xml");
        }

        await using var verifyDb = factory.CreateDbContext();
        var histories = await verifyDb.KeyBackupHistory.ToListAsync();
        histories.Should().ContainSingle(h => h.FileName == result.FileName);
        histories.Should().NotContain(h => h.FileName == "old_key_backup.zip");
        File.Exists(oldFile).Should().BeFalse();
    }

    [Fact]
    public async Task CreateKeyBackupAsync_WithRequirePassword_EncryptsFileAndRestoreRoundtripsCorrectly()
    {
        WriteKeyFile("key-beta.xml", "beta-content-1234");
        var localBase = CreateTempDir();
        var factory = CreateFactory(nameof(CreateKeyBackupAsync_WithRequirePassword_EncryptsFileAndRestoreRoundtripsCorrectly));
        await SeedKeyBackupSettingsAsync(factory, new LagersystemLVHome.Domain.Models.KeyBackupSettings
        {
            Enabled = true,
            BackupProviderId = 1,
            RetentionDays = 90,
            RequirePassword = true,
            BackupPassword = "correct-horse"
        });
        await SeedProviderAsync(factory, 1, BackupProviderType.Local,
            JsonSerializer.Serialize(new LocalBackupConfig { Paths = new() { localBase } }));

        var sut = CreateSut(factory);

        var result = await sut.CreateKeyBackupAsync();

        result.Success.Should().BeTrue();
        result.FileName.Should().EndWith(".zip.enc");

        await using var db = factory.CreateDbContext();
        var history = await db.KeyBackupHistory.SingleAsync();
        history.IsEncrypted.Should().BeTrue();

        // Wipe the local key file to prove restore actually repopulates it from the encrypted archive.
        File.Delete(Path.Combine(_keysDir, "key-beta.xml"));
        _ownedKeyFiles.Remove(Path.Combine(_keysDir, "key-beta.xml"));

        var restored = await sut.RestoreKeysFromBackupAsync(history.Id, "correct-horse");

        restored.Should().BeTrue();
        var restoredPath = Path.Combine(_keysDir, "key-beta.xml");
        File.Exists(restoredPath).Should().BeTrue();
        (await File.ReadAllTextAsync(restoredPath)).Should().Be("beta-content-1234");
        _ownedKeyFiles.Add(restoredPath);
    }

    [Fact]
    public async Task CreateKeyBackupAsync_NetworkShareProvider_NoCredentials_SkipsConnectAndSucceedsLocally()
    {
        WriteKeyFile("key-gamma.xml", "gamma");
        var shareBase = CreateTempDir();
        var factory = CreateFactory(nameof(CreateKeyBackupAsync_NetworkShareProvider_NoCredentials_SkipsConnectAndSucceedsLocally));
        await SeedKeyBackupSettingsAsync(factory, new LagersystemLVHome.Domain.Models.KeyBackupSettings { Enabled = true, BackupProviderId = 1 });
        await SeedProviderAsync(factory, 1, BackupProviderType.NetworkShare, JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new() { new NetworkSharePath { UncPath = shareBase } }
        }));

        var sut = CreateSut(factory);

        var result = await sut.CreateKeyBackupAsync();

        result.Success.Should().BeTrue();
        Directory.Exists(Path.Combine(shareBase, "KeyBackups")).Should().BeTrue();
    }

    [Fact]
    public async Task CreateKeyBackupAsync_NetworkShareProvider_InvalidUncFormatWithCredentials_LogsAndContinuesAnyway()
    {
        // A single-segment "UNC" path makes ConnectToNetworkShare's own format guard
        // (parts.Length < 2) return false *before* it ever calls into WNetAddConnection2,
        // so this exercises that branch deterministically without touching real networking.
        WriteKeyFile("key-delta.xml", "delta");
        var relativeShareName = "unittest_share_" + Guid.NewGuid().ToString("N");
        var factory = CreateFactory(nameof(CreateKeyBackupAsync_NetworkShareProvider_InvalidUncFormatWithCredentials_LogsAndContinuesAnyway));
        await SeedKeyBackupSettingsAsync(factory, new LagersystemLVHome.Domain.Models.KeyBackupSettings { Enabled = true, BackupProviderId = 1 });
        await SeedProviderAsync(factory, 1, BackupProviderType.NetworkShare, JsonSerializer.Serialize(new NetworkShareConfig
        {
            Paths = new() { new NetworkSharePath { UncPath = relativeShareName } },
            Username = "someuser",
            Password = "somepass"
        }));

        var sut = CreateSut(factory);
        try
        {
            var result = await sut.CreateKeyBackupAsync();

            result.Success.Should().BeTrue();
        }
        finally
        {
            var createdDir = Path.Combine(Directory.GetCurrentDirectory(), relativeShareName);
            if (Directory.Exists(createdDir)) Directory.Delete(createdDir, recursive: true);
        }
    }

    // ----- RestoreKeysFromBackupAsync -----

    [Fact]
    public async Task RestoreKeysFromBackupAsync_HistoryNotFound_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(RestoreKeysFromBackupAsync_HistoryNotFound_ReturnsFalse));
        var sut = CreateSut(factory);

        (await sut.RestoreKeysFromBackupAsync(123, null)).Should().BeFalse();
    }

    [Fact]
    public async Task RestoreKeysFromBackupAsync_FileMissing_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(RestoreKeysFromBackupAsync_FileMissing_ReturnsFalse));
        var provider = await SeedProviderAsync(factory, 1, BackupProviderType.Local, JsonSerializer.Serialize(new LocalBackupConfig()));
        await using (var db = factory.CreateDbContext())
        {
            db.KeyBackupHistory.Add(new KeyBackupHistory
            {
                BackupDate = DateTime.UtcNow,
                FileName = "gone.zip",
                FilePath = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".zip"),
                BackupProviderId = provider.Id,
                Status = BackupStatus.Success
            });
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var historyId = (await factory.CreateDbContext().KeyBackupHistory.FirstAsync()).Id;

        (await sut.RestoreKeysFromBackupAsync(historyId, null)).Should().BeFalse();
    }

    [Fact]
    public async Task RestoreKeysFromBackupAsync_EncryptedButNoPassword_ReturnsFalse()
    {
        var localBase = CreateTempDir();
        var zipPath = Path.Combine(localBase, "enc.zip.enc");
        await File.WriteAllBytesAsync(zipPath, new byte[] { 1, 2, 3 });

        var factory = CreateFactory(nameof(RestoreKeysFromBackupAsync_EncryptedButNoPassword_ReturnsFalse));
        var provider = await SeedProviderAsync(factory, 1, BackupProviderType.Local, JsonSerializer.Serialize(new LocalBackupConfig()));
        int historyId;
        await using (var db = factory.CreateDbContext())
        {
            var history = new KeyBackupHistory
            {
                BackupDate = DateTime.UtcNow,
                FileName = "enc.zip.enc",
                FilePath = zipPath,
                BackupProviderId = provider.Id,
                IsEncrypted = true,
                Status = BackupStatus.Success
            };
            db.KeyBackupHistory.Add(history);
            await db.SaveChangesAsync();
            historyId = history.Id;
        }

        var sut = CreateSut(factory);

        (await sut.RestoreKeysFromBackupAsync(historyId, null)).Should().BeFalse();
    }

    [Fact]
    public async Task RestoreKeysFromBackupAsync_CorruptArchive_CatchesAndReturnsFalse()
    {
        var localBase = CreateTempDir();
        var zipPath = Path.Combine(localBase, "corrupt.zip");
        await File.WriteAllBytesAsync(zipPath, new byte[] { 0x00, 0x01, 0x02, 0x03 });

        var factory = CreateFactory(nameof(RestoreKeysFromBackupAsync_CorruptArchive_CatchesAndReturnsFalse));
        var provider = await SeedProviderAsync(factory, 1, BackupProviderType.Local, JsonSerializer.Serialize(new LocalBackupConfig()));
        int historyId;
        await using (var db = factory.CreateDbContext())
        {
            var history = new KeyBackupHistory
            {
                BackupDate = DateTime.UtcNow,
                FileName = "corrupt.zip",
                FilePath = zipPath,
                BackupProviderId = provider.Id,
                IsEncrypted = false,
                Status = BackupStatus.Success
            };
            db.KeyBackupHistory.Add(history);
            await db.SaveChangesAsync();
            historyId = history.Id;
        }

        var sut = CreateSut(factory);

        (await sut.RestoreKeysFromBackupAsync(historyId, null)).Should().BeFalse();
    }

    [Fact]
    public async Task RestoreKeysFromBackupAsync_NotEncrypted_ExtractsAndCreatesSafetyBackup()
    {
        WriteKeyFile("key-existing.xml", "existing-before-restore");

        var localBase = CreateTempDir();
        var sourceKeyDir = CreateTempDir();
        File.WriteAllText(Path.Combine(sourceKeyDir, "key-restored.xml"), "restored-content");
        var zipPath = Path.Combine(localBase, "plain.zip");
        ZipFile.CreateFromDirectory(sourceKeyDir, zipPath);

        var factory = CreateFactory(nameof(RestoreKeysFromBackupAsync_NotEncrypted_ExtractsAndCreatesSafetyBackup));
        var provider = await SeedProviderAsync(factory, 1, BackupProviderType.Local, JsonSerializer.Serialize(new LocalBackupConfig()));
        int historyId;
        await using (var db = factory.CreateDbContext())
        {
            var history = new KeyBackupHistory
            {
                BackupDate = DateTime.UtcNow,
                FileName = "plain.zip",
                FilePath = zipPath,
                BackupProviderId = provider.Id,
                IsEncrypted = false,
                Status = BackupStatus.Success
            };
            db.KeyBackupHistory.Add(history);
            await db.SaveChangesAsync();
            historyId = history.Id;
        }

        var sut = CreateSut(factory);

        var restored = await sut.RestoreKeysFromBackupAsync(historyId, null);

        restored.Should().BeTrue();
        var restoredFile = Path.Combine(_keysDir, "key-restored.xml");
        File.Exists(restoredFile).Should().BeTrue();
        _ownedKeyFiles.Add(restoredFile);
        // Overwrite semantics: pre-existing key file is untouched (not part of the restored archive).
        File.Exists(Path.Combine(_keysDir, "key-existing.xml")).Should().BeTrue();
    }

    // ----- GetHistoryAsync -----

    [Fact]
    public async Task GetHistoryAsync_OrdersByDateDescendingAndCapsAt100()
    {
        var factory = CreateFactory(nameof(GetHistoryAsync_OrdersByDateDescendingAndCapsAt100));
        var provider = await SeedProviderAsync(factory, 1, BackupProviderType.Local, "{}");
        await using (var db = factory.CreateDbContext())
        {
            db.KeyBackupHistory.Add(new KeyBackupHistory { BackupDate = DateTime.UtcNow.AddDays(-1), FileName = "old.zip", FilePath = "x", BackupProviderId = provider.Id });
            db.KeyBackupHistory.Add(new KeyBackupHistory { BackupDate = DateTime.UtcNow, FileName = "new.zip", FilePath = "x", BackupProviderId = provider.Id });
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var history = await sut.GetHistoryAsync();

        history.Should().HaveCount(2);
        history[0].FileName.Should().Be("new.zip");
        history[1].FileName.Should().Be("old.zip");
    }

    // ----- DeleteKeyBackupAsync -----

    [Fact]
    public async Task DeleteKeyBackupAsync_NotFound_IsNoOp()
    {
        var factory = CreateFactory(nameof(DeleteKeyBackupAsync_NotFound_IsNoOp));
        var sut = CreateSut(factory);

        var act = async () => await sut.DeleteKeyBackupAsync(555);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteKeyBackupAsync_DeletesFileAndDbRow()
    {
        var localBase = CreateTempDir();
        var filePath = Path.Combine(localBase, "todelete.zip");
        await File.WriteAllTextAsync(filePath, "x");

        var factory = CreateFactory(nameof(DeleteKeyBackupAsync_DeletesFileAndDbRow));
        var provider = await SeedProviderAsync(factory, 1, BackupProviderType.Local, "{}");
        int historyId;
        await using (var db = factory.CreateDbContext())
        {
            var history = new KeyBackupHistory { BackupDate = DateTime.UtcNow, FileName = "todelete.zip", FilePath = filePath, BackupProviderId = provider.Id };
            db.KeyBackupHistory.Add(history);
            await db.SaveChangesAsync();
            historyId = history.Id;
        }
        var sut = CreateSut(factory);

        await sut.DeleteKeyBackupAsync(historyId);

        File.Exists(filePath).Should().BeFalse();
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.KeyBackupHistory.AnyAsync(h => h.Id == historyId)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteKeyBackupAsync_FileAlreadyMissing_StillRemovesDbRow()
    {
        var factory = CreateFactory(nameof(DeleteKeyBackupAsync_FileAlreadyMissing_StillRemovesDbRow));
        var provider = await SeedProviderAsync(factory, 1, BackupProviderType.Local, "{}");
        int historyId;
        await using (var db = factory.CreateDbContext())
        {
            var history = new KeyBackupHistory
            {
                BackupDate = DateTime.UtcNow,
                FileName = "gone.zip",
                FilePath = Path.Combine(Path.GetTempPath(), "never-existed-" + Guid.NewGuid() + ".zip"),
                BackupProviderId = provider.Id
            };
            db.KeyBackupHistory.Add(history);
            await db.SaveChangesAsync();
            historyId = history.Id;
        }
        var sut = CreateSut(factory);

        await sut.DeleteKeyBackupAsync(historyId);

        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.KeyBackupHistory.AnyAsync(h => h.Id == historyId)).Should().BeFalse();
    }

    // ----- GetAvailableLocalProvidersAsync -----

    [Fact]
    public async Task GetAvailableLocalProvidersAsync_FiltersToEnabledLocalAndNetworkShareOnly()
    {
        var factory = CreateFactory(nameof(GetAvailableLocalProvidersAsync_FiltersToEnabledLocalAndNetworkShareOnly));
        await SeedProviderAsync(factory, 1, BackupProviderType.Local, "{}", enabled: true);
        await SeedProviderAsync(factory, 2, BackupProviderType.NetworkShare, "{}", enabled: true);
        await SeedProviderAsync(factory, 3, BackupProviderType.Local, "{}", enabled: false);
        await SeedProviderAsync(factory, 4, BackupProviderType.AzureBlob, "{}", enabled: true);
        var sut = CreateSut(factory);

        var providers = await sut.GetAvailableLocalProvidersAsync();

        providers.Select(p => p.Id).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task GetAvailableLocalProvidersAsync_DecryptFailure_IsLoggedAndProviderStillReturned()
    {
        var factory = CreateFactory(nameof(GetAvailableLocalProvidersAsync_DecryptFailure_IsLoggedAndProviderStillReturned));
        await SeedProviderAsync(factory, 1, BackupProviderType.Local, "not-empty-cipher-text");
        var secureConfig = Substitute.For<ISecureConfigurationService>();
        secureConfig.Decrypt(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("bad cipher"));
        var sut = CreateSut(factory, secureConfig);

        var providers = await sut.GetAvailableLocalProvidersAsync();

        providers.Should().ContainSingle();
        providers[0].Configuration.Should().Be("not-empty-cipher-text");
    }
}
