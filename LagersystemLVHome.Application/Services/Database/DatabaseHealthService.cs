using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Application.Configuration;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace LagersystemLVHome.Application.Services;

public sealed class DatabaseHealthReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string DatabaseProvider { get; set; } = string.Empty;
    public string DatabaseVersion { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public TimeSpan ConnectionLatency { get; set; }

    public long DatabaseSizeBytes { get; set; }
    public string DatabaseSizeFormatted => FormatSize(DatabaseSizeBytes);

    public int TotalTables { get; set; }
    public long TotalRows { get; set; }

    public double AverageQueryTimeMs { get; set; }
    public int ActiveConnections { get; set; }
    public int MaxConnections { get; set; }

    public DateTime? LastBackup { get; set; }
    public DateTime? LastVacuum { get; set; }
    public bool NeedsVacuum { get; set; }

    public int HealthScore { get; set; }
    public string HealthStatus => HealthScore switch
    {
        >= 90 => "Excellent",
        >= 70 => "Good",
        >= 50 => "Fair",
        >= 30 => "Poor",
        _ => "Critical"
    };

    public List<string> Warnings { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

public sealed class ConnectionTestResult
{
    public bool Success { get; set; }
    public TimeSpan Latency { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime TestedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TableStatistics
{
    public string TableName { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public long SizeBytes { get; set; }
    public string SizeFormatted => FormatSize(SizeBytes);
    public long IndexSizeBytes { get; set; }
    public DateTime? LastModified { get; set; }
    public double PercentageOfTotal { get; set; }

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

public sealed class IndexStatistics
{
    public string IndexName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string Columns { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool IsUnique { get; set; }
    public long? ScanCount { get; set; }
    public double? EfficiencyPercent { get; set; }
}

public sealed class SlowQueryInfo
{
    public string Query { get; set; } = string.Empty;
    public double DurationMs { get; set; }
    public DateTime ExecutedAt { get; set; }
    public int CallCount { get; set; }
}

public sealed class DatabaseHealthService : IDatabaseHealthService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly DatabaseSettings _databaseSettings;
    private readonly ILogger<DatabaseHealthService> _logger;

    public DatabaseHealthService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        DatabaseSettings databaseSettings,
        ILogger<DatabaseHealthService> logger)
    {
        _contextFactory = contextFactory;
        _databaseSettings = databaseSettings;
        _logger = logger;
    }

    public async Task<DatabaseHealthReport> GetHealthReportAsync(CancellationToken cancellationToken = default)
    {
        var report = new DatabaseHealthReport
        {
            DatabaseProvider = _databaseSettings.Provider.ToString()
        };

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // 1. Connection test
            var connectionTest = await TestConnectionAsync();
            report.IsConnected = connectionTest.Success;
            report.ConnectionLatency = connectionTest.Latency;

            if (!connectionTest.Success)
            {
                report.HealthScore = 0;
                report.Warnings.Add($"Database connection failed: {connectionTest.ErrorMessage}");
                return report;
            }

            // 2. Database version
            report.DatabaseVersion = await GetDatabaseVersionAsync(context);

            // 3. Table statistics
            var tableStats = await GetTableStatisticsAsync();
            report.TotalTables = tableStats.Count;
            report.TotalRows = tableStats.Sum(t => t.RowCount);
            report.DatabaseSizeBytes = tableStats.Sum(t => t.SizeBytes + t.IndexSizeBytes);

            // 4. Connection and performance metrics
            var (activeConnections, maxConnections) = await GetConnectionStatsAsync(context);
            report.ActiveConnections = activeConnections;
            report.MaxConnections = maxConnections;

            // 5. Query performance
            report.AverageQueryTimeMs = await GetAverageQueryTimeAsync(context);

            // 6. Calculate health score
            report.HealthScore = CalculateHealthScore(report, tableStats);

            // 7. Generate recommendations
            GenerateRecommendations(report, tableStats);

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating database health report");
            report.IsConnected = false;
            report.HealthScore = 0;
            report.Warnings.Add($"Error generating report: {ex.Message}");
            return report;
        }
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            _ = await context.Database.ExecuteSqlRawAsync("SELECT 1");

            sw.Stop();
            return new ConnectionTestResult
            {
                Success = true,
                Latency = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Database connection test failed");
            return new ConnectionTestResult
            {
                Success = false,
                Latency = sw.Elapsed,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<List<TableStatistics>> GetTableStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var stats = new List<TableStatistics>();

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var provider = _databaseSettings.Provider;

            switch (provider)
            {
                case DatabaseProvider.PostgreSQL:
                    stats = await GetPostgreSqlTableStatsAsync(context);
                    break;
                case DatabaseProvider.MySQL:
                    stats = await GetMySqlTableStatsAsync(context);
                    break;
                case DatabaseProvider.SQLite:
                    stats = await GetSqliteTableStatsAsync(context);
                    break;
                default:
                    stats = await GetGenericTableStatsAsync(context);
                    break;
            }

            // Calculate percentage
            var totalSize = stats.Sum(s => s.SizeBytes);
            foreach (var stat in stats)
            {
                stat.PercentageOfTotal = totalSize > 0 ? (double)stat.SizeBytes / totalSize * 100 : 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting table statistics");
        }

        return stats.OrderByDescending(s => s.SizeBytes).ToList();
    }

    public async Task<List<IndexStatistics>> GetIndexStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var stats = new List<IndexStatistics>();

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var provider = _databaseSettings.Provider;

            switch (provider)
            {
                case DatabaseProvider.PostgreSQL:
                    stats = await GetPostgreSqlIndexStatsAsync(context);
                    break;
                case DatabaseProvider.MySQL:
                    stats = await GetMySqlIndexStatsAsync(context);
                    break;
                default:
                    // SQLite and others have limited index statistics
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting index statistics");
        }

        return stats;
    }

    public async Task<List<SlowQueryInfo>> GetSlowQueriesAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        // Requires pg_stat_statements or similar extension
        return new List<SlowQueryInfo>();
    }

    private async Task<string> GetDatabaseVersionAsync(InventoryDbContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var provider = _databaseSettings.Provider;

            var versionQuery = provider switch
            {
                DatabaseProvider.PostgreSQL => "SELECT version()",
                DatabaseProvider.MySQL => "SELECT VERSION()",
                DatabaseProvider.SQLite => "SELECT sqlite_version()",
                _ => null
            };

            if (versionQuery == null)
                return "Unknown";

            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = versionQuery;
            var result = await command.ExecuteScalarAsync();

            return result?.ToString() ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    private async Task<List<TableStatistics>> GetPostgreSqlTableStatsAsync(InventoryDbContext context, CancellationToken cancellationToken = default)
    {
        var stats = new List<TableStatistics>();

        var query = @"
            SELECT 
                relname as table_name,
                n_live_tup as row_count,
                pg_total_relation_size(quote_ident(relname)) as total_size,
                pg_indexes_size(quote_ident(relname)) as index_size
            FROM pg_stat_user_tables
            ORDER BY pg_total_relation_size(quote_ident(relname)) DESC";

        try
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = query;

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                stats.Add(new TableStatistics
                {
                    TableName = reader.GetString(0),
                    RowCount = reader.GetInt64(1),
                    SizeBytes = reader.GetInt64(2),
                    IndexSizeBytes = reader.GetInt64(3)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting PostgreSQL table stats");
        }

        return stats;
    }

    private async Task<List<TableStatistics>> GetMySqlTableStatsAsync(InventoryDbContext context, CancellationToken cancellationToken = default)
    {
        var stats = new List<TableStatistics>();

        var query = @"
            SELECT 
                TABLE_NAME,
                TABLE_ROWS,
                DATA_LENGTH + INDEX_LENGTH as total_size,
                INDEX_LENGTH
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
            ORDER BY DATA_LENGTH + INDEX_LENGTH DESC";

        try
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = query;

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                stats.Add(new TableStatistics
                {
                    TableName = reader.GetString(0),
                    RowCount = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                    SizeBytes = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    IndexSizeBytes = reader.IsDBNull(3) ? 0 : reader.GetInt64(3)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting MySQL table stats");
        }

        return stats;
    }

    private async Task<List<TableStatistics>> GetSqliteTableStatsAsync(InventoryDbContext context, CancellationToken cancellationToken = default)
    {
        var stats = new List<TableStatistics>();

        try
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            // Get all table names
            using var tableCommand = connection.CreateCommand();
            tableCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";

            var tableNames = new List<string>();
            using (var reader = await tableCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    tableNames.Add(reader.GetString(0));
                }
            }

            // Count rows per table
            foreach (var tableName in tableNames)
            {
                try
                {
                    using var countCommand = connection.CreateCommand();
                    countCommand.CommandText = $"SELECT COUNT(*) FROM \"{tableName}\"";
                    var count = Convert.ToInt64(await countCommand.ExecuteScalarAsync());

                    stats.Add(new TableStatistics
                    {
                        TableName = tableName,
                        RowCount = count,
                        // SQLite has no direct size info - estimate based on rows
                        SizeBytes = count * 100
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error counting rows in table {Table}", tableName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting SQLite table stats");
        }

        return stats;
    }

    private async Task<List<TableStatistics>> GetGenericTableStatsAsync(InventoryDbContext context, CancellationToken cancellationToken = default)
    {
        var stats = new List<TableStatistics>();

        try
        {
            stats.Add(new TableStatistics { TableName = "Users", RowCount = await context.Users.CountAsync(cancellationToken) });
            stats.Add(new TableStatistics { TableName = "Products", RowCount = await context.Products.CountAsync(cancellationToken) });
            stats.Add(new TableStatistics { TableName = "Categories", RowCount = await context.Categories.CountAsync(cancellationToken) });
            stats.Add(new TableStatistics { TableName = "StockMovements", RowCount = await context.StockMovements.CountAsync(cancellationToken) });
            stats.Add(new TableStatistics { TableName = "AuditLogs", RowCount = await context.AuditLogs.CountAsync(cancellationToken) });
            stats.Add(new TableStatistics { TableName = "Notifications", RowCount = await context.Notifications.CountAsync(cancellationToken) });
            stats.Add(new TableStatistics { TableName = "UserSessions", RowCount = await context.UserSessions.CountAsync(cancellationToken) });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting generic table stats");
        }

        return stats;
    }

    private async Task<List<IndexStatistics>> GetPostgreSqlIndexStatsAsync(InventoryDbContext context, CancellationToken cancellationToken = default)
    {
        var stats = new List<IndexStatistics>();

        var query = @"
            SELECT 
                indexrelname as index_name,
                relname as table_name,
                pg_relation_size(indexrelid) as index_size,
                idx_scan as scan_count
            FROM pg_stat_user_indexes
            ORDER BY pg_relation_size(indexrelid) DESC
            LIMIT 50";

        try
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = query;

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                stats.Add(new IndexStatistics
                {
                    IndexName = reader.GetString(0),
                    TableName = reader.GetString(1),
                    SizeBytes = reader.GetInt64(2),
                    ScanCount = reader.IsDBNull(3) ? null : reader.GetInt64(3)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting PostgreSQL index stats");
        }

        return stats;
    }

    private async Task<List<IndexStatistics>> GetMySqlIndexStatsAsync(InventoryDbContext context, CancellationToken cancellationToken = default)
    {
        var stats = new List<IndexStatistics>();

        var query = @"
            SELECT 
                INDEX_NAME,
                TABLE_NAME,
                GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) as columns,
                SUM(CARDINALITY) as cardinality
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
            GROUP BY INDEX_NAME, TABLE_NAME
            ORDER BY cardinality DESC
            LIMIT 50";

        try
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = query;

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                stats.Add(new IndexStatistics
                {
                    IndexName = reader.GetString(0),
                    TableName = reader.GetString(1),
                    Columns = reader.IsDBNull(2) ? "" : reader.GetString(2)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting MySQL index stats");
        }

        return stats;
    }

    private async Task<DateTime?> GetLastBackupDateAsync(InventoryDbContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var lastBackup = await context.BackupHistory
                .Where(b => b.Status == LagersystemLVHome.Domain.Models.BackupStatus.Success)
                .OrderByDescending(b => b.BackupDate)
                .Select(b => b.BackupDate)
                .FirstOrDefaultAsync(cancellationToken);

            return lastBackup == default ? null : lastBackup;
        }
        catch
        {
            return null;
        }
    }

    private int CalculateHealthScore(DatabaseHealthReport report, List<TableStatistics> tableStats)
    {
        var score = 100;

        // Connection score (20 points)
        if (!report.IsConnected)
        {
            return 0;
        }

        // Latency score (25 points)
        if (report.ConnectionLatency.TotalMilliseconds > 1000)
        {
            score -= 25;
            report.Warnings.Add($"Kritische Latenz: {report.ConnectionLatency.TotalMilliseconds:F0}ms (>1000ms)");
        }
        else if (report.ConnectionLatency.TotalMilliseconds > 500)
        {
            score -= 15;
            report.Warnings.Add($"Hohe Latenz: {report.ConnectionLatency.TotalMilliseconds:F0}ms (>500ms)");
        }
        else if (report.ConnectionLatency.TotalMilliseconds > 200)
        {
            score -= 8;
        }
        else if (report.ConnectionLatency.TotalMilliseconds > 100)
        {
            score -= 3;
        }

        // Database size score (20 points)
        var sizeGB = report.DatabaseSizeBytes / (1024.0 * 1024.0 * 1024.0);
        if (sizeGB > 50)
        {
            score -= 20;
            report.Warnings.Add($"Sehr grosse Datenbank: {sizeGB:F1} GB");
        }
        else if (sizeGB > 20)
        {
            score -= 15;
            report.Warnings.Add($"Grosse Datenbank: {sizeGB:F1} GB - Archivierung empfohlen");
        }
        else if (sizeGB > 10)
        {
            score -= 10;
        }
        else if (sizeGB > 5)
        {
            score -= 5;
        }

        // Table analysis (20 points)
        var largeTables = tableStats.Where(t => t.RowCount > 1000000).ToList();
        if (largeTables.Count > 5)
        {
            score -= 15;
            report.Warnings.Add($"{largeTables.Count} Tabellen mit mehr als 1 Mio. Zeilen");
        }
        else if (largeTables.Count > 2)
        {
            score -= 10;
            report.Warnings.Add($"{largeTables.Count} grosse Tabellen (>1 Mio. Zeilen)");
        }
        else if (largeTables.Any())
        {
            score -= 5;
        }

        // Index size ratio (15 points)
        var totalDataSize = tableStats.Sum(t => t.SizeBytes);
        var totalIndexSize = tableStats.Sum(t => t.IndexSizeBytes);
        if (totalDataSize > 0)
        {
            var indexRatio = (double)totalIndexSize / totalDataSize * 100;
            if (indexRatio > 200)
            {
                score -= 15;
                report.Warnings.Add($"Index-Overhead sehr hoch: {indexRatio:F0}% der Datengroesse");
            }
            else if (indexRatio > 100)
            {
                score -= 8;
            }
            else if (indexRatio < 10 && totalDataSize > 100 * 1024 * 1024)
            {
                score -= 5;
                report.Warnings.Add("Wenige Indizes - Performance koennte verbessert werden");
            }
        }

        return Math.Max(0, Math.Min(100, score));
    }

    private void GenerateRecommendations(DatabaseHealthReport report, List<TableStatistics> tableStats)
    {
        // Latency recommendations
        if (report.ConnectionLatency.TotalMilliseconds > 200)
        {
            report.Recommendations.Add("Pruefen Sie die Netzwerkverbindung zur Datenbank");
        }

        // Size recommendations
        var sizeGB = report.DatabaseSizeBytes / (1024.0 * 1024.0 * 1024.0);
        if (sizeGB > 10)
        {
            report.Recommendations.Add("Erwaegen Sie eine Archivierung alter Daten");
        }

        // Audit log cleanup
        var auditLogTable = tableStats.FirstOrDefault(t =>
            t.TableName.Equals("AuditLogs", StringComparison.OrdinalIgnoreCase) ||
            t.TableName.Equals("auditlogs", StringComparison.OrdinalIgnoreCase));
        if (auditLogTable != null && auditLogTable.RowCount > 100000)
        {
            report.Recommendations.Add($"Audit-Logs bereinigen ({auditLogTable.RowCount:N0} Eintraege)");
        }

        // Session cleanup
        var sessionTable = tableStats.FirstOrDefault(t =>
            t.TableName.Equals("UserSessions", StringComparison.OrdinalIgnoreCase) ||
            t.TableName.Equals("usersessions", StringComparison.OrdinalIgnoreCase));
        if (sessionTable != null && sessionTable.RowCount > 10000)
        {
            report.Recommendations.Add($"Session-Bereinigung durchfuehren ({sessionTable.RowCount:N0} Sessions)");
        }

        // Notification cleanup
        var notificationTable = tableStats.FirstOrDefault(t =>
            t.TableName.Equals("Notifications", StringComparison.OrdinalIgnoreCase) ||
            t.TableName.Equals("notifications", StringComparison.OrdinalIgnoreCase));
        if (notificationTable != null && notificationTable.RowCount > 50000)
        {
            report.Recommendations.Add($"Alte Benachrichtigungen loeschen ({notificationTable.RowCount:N0} Eintraege)");
        }

        // PostgreSQL-specific recommendations
        if (_databaseSettings.Provider == DatabaseProvider.PostgreSQL)
        {
            if (sizeGB > 1)
            {
                report.Recommendations.Add("Fuehren Sie regelmaessig VACUUM ANALYZE aus");
            }
        }

        // MySQL-specific recommendations
        if (_databaseSettings.Provider == DatabaseProvider.MySQL)
        {
            if (sizeGB > 5)
            {
                report.Recommendations.Add("Erwaegen Sie OPTIMIZE TABLE fuer grosse Tabellen");
            }
        }

        // General performance recommendations
        var totalRows = tableStats.Sum(t => t.RowCount);
        if (totalRows > 10000000)
        {
            report.Recommendations.Add("Bei grossen Datenmengen: Partitionierung in Betracht ziehen");
        }
    }

    private async Task<(int active, int max)> GetConnectionStatsAsync(InventoryDbContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            var provider = _databaseSettings.Provider;

            switch (provider)
            {
                case DatabaseProvider.PostgreSQL:
                    return await GetPostgreSqlConnectionStatsAsync(connection);
                case DatabaseProvider.MySQL:
                    return await GetMySqlConnectionStatsAsync(connection);
                default:
                    return (0, 0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting connection stats");
            return (0, 0);
        }
    }

    private async Task<(int active, int max)> GetPostgreSqlConnectionStatsAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            using var activeCmd = connection.CreateCommand();
            activeCmd.CommandText = "SELECT COUNT(*) FROM pg_stat_activity WHERE state = 'active'";
            var activeResult = await activeCmd.ExecuteScalarAsync();
            var active = Convert.ToInt32(activeResult ?? 0);

            using var maxCmd = connection.CreateCommand();
            maxCmd.CommandText = "SHOW max_connections";
            var maxResult = await maxCmd.ExecuteScalarAsync();
            var max = Convert.ToInt32(maxResult ?? 0);

            return (active, max);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting PostgreSQL connection stats");
            return (0, 0);
        }
    }

    private async Task<(int active, int max)> GetMySqlConnectionStatsAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            using var activeCmd = connection.CreateCommand();
            activeCmd.CommandText = "SHOW STATUS LIKE 'Threads_connected'";
            int active = 0;
            using (var reader = await activeCmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    active = Convert.ToInt32(reader.GetValue(1));
                }
            }

            using var maxCmd = connection.CreateCommand();
            maxCmd.CommandText = "SHOW VARIABLES LIKE 'max_connections'";
            int max = 0;
            using (var reader = await maxCmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    max = Convert.ToInt32(reader.GetValue(1));
                }
            }

            return (active, max);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting MySQL connection stats");
            return (0, 0);
        }
    }

    private async Task<double> GetAverageQueryTimeAsync(InventoryDbContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            var provider = _databaseSettings.Provider;

            switch (provider)
            {
                case DatabaseProvider.PostgreSQL:
                    return await GetPostgreSqlAvgQueryTimeAsync(connection);
                case DatabaseProvider.MySQL:
                    return await GetMySqlAvgQueryTimeAsync(connection);
                default:
                    return 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting average query time");
            return 0;
        }
    }

    private async Task<double> GetPostgreSqlAvgQueryTimeAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            // Average query time from pg_stat_statements (if available)
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(
                    (SELECT AVG(mean_exec_time) 
                     FROM pg_stat_statements 
                     WHERE calls > 10 
                     AND query NOT LIKE '%pg_stat%'),
                    0
                )";

            try
            {
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToDouble(result ?? 0);
            }
            catch
            {
                // pg_stat_statements not installed - use alternative metric
                using var altCmd = connection.CreateCommand();
                altCmd.CommandText = @"
                    SELECT EXTRACT(EPOCH FROM (NOW() - backend_start)) * 1000 / GREATEST(1, 
                        (SELECT SUM(calls) FROM pg_stat_user_tables))
                    FROM pg_stat_activity 
                    WHERE state = 'active' 
                    LIMIT 1";

                try
                {
                    var altResult = await altCmd.ExecuteScalarAsync();
                    return Math.Min(Convert.ToDouble(altResult ?? 0), 1000);
                }
                catch
                {
                    return 0;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting PostgreSQL avg query time");
            return 0;
        }
    }

    private async Task<double> GetMySqlAvgQueryTimeAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    (SELECT VARIABLE_VALUE FROM performance_schema.global_status WHERE VARIABLE_NAME = 'Questions') /
                    GREATEST(1, (SELECT VARIABLE_VALUE FROM performance_schema.global_status WHERE VARIABLE_NAME = 'Uptime'))
                    * 1000";

            try
            {
                var result = await cmd.ExecuteScalarAsync();
                var queriesPerSec = Convert.ToDouble(result ?? 0);
                return queriesPerSec > 0 ? Math.Min(1000 / queriesPerSec, 500) : 0;
            }
            catch
            {
                return 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting MySQL avg query time");
            return 0;
        }
    }
}
