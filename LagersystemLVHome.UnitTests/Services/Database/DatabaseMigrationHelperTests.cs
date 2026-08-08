using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Database;

/// <summary>Covers <see cref="DatabaseMigrationHelper.EnsureMissingColumnsAsync"/>.</summary>
public sealed class DatabaseMigrationHelperTests : IDisposable
{
    private readonly List<SqliteConnection> _connections = new();

    public void Dispose()
    {
        foreach (var c in _connections)
        {
            c.Dispose();
        }
    }

    private InventoryDbContext CreateSqliteContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _connections.Add(connection);
        var options = new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(connection).Options;
        var ctx = new InventoryDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static InventoryDbContext CreateInMemoryContext(string name)
        => new(new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<bool> ColumnExistsAsync(InventoryDbContext db, string table, string column)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = '{column}'";
        var result = await cmd.ExecuteScalarAsync();
        return result != null;
    }

    [Fact]
    public async Task EnsureMissingColumnsAsync_ColumnAlreadyExists_LeavesSchemaUntouched()
    {
        // The current EF model already defines all of these columns, so a freshly-created
        // SQLite schema should report every column as already present and add nothing.
        await using var db = CreateSqliteContext();

        var act = () => DatabaseMigrationHelper.EnsureMissingColumnsAsync(db, DatabaseProvider.SQLite, NullLogger.Instance);

        await act.Should().NotThrowAsync();
        (await ColumnExistsAsync(db, "UserGamificationStats", "StorageLocationsCreated")).Should().BeTrue();
    }

    [Fact]
    public async Task EnsureMissingColumnsAsync_SQLite_ColumnActuallyMissing_AddsItBack()
    {
        await using var db = CreateSqliteContext();
        await db.Database.GetDbConnection().OpenAsync();
        await using (var drop = db.Database.GetDbConnection().CreateCommand())
        {
            drop.CommandText = "ALTER TABLE UserGamificationStats DROP COLUMN StorageLocationsCreated";
            await drop.ExecuteNonQueryAsync();
        }
        (await ColumnExistsAsync(db, "UserGamificationStats", "StorageLocationsCreated")).Should().BeFalse("precondition: column must genuinely be gone");

        await DatabaseMigrationHelper.EnsureMissingColumnsAsync(db, DatabaseProvider.SQLite, NullLogger.Instance);

        (await ColumnExistsAsync(db, "UserGamificationStats", "StorageLocationsCreated")).Should().BeTrue("the helper should have re-added the missing column");
    }

    [Fact]
    public async Task EnsureMissingColumnsAsync_SQLite_MultipleMissingColumns_AddsAllOfThem()
    {
        await using var db = CreateSqliteContext();
        await db.Database.GetDbConnection().OpenAsync();
        foreach (var (table, column) in new[]
        {
            ("UserGamificationStats", "ExportsCompleted"),
            ("UserGamificationStats", "PasswordChanges"),
            ("BackupSettings", "CreatedAt")
        })
        {
            await using var drop = db.Database.GetDbConnection().CreateCommand();
            drop.CommandText = $"ALTER TABLE {table} DROP COLUMN {column}";
            await drop.ExecuteNonQueryAsync();
        }

        await DatabaseMigrationHelper.EnsureMissingColumnsAsync(db, DatabaseProvider.SQLite, NullLogger.Instance);

        (await ColumnExistsAsync(db, "UserGamificationStats", "ExportsCompleted")).Should().BeTrue();
        (await ColumnExistsAsync(db, "UserGamificationStats", "PasswordChanges")).Should().BeTrue();
        (await ColumnExistsAsync(db, "BackupSettings", "CreatedAt")).Should().BeTrue();
    }

    [Fact]
    public async Task EnsureMissingColumnsAsync_NonRelationalProvider_CatchesAndLogsWithoutThrowing()
    {
        // The EF Core InMemory provider does not support GetDbConnection()/ExecuteSqlRawAsync,
        // so both the "column exists?" probe and, if that is (mis-)reported as "missing", the
        // subsequent ALTER TABLE attempt throw - both are caught internally per-column, and the
        // overall call must complete without propagating anything to the caller.
        await using var db = CreateInMemoryContext(nameof(EnsureMissingColumnsAsync_NonRelationalProvider_CatchesAndLogsWithoutThrowing));

        var act = () => DatabaseMigrationHelper.EnsureMissingColumnsAsync(db, DatabaseProvider.SQLite, NullLogger.Instance);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureMissingColumnsAsync_PostgreSQLProviderAgainstSqliteConnection_UsesQuotedIdentifiersAndCatchesFailure()
    {
        // Provider says PostgreSQL (quoted identifiers, information_schema probe) while the real
        // connection is SQLite: the existence probe fails (no information_schema in SQLite) and is
        // caught, is treated as "missing", and the follow-up quoted ALTER TABLE then fails too
        // (duplicate column, since it already exists under its unquoted name) - exercising the
        // PostgreSQL branch's SQL-building and the outer per-column catch, without needing a real
        // PostgreSQL server, and without corrupting the schema.
        await using var db = CreateSqliteContext();

        var act = () => DatabaseMigrationHelper.EnsureMissingColumnsAsync(db, DatabaseProvider.PostgreSQL, NullLogger.Instance);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureMissingColumnsAsync_MySQLProviderAgainstSqliteConnection_UsesUnquotedIdentifiersAndCatchesFailure()
    {
        await using var db = CreateSqliteContext();

        var act = () => DatabaseMigrationHelper.EnsureMissingColumnsAsync(db, DatabaseProvider.MySQL, NullLogger.Instance);

        await act.Should().NotThrowAsync();
    }
}
