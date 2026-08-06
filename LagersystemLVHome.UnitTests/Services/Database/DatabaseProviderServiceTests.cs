using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace LagersystemLVHome.UnitTests.Services.Database;

public class DatabaseProviderServiceTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dbprovider-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    private DatabaseProviderService Build(
        DatabaseProvider provider = DatabaseProvider.SQLite,
        string connectionString = "Data Source=test.db",
        string? secureConnectionString = null,
        bool enableRetry = true,
        string? contentRoot = null)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(contentRoot ?? CreateTempDir());

        var settings = new DatabaseSettings
        {
            Provider = provider,
            ConnectionString = connectionString,
            EnableRetryOnFailure = enableRetry,
            CommandTimeout = 30,
            MaxRetryCount = 3
        };

        return new DatabaseProviderService(
            settings, NullLogger<DatabaseProviderService>.Instance, env, secureConnectionString ?? connectionString);
    }

    // ---------- Provider ----------

    [Fact]
    public void Provider_ReflectsConfiguredSettings()
    {
        var sut = Build(DatabaseProvider.PostgreSQL);

        sut.Provider.Should().Be(DatabaseProvider.PostgreSQL);
    }

    // ---------- ConfigureDbContext ----------

    [Fact]
    public void ConfigureDbContext_SQLite_ConfiguresWithoutThrowing()
    {
        var sut = Build(DatabaseProvider.SQLite);
        var builder = new DbContextOptionsBuilder<InventoryDbContext>();

        var act = () => sut.ConfigureDbContext(builder, "Data Source=whatever.db");

        act.Should().NotThrow();
        builder.Options.Should().NotBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfigureDbContext_PostgreSQL_ConfiguresWithoutThrowing_RegardlessOfRetrySetting(bool enableRetry)
    {
        var sut = Build(DatabaseProvider.PostgreSQL, enableRetry: enableRetry);
        var builder = new DbContextOptionsBuilder<InventoryDbContext>();

        var act = () => sut.ConfigureDbContext(builder, "Host=localhost;Database=test;Username=test;Password=test");

        act.Should().NotThrow();
    }

    [Fact]
    public void ConfigureDbContext_UnsupportedProvider_ThrowsNotSupportedException()
    {
        var sut = Build((DatabaseProvider)99);
        var builder = new DbContextOptionsBuilder<InventoryDbContext>();

        var act = () => sut.ConfigureDbContext(builder, "irrelevant");

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*not supported*");
    }

    // NOTE: The MySQL branch of ConfigureDbContext calls ServerVersion.AutoDetect(connectionString),
    // which opens a real connection to detect the server version. That is a genuine external-DB seam -
    // it cannot be exercised deterministically (and fast) without a live MySQL server, so it is left
    // uncovered here by design rather than risking a slow/flaky test against a non-existent server.

    // ---------- EnsureDatabaseExistsAsync ----------

    [Fact]
    public async Task EnsureDatabaseExistsAsync_SQLite_AlwaysReturnsTrue()
    {
        var sut = Build(DatabaseProvider.SQLite);

        (await sut.EnsureDatabaseExistsAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task EnsureDatabaseExistsAsync_UnsupportedProvider_ReturnsFalse()
    {
        var sut = Build((DatabaseProvider)99);

        (await sut.EnsureDatabaseExistsAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task EnsureDatabaseExistsAsync_PostgreSQL_MalformedConnectionString_ReturnsFalse()
    {
        // The NpgsqlConnectionStringBuilder constructor itself throws on a malformed string,
        // which is caught by EnsurePostgreSQLDatabaseExistsAsync's own try/catch - no network needed.
        var sut = Build(DatabaseProvider.PostgreSQL, secureConnectionString: "this is not a valid connection string!!!");

        (await sut.EnsureDatabaseExistsAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task EnsureDatabaseExistsAsync_PostgreSQL_InvalidDatabaseName_ReturnsFalse()
    {
        // Connection string parses fine, but the database name fails IsValidDatabaseName's
        // allow-list regex before any network connection is attempted.
        var sut = Build(DatabaseProvider.PostgreSQL, secureConnectionString: "Host=localhost;Database=bad.name;Username=x;Password=x");

        (await sut.EnsureDatabaseExistsAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task EnsureDatabaseExistsAsync_MySQL_MalformedConnectionString_ReturnsFalse()
    {
        var sut = Build(DatabaseProvider.MySQL, secureConnectionString: "this is not a valid connection string!!!");

        (await sut.EnsureDatabaseExistsAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task EnsureDatabaseExistsAsync_MySQL_InvalidDatabaseName_ReturnsFalse()
    {
        var sut = Build(DatabaseProvider.MySQL, secureConnectionString: "Server=localhost;Database=bad name;Uid=x;Pwd=x;");

        (await sut.EnsureDatabaseExistsAsync()).Should().BeFalse();
    }

    // NOTE: The success branches inside EnsurePostgreSQLDatabaseExistsAsync/EnsureMySQLDatabaseExistsAsync
    // (actually opening the system database and running CREATE DATABASE) require a live Postgres/MySQL
    // server and are not reachable from these unit tests - documented as a seam.

    // ---------- TestConnectionAsync ----------

    [Fact]
    public async Task TestConnectionAsync_SQLite_ValidFile_ReturnsTrue()
    {
        var dir = CreateTempDir();
        var dbFile = Path.Combine(dir, "connect-test.db");
        // EF Core's SqliteDatabaseCreator.CanConnect treats a missing file as "cannot connect"
        // (unlike opening a raw ADO.NET connection, which would auto-create it) - pre-create the
        // file to exercise the genuine "can connect to an existing database" success path.
        await File.WriteAllBytesAsync(dbFile, Array.Empty<byte>());
        var sut = Build(DatabaseProvider.SQLite, secureConnectionString: $"Data Source={dbFile}");

        (await sut.TestConnectionAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task TestConnectionAsync_SQLite_UnreachablePath_ReturnsFalse()
    {
        var sut = Build(DatabaseProvider.SQLite, secureConnectionString: "Data Source=Z:\\definitely\\not\\a\\real\\path\\db.sqlite");

        (await sut.TestConnectionAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectionAsync_UnsupportedProvider_CatchesConfigureDbContextExceptionAndReturnsFalse()
    {
        // ConfigureDbContext throws NotSupportedException for an unrecognized provider; since that
        // call happens inside TestConnectionAsync's own try block, this exercises its outer catch
        // deterministically (no CanConnectAsync/network behavior involved).
        var sut = Build((DatabaseProvider)99);

        (await sut.TestConnectionAsync()).Should().BeFalse();
    }

    // ---------- BackupDatabaseAsync / RestoreDatabaseAsync (SQLite) ----------

    [Fact]
    public async Task BackupDatabaseAsync_SQLite_CopiesSourceFileToBackupPath()
    {
        var dir = CreateTempDir();
        var sourceFile = Path.Combine(dir, "source.db");
        await File.WriteAllTextAsync(sourceFile, "fake-sqlite-content");
        var backupFile = Path.Combine(dir, "backup.db");

        var sut = Build(DatabaseProvider.SQLite, connectionString: $"Data Source={sourceFile}");

        await sut.BackupDatabaseAsync(backupFile);

        File.Exists(backupFile).Should().BeTrue();
        (await File.ReadAllTextAsync(backupFile)).Should().Be("fake-sqlite-content");
    }

    [Fact]
    public async Task BackupDatabaseAsync_SQLite_MissingSourceFile_ThrowsFileNotFoundException()
    {
        var dir = CreateTempDir();
        var sut = Build(DatabaseProvider.SQLite, connectionString: $"Data Source={Path.Combine(dir, "does-not-exist.db")}");

        var act = async () => await sut.BackupDatabaseAsync(Path.Combine(dir, "backup.db"));

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task RestoreDatabaseAsync_SQLite_CopiesBackupFileToTargetPath()
    {
        var dir = CreateTempDir();
        var backupFile = Path.Combine(dir, "backup.db");
        await File.WriteAllTextAsync(backupFile, "restored-content");
        var targetFile = Path.Combine(dir, "target.db");

        var sut = Build(DatabaseProvider.SQLite, connectionString: $"Data Source={targetFile}");

        await sut.RestoreDatabaseAsync(backupFile);

        File.Exists(targetFile).Should().BeTrue();
        (await File.ReadAllTextAsync(targetFile)).Should().Be("restored-content");
    }

    [Fact]
    public async Task RestoreDatabaseAsync_SQLite_MissingBackupFile_ThrowsFileNotFoundException()
    {
        var dir = CreateTempDir();
        var sut = Build(DatabaseProvider.SQLite, connectionString: $"Data Source={Path.Combine(dir, "target.db")}");

        var act = async () => await sut.RestoreDatabaseAsync(Path.Combine(dir, "no-such-backup.db"));

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    // ---------- BackupDatabaseAsync / RestoreDatabaseAsync (PostgreSQL / MySQL) ----------
    // These shell out to pg_dump/pg_restore/mysqldump/mysql. The bundled-tool path
    // (ContentRootPath/Tools/...) never exists in the test sandbox, so they fall back to PATH.
    // Whether or not such a client happens to be installed on the host, a bogus connection
    // string (bad host/port/credentials) makes the operation fail one way or another:
    // either Process.Start throws (tool missing) or the tool itself exits non-zero / throws our
    // wrapped exception (tool present but can't connect). Either way this asserts the legacy
    // methods surface a failure instead of silently succeeding. The success path (a real dump/
    // restore against a live server) is a genuine external-tool seam and is not covered here.

    [Fact]
    public async Task BackupDatabaseAsync_PostgreSQL_WithoutReachableServer_Throws()
    {
        var dir = CreateTempDir();
        var sut = Build(
            DatabaseProvider.PostgreSQL,
            connectionString: "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test",
            contentRoot: dir);

        var act = async () => await sut.BackupDatabaseAsync(Path.Combine(dir, "pg-backup.dump"));

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task BackupDatabaseAsync_MySQL_WithoutReachableServer_Throws()
    {
        var dir = CreateTempDir();
        var sut = Build(
            DatabaseProvider.MySQL,
            connectionString: "Server=127.0.0.1;Port=1;Database=test;Uid=test;Pwd=test",
            contentRoot: dir);

        var act = async () => await sut.BackupDatabaseAsync(Path.Combine(dir, "mysql-backup.sql"));

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task RestoreDatabaseAsync_PostgreSQL_WithoutReachableServer_Throws()
    {
        var dir = CreateTempDir();
        var sut = Build(
            DatabaseProvider.PostgreSQL,
            connectionString: "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test",
            contentRoot: dir);

        var act = async () => await sut.RestoreDatabaseAsync(Path.Combine(dir, "pg-backup.dump"));

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task RestoreDatabaseAsync_MySQL_WithoutReachableServer_Throws()
    {
        var dir = CreateTempDir();
        var sut = Build(
            DatabaseProvider.MySQL,
            connectionString: "Server=127.0.0.1;Port=1;Database=test;Uid=test;Pwd=test",
            contentRoot: dir);

        var act = async () => await sut.RestoreDatabaseAsync(Path.Combine(dir, "mysql-backup.sql"));

        await act.Should().ThrowAsync<Exception>();
    }

    // ---------- Private helpers via reflection (pure, deterministic, no DB needed) ----------

    [Theory]
    [InlineData("valid_name-123", true)]
    [InlineData("bad.name", false)]
    [InlineData("bad name", false)]
    [InlineData("bad;name", false)]
    [InlineData("", false)]
    public void IsValidDatabaseName_ValidatesAllowedCharacterSet(string name, bool expected)
    {
        var sut = Build();
        var method = typeof(DatabaseProviderService).GetMethod("IsValidDatabaseName", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var result = (bool)method.Invoke(sut, new object[] { name })!;

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("simple", "\"simple\"")]
    [InlineData("with\"quote", "\"with\"\"quote\"")]
    public void QuoteIdentifier_EscapesDoubleQuotesAndWraps(string input, string expected)
    {
        var sut = Build();
        var method = typeof(DatabaseProviderService).GetMethod("QuoteIdentifier", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var result = (string)method.Invoke(sut, new object[] { input })!;

        result.Should().Be(expected);
    }
}
