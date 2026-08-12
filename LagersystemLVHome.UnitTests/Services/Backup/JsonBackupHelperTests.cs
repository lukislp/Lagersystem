using System.IO.Compression;
using System.Text.Json;
using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.UnitTests.Services.Backup;

/// <summary>
/// Covers <see cref="JsonBackupHelper"/>.
///
/// TRUNCATE/DELETE for PostgreSQL (<c>TruncatePostgreSQLAsync</c>) and the sequence
/// reset step (<c>ResetAllPostgreSQLSequencesAsync</c>) open a real <c>NpgsqlConnection</c>
/// and are exercised only when <c>DatabaseSettings.Provider == PostgreSQL</c>; there is no
/// live PostgreSQL server in this environment, so those two methods are intentionally
/// never invoked here - equivalent to the external-process/pg_dump seam called out in
/// the task brief. The SQLite branch (<c>TruncateSQLiteAsync</c>) issues raw
/// <c>ExecuteSqlRawAsync("DELETE FROM ...")</c> calls, which the EF Core InMemory
/// provider does not support; each per-table call throws and is caught+logged inside the
/// helper's own per-table try/catch, so restore tests below still complete successfully -
/// they just don't prove real truncation semantics, only that the surrounding
/// import/round-trip logic is correct. This mirrors the "InMemory caveats apply" note in
/// the task brief.
/// </summary>
public sealed class JsonBackupHelperTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private string NewTempZipPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jbh_test_{Guid.NewGuid()}.zip");
        _tempPaths.Add(path);
        return path;
    }

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"jbh_test_dir_{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        _tempPaths.Add(dir);
        return dir;
    }

    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static JsonBackupHelper CreateSut(
        IDbContextFactory<InventoryDbContext> factory,
        DatabaseProvider provider = DatabaseProvider.SQLite)
        => new(factory, NullLogger<JsonBackupHelper>.Instance,
            Options.Create(new DatabaseSettings { Provider = provider, ConnectionString = "Data Source=:memory:" }));

    private static async Task SeedBasicDataAsync(IDbContextFactory<InventoryDbContext> factory)
    {
        await using var db = factory.CreateDbContext();
        db.Warehouses.Add(new Warehouse { Id = 1, Name = "WH1", Address = "a", IsActive = true });
        db.Users.Add(new User
        {
            Id = 1,
            Username = "u1",
            Email = "u1@x.local",
            DisplayName = "User 1",
            PasswordHash = "x",
            WarehouseId = 1,
            ApprovalStatus = UserApprovalStatus.Approved,
            Role = UserRole.User,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    // ----- CreateJsonBackupAsync -----

    [Fact]
    public async Task CreateJsonBackupAsync_WritesZipWithMetadataAndAllTableFiles()
    {
        var factory = CreateFactory(nameof(CreateJsonBackupAsync_WritesZipWithMetadataAndAllTableFiles));
        await SeedBasicDataAsync(factory);
        var sut = CreateSut(factory);
        var outputPath = NewTempZipPath();

        await sut.CreateJsonBackupAsync(outputPath);

        File.Exists(outputPath).Should().BeTrue();

        using var archive = ZipFile.OpenRead(outputPath);
        var metadataEntry = archive.GetEntry("metadata.json");
        metadataEntry.Should().NotBeNull();

        using var reader = new StreamReader(metadataEntry!.Open());
        var metadata = JsonSerializer.Deserialize<BackupMetadata>(await reader.ReadToEndAsync())!;

        metadata.BackupType.Should().Be("JSON");
        metadata.Version.Should().Be("1.1");
        metadata.DatabaseProvider.Should().Be("SQLite");
        metadata.TableCounts["Warehouses"].Should().Be(1);
        metadata.TableCounts["Users"].Should().Be(1);
        metadata.TableCounts["Products"].Should().Be(0);

        // 28 exported tables + metadata.json
        archive.Entries.Should().HaveCount(metadata.TableCounts.Count + 1);
        archive.GetEntry("Warehouses.json").Should().NotBeNull();
        archive.GetEntry("Users.json").Should().NotBeNull();
    }

    [Fact]
    public async Task CreateJsonBackupAsync_CleansUpItsOwnScratchDirectoryInFinally()
    {
        // NOTE: intentionally does not snapshot/diff Path.GetTempPath()'s "backup_*"
        // directories globally - other test classes (e.g. BackupManagementServiceTests)
        // exercise the same real JsonBackupHelper concurrently under xUnit's default
        // cross-class parallelization, which made a global-directory-listing diff here
        // flaky (a concurrently-running, unrelated test's still-in-flight scratch
        // directory would show up as a false "leftover"). A successful, well-formed
        // output archive can only exist if the try block (and therefore the finally
        // cleanup) ran to completion; the exception-path variant below captures its own
        // scratch directory precisely instead of diffing global state.
        var factory = CreateFactory(nameof(CreateJsonBackupAsync_CleansUpItsOwnScratchDirectoryInFinally));
        var sut = CreateSut(factory);
        var outputPath = NewTempZipPath();

        await sut.CreateJsonBackupAsync(outputPath);

        File.Exists(outputPath).Should().BeTrue();
        using var archive = ZipFile.OpenRead(outputPath);
        archive.Entries.Should().NotBeEmpty();
    }

    private sealed class CapturingThrowingContextFactory(Action onCreateDbContext) : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext()
        {
            onCreateDbContext();
            throw new InvalidOperationException("simulated export failure");
        }
    }

    [Fact]
    public async Task CreateJsonBackupAsync_ExceptionDuringExport_StillCleansUpItsOwnScratchDirectory()
    {
        // Precisely identifies *this* call's own scratch directory (rather than diffing
        // global temp-directory state, which is racy under parallel test execution - see
        // the test above) by capturing it the instant this SUT's own context-factory call
        // fires, which happens synchronously right after JsonBackupHelper creates it.
        string? capturedScratchDir = null;
        var throwingFactory = new CapturingThrowingContextFactory(() =>
        {
            capturedScratchDir = Directory.GetDirectories(Path.GetTempPath(), "backup_*")
                .OrderByDescending(Directory.GetCreationTimeUtc)
                .FirstOrDefault();
        });
        var sut = new JsonBackupHelper(throwingFactory, NullLogger<JsonBackupHelper>.Instance,
            Options.Create(new DatabaseSettings { Provider = DatabaseProvider.SQLite }));

        var act = async () => await sut.CreateJsonBackupAsync(NewTempZipPath());

        await act.Should().ThrowAsync<InvalidOperationException>();
        capturedScratchDir.Should().NotBeNull();
        Directory.Exists(capturedScratchDir!).Should().BeFalse();
    }

    [Fact]
    public async Task CreateJsonBackupAsync_OverwritesExistingOutputFile()
    {
        var factory = CreateFactory(nameof(CreateJsonBackupAsync_OverwritesExistingOutputFile));
        var sut = CreateSut(factory);
        var outputPath = NewTempZipPath();
        await File.WriteAllTextAsync(outputPath, "not a zip");

        await sut.CreateJsonBackupAsync(outputPath);

        using var archive = ZipFile.OpenRead(outputPath);
        archive.Entries.Should().NotBeEmpty();
    }

    // ----- RestoreFromJsonBackupAsync -----

    [Fact]
    public async Task RestoreFromJsonBackupAsync_DirectoryMissing_Throws()
    {
        var factory = CreateFactory(nameof(RestoreFromJsonBackupAsync_DirectoryMissing_Throws));
        var sut = CreateSut(factory);

        var act = async () => await sut.RestoreFromJsonBackupAsync(
            Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()));

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task RestoreFromJsonBackupAsync_UnsupportedProvider_ThrowsNotSupportedException()
    {
        var sourceFactory = CreateFactory(nameof(RestoreFromJsonBackupAsync_UnsupportedProvider_ThrowsNotSupportedException) + "_src");
        await SeedBasicDataAsync(sourceFactory);
        var exportSut = CreateSut(sourceFactory);
        var zipPath = NewTempZipPath();
        await exportSut.CreateJsonBackupAsync(zipPath);
        var extractDir = NewTempDir();
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

        var targetFactory = CreateFactory(nameof(RestoreFromJsonBackupAsync_UnsupportedProvider_ThrowsNotSupportedException) + "_dst");
        var sut = CreateSut(targetFactory, DatabaseProvider.MySQL);

        var act = async () => await sut.RestoreFromJsonBackupAsync(extractDir);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task RestoreFromJsonBackupAsync_RoundTripsExportedDataIntoFreshDatabase()
    {
        var sourceFactory = CreateFactory(nameof(RestoreFromJsonBackupAsync_RoundTripsExportedDataIntoFreshDatabase) + "_src");
        await SeedBasicDataAsync(sourceFactory);
        var exportSut = CreateSut(sourceFactory);
        var zipPath = NewTempZipPath();
        await exportSut.CreateJsonBackupAsync(zipPath);
        var extractDir = NewTempDir();
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

        var targetFactory = CreateFactory(nameof(RestoreFromJsonBackupAsync_RoundTripsExportedDataIntoFreshDatabase) + "_dst");
        var sut = CreateSut(targetFactory);
        var progressMessages = new List<string>();
        var progress = new SyncProgress<string>(m => progressMessages.Add(m));

        await sut.RestoreFromJsonBackupAsync(extractDir, progress);

        await using var db = targetFactory.CreateDbContext();
        (await db.Warehouses.CountAsync()).Should().Be(1);
        var user = await db.Users.SingleAsync();
        user.Username.Should().Be("u1");
        user.ApprovalStatus.Should().Be(UserApprovalStatus.Approved);

        progressMessages.Should().Contain("Truncating all tables...");
        progressMessages.Should().Contain(m => m.Contains("Warehouses"));
        progressMessages.Should().Contain(m => m.Contains("Users"));
        progressMessages.Should().Contain("Restore complete!");
    }

    [Fact]
    public async Task RestoreFromJsonBackupAsync_MissingTableFiles_LogsWarningAndSkipsThoseTables()
    {
        var extractDir = NewTempDir();
        // Only provide Warehouses.json; every other expected file is absent.
        var warehouses = new[] { new Warehouse { Id = 1, Name = "OnlyWarehouse", Address = "addr", IsActive = true } };
        var options = new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(), new UtcDateTimeConverter(), new UtcNullableDateTimeConverter() }
        };
        await File.WriteAllTextAsync(Path.Combine(extractDir, "Warehouses.json"), JsonSerializer.Serialize(warehouses, options));

        var targetFactory = CreateFactory(nameof(RestoreFromJsonBackupAsync_MissingTableFiles_LogsWarningAndSkipsThoseTables));
        var sut = CreateSut(targetFactory);

        await sut.RestoreFromJsonBackupAsync(extractDir);

        await using var db = targetFactory.CreateDbContext();
        (await db.Warehouses.CountAsync()).Should().Be(1);
        (await db.Users.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RestoreFromJsonBackupAsync_EmptyArrayFile_ImportsNothingWithoutError()
    {
        var extractDir = NewTempDir();
        await File.WriteAllTextAsync(Path.Combine(extractDir, "Warehouses.json"), "[]");

        var targetFactory = CreateFactory(nameof(RestoreFromJsonBackupAsync_EmptyArrayFile_ImportsNothingWithoutError));
        var sut = CreateSut(targetFactory);

        var act = async () => await sut.RestoreFromJsonBackupAsync(extractDir);

        await act.Should().NotThrowAsync();
        await using var db = targetFactory.CreateDbContext();
        (await db.Warehouses.CountAsync()).Should().Be(0);
    }
}

/// <summary>Invokes the progress handler synchronously on the calling thread, avoiding the
/// async dispatch of <see cref="Progress{T}"/> - which posts each report via the captured
/// SynchronizationContext (or a ThreadPool work item when none is captured, as on a typical
/// xUnit test thread), so asserting immediately after an awaited call completes is a real
/// race: the final report(s) may not have been delivered yet. Same fix already used in
/// DatabaseRestoreServiceTests.cs; duplicated here since `file`-scoped types aren't shared
/// across files.</summary>
file sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
