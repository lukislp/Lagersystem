using LagersystemLVHome.Application.Configuration;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Data;

public static class DatabaseMigrationHelper
{
    public static async Task EnsureMissingColumnsAsync(
        InventoryDbContext db, DatabaseProvider provider, ILogger logger)
    {
        var columns = new (string Table, string Column, string SqliteType, string PgType, string MysqlType)[]
        {
            ("UserGamificationStats", "StorageLocationsCreated", "INTEGER NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0", "INT NOT NULL DEFAULT 0"),
            ("UserGamificationStats", "ExportsCompleted", "INTEGER NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0", "INT NOT NULL DEFAULT 0"),
            ("UserGamificationStats", "PasswordChanges", "INTEGER NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0", "INT NOT NULL DEFAULT 0"),
            ("UserGamificationStats", "TwoFactorToggles", "INTEGER NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0", "INT NOT NULL DEFAULT 0"),
            ("UserGamificationStats", "RoomsCreated", "INTEGER NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0", "INT NOT NULL DEFAULT 0"),
            ("BackupSettings", "CreatedAt", "TEXT NOT NULL DEFAULT '2024-01-01T00:00:00Z'", "TIMESTAMP NOT NULL DEFAULT '2024-01-01T00:00:00Z'", "DATETIME NOT NULL DEFAULT '2024-01-01 00:00:00'"),
        };

        foreach (var (table, column, sqliteType, pgType, mysqlType) in columns)
        {
            try
            {
                var exists = await ColumnExistsAsync(db, provider, table, column);
                if (!exists)
                {
                    var sqlType = provider switch
                    {
                        DatabaseProvider.PostgreSQL => pgType,
                        DatabaseProvider.MySQL => mysqlType,
                        _ => sqliteType
                    };

                    var quotedTable = provider == DatabaseProvider.PostgreSQL
                        ? $"\"{table}\"" : table;
                    var quotedColumn = provider == DatabaseProvider.PostgreSQL
                        ? $"\"{column}\"" : column;

                    var sql = $"ALTER TABLE {quotedTable} ADD COLUMN {quotedColumn} {sqlType}";
                    await db.Database.ExecuteSqlRawAsync(sql);
                    logger.LogInformation("Added missing column {Table}.{Column}", table, column);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not add column {Table}.{Column} (may already exist)", table, column);
            }
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        InventoryDbContext db, DatabaseProvider provider, string table, string column)
    {
        try
        {
            var sql = provider switch
            {
                DatabaseProvider.PostgreSQL =>
                    $"SELECT 1 FROM information_schema.columns WHERE table_name = '{table}' AND column_name = '{column}'",
                DatabaseProvider.MySQL =>
                    $"SELECT 1 FROM information_schema.columns WHERE table_name = '{table}' AND column_name = '{column}' AND table_schema = DATABASE()",
                _ =>
                    $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = '{column}'"
            };

            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var result = await cmd.ExecuteScalarAsync();
            return result != null;
        }
        catch
        {
            return false;
        }
    }
}
