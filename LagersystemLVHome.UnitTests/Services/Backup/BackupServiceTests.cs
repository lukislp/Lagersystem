using LagersystemLVHome.Application.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using BackupSettings = LagersystemLVHome.Application.Configuration.BackupSettings;

namespace LagersystemLVHome.UnitTests.Services.Backup;

/// <summary>
/// Covers <see cref="BackupInfo"/>, <see cref="BackupService"/> and the (legacy,
/// same-file) <see cref="LagersystemLVHome.Application.Services.BackupHostedService"/> -
/// a database-file backup pipeline distinct from the JSON-based
/// <see cref="BackupManagementService"/> covered elsewhere. All file I/O runs against real
/// temp directories; <see cref="IDatabaseProviderService"/> is substituted since the real
/// implementation talks to an actual RDBMS.
/// </summary>
public sealed class BackupServiceTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "backupservice_" + Guid.NewGuid());
        _tempDirs.Add(dir);
        return dir;
    }

    private (BackupService Sut, string BackupDir, IDatabaseProviderService DbProvider) CreateSut(
        DatabaseProvider provider = DatabaseProvider.SQLite,
        bool compress = true,
        int maxBackupCount = 30,
        IDatabaseProviderService? dbProvider = null)
    {
        var contentRoot = NewTempDir();
        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(contentRoot);

        var settings = new BackupSettings { CompressBackups = compress, MaxBackupCount = maxBackupCount };

        var db = dbProvider;
        if (db == null)
        {
            db = Substitute.For<IDatabaseProviderService>();
            db.Provider.Returns(provider);
            // Stub BackupDatabaseAsync to actually materialize a file, mirroring what a
            // real provider implementation would do - CreateBackupAsync itself never
            // checks that the file exists, it just proceeds to (optionally) compress/log it.
            db.BackupDatabaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => { File.WriteAllText(ci.ArgAt<string>(0), "fake-db-content"); return Task.CompletedTask; });
        }

        var sut = new BackupService(db, settings, NullLogger<BackupService>.Instance, env);
        var backupDir = Path.Combine(contentRoot, settings.BackupDirectory);
        return (sut, backupDir, db);
    }

    // ----- BackupInfo.SizeFormatted -----

    [Theory]
    [InlineData(500, "500 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    public void BackupInfo_SizeFormatted_FormatsHumanReadableSize(long bytes, string expected)
    {
        var info = new BackupInfo { SizeBytes = bytes };
        info.SizeFormatted.Should().Be(expected);
    }

    // ----- Constructor -----

    [Fact]
    public void Constructor_CreatesBackupDirectory()
    {
        var (_, backupDir, _) = CreateSut();
        Directory.Exists(backupDir).Should().BeTrue();
    }

    // ----- CreateBackupAsync -----

    [Fact]
    public async Task CreateBackupAsync_SQLiteProvider_Uncompressed_CreatesDbFile()
    {
        var (sut, backupDir, _) = CreateSut(DatabaseProvider.SQLite, compress: false);

        await sut.CreateBackupAsync("mybackup");

        Directory.GetFiles(backupDir, "mybackup*").Should().ContainSingle()
            .Which.Should().EndWith(".db");
    }

    [Fact]
    public async Task CreateBackupAsync_PostgreSQLProvider_UsesBackupExtension()
    {
        var (sut, backupDir, _) = CreateSut(DatabaseProvider.PostgreSQL, compress: false);

        await sut.CreateBackupAsync("pg");

        Directory.GetFiles(backupDir, "pg*").Should().ContainSingle().Which.Should().EndWith(".backup");
    }

    [Fact]
    public async Task CreateBackupAsync_MySQLProvider_UsesSqlExtension()
    {
        var (sut, backupDir, _) = CreateSut(DatabaseProvider.MySQL, compress: false);

        await sut.CreateBackupAsync("my");

        Directory.GetFiles(backupDir, "my*").Should().ContainSingle().Which.Should().EndWith(".sql");
    }

    [Fact]
    public async Task CreateBackupAsync_UnknownProviderValue_FallsBackToBakExtension()
    {
        var (sut, backupDir, _) = CreateSut((DatabaseProvider)999, compress: false);

        await sut.CreateBackupAsync("unk");

        Directory.GetFiles(backupDir, "unk*").Should().ContainSingle().Which.Should().EndWith(".bak");
    }

    [Fact]
    public async Task CreateBackupAsync_CompressionEnabled_CreatesGzAndDeletesUncompressedFile()
    {
        var (sut, backupDir, _) = CreateSut(DatabaseProvider.SQLite, compress: true);

        await sut.CreateBackupAsync("compressed");

        var files = Directory.GetFiles(backupDir, "compressed*");
        files.Should().ContainSingle().Which.Should().EndWith(".db.gz");
        File.Exists(files[0].Replace(".gz", "")).Should().BeFalse("the uncompressed intermediate file must be deleted");
    }

    [Fact]
    public async Task CreateBackupAsync_DbProviderThrows_RethrowsException()
    {
        var db = Substitute.For<IDatabaseProviderService>();
        db.Provider.Returns(DatabaseProvider.SQLite);
        db.BackupDatabaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("disk full"));
        var (sut, _, _) = CreateSut(dbProvider: db);

        var act = async () => await sut.CreateBackupAsync();

        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task CreateBackupAsync_DefaultName_UsesTimestampedBackupPrefix()
    {
        var (sut, backupDir, _) = CreateSut(compress: false);

        await sut.CreateBackupAsync();

        Directory.GetFiles(backupDir, "backup_*.db").Should().ContainSingle();
    }

    // ----- GetBackupsAsync -----

    [Fact]
    public async Task GetBackupsAsync_ReturnsOnlyRecognizedExtensions_OrderedByCreatedDescending()
    {
        var (sut, backupDir, _) = CreateSut();
        File.WriteAllText(Path.Combine(backupDir, "a.db"), "1");
        await Task.Delay(15);
        File.WriteAllText(Path.Combine(backupDir, "b.backup"), "2");
        await Task.Delay(15);
        File.WriteAllText(Path.Combine(backupDir, "c.gz"), "3");
        File.WriteAllText(Path.Combine(backupDir, "ignored.txt"), "not a backup");

        var backups = (await sut.GetBackupsAsync()).ToList();

        backups.Select(b => b.FileName).Should().Equal("c.gz", "b.backup", "a.db");
        backups.Single(b => b.FileName == "c.gz").IsCompressed.Should().BeTrue();
        backups.Single(b => b.FileName == "a.db").IsCompressed.Should().BeFalse();
    }

    // ----- RestoreBackupAsync -----

    [Fact]
    public async Task RestoreBackupAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        var (sut, _, _) = CreateSut();

        var act = async () => await sut.RestoreBackupAsync("does-not-exist.db");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task RestoreBackupAsync_PlainFile_CreatesSafetyBackupThenRestores()
    {
        var (sut, backupDir, db) = CreateSut(compress: false);
        var backupFile = Path.Combine(backupDir, "existing.db");
        File.WriteAllText(backupFile, "existing-content");

        await sut.RestoreBackupAsync("existing.db");

        // Safety backup (pre_restore_*) was created via the same CreateBackupAsync path.
        Directory.GetFiles(backupDir, "pre_restore_*.db").Should().ContainSingle();
        await db.Received(1).RestoreDatabaseAsync(backupFile, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreBackupAsync_GzFile_DecompressesRestoresAndDeletesTempFile()
    {
        var (sut, backupDir, db) = CreateSut(compress: false);
        var gzFile = Path.Combine(backupDir, "compressed.db.gz");
        await using (var fileStream = File.Create(gzFile))
        await using (var gzip = new System.IO.Compression.GZipStream(fileStream, System.IO.Compression.CompressionMode.Compress))
        await using (var writer = new StreamWriter(gzip))
        {
            await writer.WriteAsync("decompressed-content");
        }

        await sut.RestoreBackupAsync("compressed.db.gz");

        var decompressedPath = gzFile.Replace(".gz", "");
        File.Exists(decompressedPath).Should().BeFalse("the temporary decompressed file must be cleaned up");
        await db.Received(1).RestoreDatabaseAsync(decompressedPath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreBackupAsync_DbProviderRestoreThrows_Rethrows()
    {
        var db = Substitute.For<IDatabaseProviderService>();
        db.Provider.Returns(DatabaseProvider.SQLite);
        db.BackupDatabaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => { File.WriteAllText(ci.ArgAt<string>(0), "x"); return Task.CompletedTask; });
        db.RestoreDatabaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("restore failed"));
        var (sut, backupDir, _) = CreateSut(dbProvider: db, compress: false);
        File.WriteAllText(Path.Combine(backupDir, "existing.db"), "x");

        var act = async () => await sut.RestoreBackupAsync("existing.db");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ----- DeleteBackupAsync -----

    [Fact]
    public async Task DeleteBackupAsync_ExistingFile_DeletesIt()
    {
        var (sut, backupDir, _) = CreateSut();
        var path = Path.Combine(backupDir, "todelete.db");
        File.WriteAllText(path, "x");

        await sut.DeleteBackupAsync("todelete.db");

        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteBackupAsync_MissingFile_IsNoOp()
    {
        var (sut, _, _) = CreateSut();

        var act = async () => await sut.DeleteBackupAsync("never-existed.db");

        await act.Should().NotThrowAsync();
    }

    // ----- CleanupOldBackupsAsync -----

    [Fact]
    public async Task CleanupOldBackupsAsync_RemovesOldestBeyondMaxCount()
    {
        var (sut, backupDir, _) = CreateSut(maxBackupCount: 2);
        File.WriteAllText(Path.Combine(backupDir, "old1.db"), "1");
        await Task.Delay(15);
        File.WriteAllText(Path.Combine(backupDir, "old2.db"), "2");
        await Task.Delay(15);
        File.WriteAllText(Path.Combine(backupDir, "new1.db"), "3");
        await Task.Delay(15);
        File.WriteAllText(Path.Combine(backupDir, "new2.db"), "4");

        await sut.CleanupOldBackupsAsync();

        var remaining = Directory.GetFiles(backupDir).Select(Path.GetFileName).ToList();
        remaining.Should().Contain("new1.db");
        remaining.Should().Contain("new2.db");
        remaining.Should().NotContain("old1.db");
        remaining.Should().NotContain("old2.db");
    }

    [Fact]
    public async Task CleanupOldBackupsAsync_WithinMaxCount_DeletesNothing()
    {
        var (sut, backupDir, _) = CreateSut(maxBackupCount: 30);
        File.WriteAllText(Path.Combine(backupDir, "a.db"), "1");
        File.WriteAllText(Path.Combine(backupDir, "b.db"), "2");

        await sut.CleanupOldBackupsAsync();

        Directory.GetFiles(backupDir).Should().HaveCount(2);
    }

    [Fact]
    public async Task CleanupOldBackupsAsync_WhenBackupDirectoryMissing_CatchesAndDoesNotThrow()
    {
        var (sut, backupDir, _) = CreateSut();
        Directory.Delete(backupDir, recursive: true);

        var act = async () => await sut.CleanupOldBackupsAsync();

        await act.Should().NotThrowAsync();
    }

    // ----- BackupHostedService (Application.Services namespace - legacy, same file) -----

    [Fact]
    public async Task BackupHostedService_Disabled_CompletesWithoutCreatingScope()
    {
        var settings = new BackupSettings { EnableAutoBackup = false };
        var serviceProvider = Substitute.For<IServiceProvider>();
        var sut = new LagersystemLVHome.Application.Services.BackupHostedService(
            serviceProvider, settings, NullLogger<LagersystemLVHome.Application.Services.BackupHostedService>.Instance);

        var act = async () => await sut.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        serviceProvider.DidNotReceiveWithAnyArgs().GetService(default!);
    }

    [Fact]
    public async Task BackupHostedService_EnabledButCancelledImmediately_SwallowsCancellationAndExitsCleanly()
    {
        // Task.Delay(TimeSpan.FromHours(...), stoppingToken) throws when the token is
        // already cancelled; production code catches Exception (not just
        // OperationCanceledException) inside the loop body, logs it, and the loop then
        // exits on its own IsCancellationRequested check - so StartAsync must complete
        // without ever propagating that exception.
        var settings = new BackupSettings { EnableAutoBackup = true, BackupIntervalHours = 24 };
        var serviceProvider = Substitute.For<IServiceProvider>();
        var sut = new LagersystemLVHome.Application.Services.BackupHostedService(
            serviceProvider, settings, NullLogger<LagersystemLVHome.Application.Services.BackupHostedService>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await sut.StartAsync(cts.Token);

        await act.Should().NotThrowAsync();
    }
}
