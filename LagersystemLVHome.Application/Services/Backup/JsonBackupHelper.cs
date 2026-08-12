using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Npgsql;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// JSON-based backup and restore helper.
/// Creates readable JSON backups and restores them using TRUNCATE CASCADE.
/// </summary>
public sealed class JsonBackupHelper
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<JsonBackupHelper> _logger;
    private readonly DatabaseSettings _databaseSettings;

    public JsonBackupHelper(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<JsonBackupHelper> logger,
        IOptions<DatabaseSettings> databaseSettings)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _databaseSettings = databaseSettings.Value;
    }

    public async Task CreateJsonBackupAsync(string outputZipPath, CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"backup_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            _logger.LogInformation("Creating JSON backup...");

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            // Export all tables as JSON (order matters due to foreign keys)
            var tableCounts = new Dictionary<string, int>();

            // Phase 1: Independent tables (no foreign keys)
            tableCounts["Warehouses"] = await ExportTableAsync(context, context.Warehouses, Path.Combine(tempDir, "Warehouses.json"), ct);

            // Phase 2: Tables depending only on phase 1
            tableCounts["Users"] = await ExportTableAsync(context, context.Users, Path.Combine(tempDir, "Users.json"), ct);
            tableCounts["Categories"] = await ExportTableAsync(context, context.Categories, Path.Combine(tempDir, "Categories.json"), ct);
            tableCounts["Rooms"] = await ExportTableAsync(context, context.Rooms, Path.Combine(tempDir, "Rooms.json"), ct);

            // Phase 3: Tables depending on phase 2
            tableCounts["StorageLocations"] = await ExportTableAsync(context, context.StorageLocations, Path.Combine(tempDir, "StorageLocations.json"), ct);
            tableCounts["Products"] = await ExportTableAsync(context, context.Products, Path.Combine(tempDir, "Products.json"), ct);
            tableCounts["ApiKeys"] = await ExportTableAsync(context, context.ApiKeys, Path.Combine(tempDir, "ApiKeys.json"), ct);
            tableCounts["Notifications"] = await ExportTableAsync(context, context.Notifications, Path.Combine(tempDir, "Notifications.json"), ct);

            // Phase 3b: User-related tables
            tableCounts["PasswordResetTokens"] = await ExportTableAsync(context, context.PasswordResetTokens, Path.Combine(tempDir, "PasswordResetTokens.json"), ct);
            tableCounts["UserNotificationSettings"] = await ExportTableAsync(context, context.UserNotificationSettings, Path.Combine(tempDir, "UserNotificationSettings.json"), ct);

            // Phase 3c: Session management tables
            tableCounts["UserSessions"] = await ExportTableAsync(context, context.UserSessions, Path.Combine(tempDir, "UserSessions.json"), ct);
            tableCounts["SessionActivities"] = await ExportTableAsync(context, context.SessionActivities, Path.Combine(tempDir, "SessionActivities.json"), ct);
            tableCounts["SecurityEvents"] = await ExportTableAsync(context, context.SecurityEvents, Path.Combine(tempDir, "SecurityEvents.json"), ct);

            // Phase 4: Tables depending on phase 3
            tableCounts["ProductBatches"] = await ExportTableAsync(context, context.ProductBatches, Path.Combine(tempDir, "ProductBatches.json"), ct);
            tableCounts["ProductStorageLocations"] = await ExportTableAsync(context, context.ProductStorageLocations, Path.Combine(tempDir, "ProductStorageLocations.json"), ct);
            tableCounts["StockMovements"] = await ExportTableAsync(context, context.StockMovements, Path.Combine(tempDir, "StockMovements.json"), ct);
            tableCounts["ProductPrices"] = await ExportTableAsync(context, context.ProductPrices, Path.Combine(tempDir, "ProductPrices.json"), ct);

            // Phase 5: Audit and insights tables
            tableCounts["AuditLogs"] = await ExportTableAsync(context, context.AuditLogs, Path.Combine(tempDir, "AuditLogs.json"), ct);
            tableCounts["PageViews"] = await ExportTableAsync(context, context.PageViews, Path.Combine(tempDir, "PageViews.json"), ct);
            tableCounts["ApiRequests"] = await ExportTableAsync(context, context.ApiRequests, Path.Combine(tempDir, "ApiRequests.json"), ct);
            tableCounts["PerformanceMetrics"] = await ExportTableAsync(context, context.PerformanceMetrics, Path.Combine(tempDir, "PerformanceMetrics.json"), ct);
            tableCounts["UserActivities"] = await ExportTableAsync(context, context.UserActivities, Path.Combine(tempDir, "UserActivities.json"), ct);

            // Phase 6: Backup system tables
            tableCounts["BackupSettings"] = await ExportTableAsync(context, context.BackupSettings, Path.Combine(tempDir, "BackupSettings.json"), ct);
            tableCounts["BackupProviders"] = await ExportTableAsync(context, context.BackupProviders, Path.Combine(tempDir, "BackupProviders.json"), ct);
            tableCounts["BackupHistory"] = await ExportTableAsync(context, context.BackupHistory, Path.Combine(tempDir, "BackupHistory.json"), ct);
            tableCounts["KeyBackupHistory"] = await ExportTableAsync(context, context.KeyBackupHistory, Path.Combine(tempDir, "KeyBackupHistory.json"), ct);

            // Phase 7: GDPR and system settings
            tableCounts["GdprCleanupHistory"] = await ExportTableAsync(context, context.GdprCleanupHistory, Path.Combine(tempDir, "GdprCleanupHistory.json"), ct);
            tableCounts["SystemSettings"] = await ExportTableAsync(context, context.SystemSettings, Path.Combine(tempDir, "SystemSettings.json"), ct);

            // Metadata
            var metadata = new BackupMetadata
            {
                BackupDate = DateTime.UtcNow,
                DatabaseProvider = _databaseSettings.Provider.ToString(),
                Version = "1.1",
                BackupType = "JSON",
                TableCounts = tableCounts
            };

            var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(tempDir, "metadata.json"), metadataJson, ct);

            // Create ZIP archive
            if (File.Exists(outputZipPath))
                File.Delete(outputZipPath);

            ZipFile.CreateFromDirectory(tempDir, outputZipPath);

            _logger.LogInformation("JSON Backup created: {Path} ({Tables} tables, {Records} records)",
                outputZipPath,
                tableCounts.Count,
                tableCounts.Values.Sum());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Restores from a JSON backup using TRUNCATE CASCADE.
    /// Accepts an already-extracted directory (not a ZIP file).
    /// </summary>
    public async Task RestoreFromJsonBackupAsync(string extractedDirectory, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting JSON restore from directory: {Directory}", extractedDirectory);

        try
        {
            if (!Directory.Exists(extractedDirectory))
            {
                throw new DirectoryNotFoundException($"Backup directory not found: {extractedDirectory}");
            }

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            // Truncate all tables in reverse order (respecting foreign keys)
            progress?.Report("Truncating all tables...");
            await TruncateAllTablesAsync(ct);

            // Import in dependency order (foreign keys)
            // Phase 1: Independent tables first
            progress?.Report("Importing Warehouses...");
            await ImportTableAsync<Warehouse>(context, context.Warehouses, Path.Combine(extractedDirectory, "Warehouses.json"), ct);

            // Phase 2: Tables depending only on phase 1
            progress?.Report("Importing Users...");
            await ImportTableAsync<User>(context, context.Users, Path.Combine(extractedDirectory, "Users.json"), ct);

            // Phase 2b: User-related tables
            progress?.Report("Importing PasswordResetTokens...");
            await ImportTableAsync<PasswordResetToken>(context, context.PasswordResetTokens, Path.Combine(extractedDirectory, "PasswordResetTokens.json"), ct);

            progress?.Report("Importing UserNotificationSettings...");
            await ImportTableAsync<UserNotificationSettings>(context, context.UserNotificationSettings, Path.Combine(extractedDirectory, "UserNotificationSettings.json"), ct);

            // Phase 2c: Session management tables
            progress?.Report("Importing UserSessions...");
            await ImportTableAsync<Domain.Models.UserSession>(context, context.UserSessions, Path.Combine(extractedDirectory, "UserSessions.json"), ct);

            progress?.Report("Importing SessionActivities...");
            await ImportTableAsync<SessionActivity>(context, context.SessionActivities, Path.Combine(extractedDirectory, "SessionActivities.json"), ct);

            progress?.Report("Importing SecurityEvents...");
            await ImportTableAsync<SecurityEvent>(context, context.SecurityEvents, Path.Combine(extractedDirectory, "SecurityEvents.json"), ct);

            progress?.Report("Importing Categories...");
            await ImportTableAsync<Category>(context, context.Categories, Path.Combine(extractedDirectory, "Categories.json"), ct);

            progress?.Report("Importing Rooms...");
            await ImportTableAsync<Room>(context, context.Rooms, Path.Combine(extractedDirectory, "Rooms.json"), ct);

            // Phase 3: Tables depending on phase 2
            progress?.Report("Importing StorageLocations...");
            await ImportTableAsync<StorageLocation>(context, context.StorageLocations, Path.Combine(extractedDirectory, "StorageLocations.json"), ct);

            progress?.Report("Importing Products...");
            await ImportTableAsync<Product>(context, context.Products, Path.Combine(extractedDirectory, "Products.json"), ct);

            progress?.Report("Importing ApiKeys...");
            await ImportTableAsync<ApiKey>(context, context.ApiKeys, Path.Combine(extractedDirectory, "ApiKeys.json"), ct);

            progress?.Report("Importing Notifications...");
            await ImportTableAsync<Notification>(context, context.Notifications, Path.Combine(extractedDirectory, "Notifications.json"), ct);

            // Phase 4: Tables depending on phase 3
            progress?.Report("Importing ProductBatches...");
            await ImportTableAsync<ProductBatch>(context, context.ProductBatches, Path.Combine(extractedDirectory, "ProductBatches.json"), ct);

            progress?.Report("Importing ProductStorageLocations...");
            await ImportTableAsync<ProductStorageLocation>(context, context.ProductStorageLocations, Path.Combine(extractedDirectory, "ProductStorageLocations.json"), ct);

            progress?.Report("Importing StockMovements...");
            await ImportTableAsync<StockMovement>(context, context.StockMovements, Path.Combine(extractedDirectory, "StockMovements.json"), ct);

            progress?.Report("Importing ProductPrices...");
            await ImportTableAsync<ProductPrice>(context, context.ProductPrices, Path.Combine(extractedDirectory, "ProductPrices.json"), ct);

            // Phase 5: Audit and insights tables
            progress?.Report("Importing AuditLogs...");
            await ImportTableAsync<AuditLog>(context, context.AuditLogs, Path.Combine(extractedDirectory, "AuditLogs.json"), ct);

            progress?.Report("Importing PageViews...");
            await ImportTableAsync<PageView>(context, context.PageViews, Path.Combine(extractedDirectory, "PageViews.json"), ct);

            progress?.Report("Importing ApiRequests...");
            await ImportTableAsync<ApiRequest>(context, context.ApiRequests, Path.Combine(extractedDirectory, "ApiRequests.json"), ct);

            progress?.Report("Importing PerformanceMetrics...");
            await ImportTableAsync<PerformanceMetric>(context, context.PerformanceMetrics, Path.Combine(extractedDirectory, "PerformanceMetrics.json"), ct);

            progress?.Report("Importing UserActivities...");
            await ImportTableAsync<UserActivity>(context, context.UserActivities, Path.Combine(extractedDirectory, "UserActivities.json"), ct);

            // Phase 6: Backup system tables
            progress?.Report("Importing BackupSettings...");
            await ImportTableAsync<Domain.Models.BackupSettings>(context, context.BackupSettings, Path.Combine(extractedDirectory, "BackupSettings.json"), ct);

            progress?.Report("Importing BackupProviders...");
            await ImportTableAsync<BackupProvider>(context, context.BackupProviders, Path.Combine(extractedDirectory, "BackupProviders.json"), ct);

            progress?.Report("Importing BackupHistory...");
            await ImportTableAsync<BackupHistory>(context, context.BackupHistory, Path.Combine(extractedDirectory, "BackupHistory.json"), ct);

            progress?.Report("Importing KeyBackupHistory...");
            await ImportTableAsync<KeyBackupHistory>(context, context.KeyBackupHistory, Path.Combine(extractedDirectory, "KeyBackupHistory.json"), ct);

            // Phase 7: GDPR and system settings
            progress?.Report("Importing GdprCleanupHistory...");
            await ImportTableAsync<GdprCleanupHistory>(context, context.GdprCleanupHistory, Path.Combine(extractedDirectory, "GdprCleanupHistory.json"), ct);

            progress?.Report("Importing SystemSettings...");
            await ImportTableAsync<SystemSetting>(context, context.SystemSettings, Path.Combine(extractedDirectory, "SystemSettings.json"), ct);

            // Reset PostgreSQL sequences automatically after restore
            if (_databaseSettings.Provider == DatabaseProvider.PostgreSQL)
            {
                progress?.Report("Resetting PostgreSQL sequences...");
                await ResetAllPostgreSQLSequencesAsync(ct);
                _logger.LogInformation("All PostgreSQL sequences reset successfully");
            }

            progress?.Report("Restore complete!");
            _logger.LogInformation("JSON Restore completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JSON restore failed");
            throw;
        }
    }

    /// <summary>
    /// Resets all PostgreSQL sequences to MAX(Id) + 1 after a restore.
    /// </summary>
    private async Task ResetAllPostgreSQLSequencesAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_databaseSettings.ConnectionString);
        await connection.OpenAsync(ct);

        var tables = new[]
        {
            "PageViews",
            "Users",
            "Products",
            "Categories",
            "Warehouses",
            "StorageLocations",
            "Rooms",
            "StockMovements",
            "ProductBatches",
            "ProductStorageLocations",
            "ProductPrices",
            "Notifications",
            "AuditLogs",
            "ApiKeys",
            "PasswordResetTokens",
            "ApiRequests",
            "PerformanceMetrics",
            "UserActivities",
            "BackupProviders",
            "BackupHistory",
            "BackupSettings",
            "SystemSettings",
            "UserNotificationSettings",
            "UserSessions",
            "SessionActivities",
            "SecurityEvents",
            "GdprCleanupHistory",
            "KeyBackupHistory"
        };

        foreach (var table in tables)
        {
            try
            {
                // Set sequence to MAX(Id) + 1 (or 1 if the table is empty)
                var sql = $@"
SELECT setval(
    pg_get_serial_sequence('public.""{table}""', 'Id'), 
    COALESCE((SELECT MAX(""Id"") FROM public.""{table}""), 1), 
    true
)";

                await using var cmd = new NpgsqlCommand(sql, connection);
                await cmd.ExecuteScalarAsync(ct);

                _logger.LogInformation("Sequence reset: {Table}", table);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not reset sequence for table: {Table}", table);
            }
        }
    }

    /// <summary>
    /// Truncates all tables in the correct order (reverse for foreign keys).
    /// </summary>
    private async Task TruncateAllTablesAsync(CancellationToken ct)
    {
        if (_databaseSettings.Provider == DatabaseProvider.PostgreSQL)
        {
            await TruncatePostgreSQLAsync(ct);
        }
        else if (_databaseSettings.Provider == DatabaseProvider.SQLite)
        {
            await TruncateSQLiteAsync(ct);
        }
        else
        {
            throw new NotSupportedException($"TRUNCATE not implemented for {_databaseSettings.Provider}");
        }
    }

    private async Task TruncatePostgreSQLAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_databaseSettings.ConnectionString);
        await connection.OpenAsync(ct);

        // Reverse order (dependent -> independent)
        var tables = new[]
        {
            // Phase 7: System Settings and GDPR
            "GdprCleanupHistory",
            "SystemSettings",

            // Phase 6: Backup system
            "KeyBackupHistory",
            "BackupHistory",
            "BackupProviders",
            "BackupSettings",

            // Phase 5: Audit and insights
            "UserActivities",
            "PerformanceMetrics",
            "ApiRequests",
            "PageViews",
            "AuditLogs",

            // Phase 4: Product-dependent tables
            "ProductPrices",
            "StockMovements",
            "ProductStorageLocations",
            "ProductBatches",

            // Phase 3: Warehouse-dependent tables
            "Notifications",
            "ApiKeys",
            "Products",
            "StorageLocations",

            // Phase 2c: Session management (user-dependent)
            "SecurityEvents",
            "SessionActivities",
            "UserSessions",

            // Phase 2b: User-related tables
            "UserNotificationSettings",
            "PasswordResetTokens",

            // Phase 2: Base tables
            "Rooms",
            "Categories",
            "Users",

            // Phase 1: Independent tables
            "Warehouses"
        };

        foreach (var table in tables)
        {
            try
            {
                await using var cmd = new NpgsqlCommand($"TRUNCATE TABLE \"{table}\" CASCADE", connection);
                await cmd.ExecuteNonQueryAsync(ct);
                _logger.LogInformation("Truncated table: {Table}", table);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not truncate table: {Table}", table);
            }
        }
    }

    private async Task TruncateSQLiteAsync(CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // SQLite uses DELETE FROM (no TRUNCATE support)
        var tables = new[]
        {
            // Reverse order (dependent -> independent)

            // Phase 7: System Settings and GDPR
            "GdprCleanupHistory",
            "SystemSettings",

            // Phase 6: Backup system
            "KeyBackupHistory",
            "BackupHistory",
            "BackupProviders",
            "BackupSettings",

            // Phase 5: Audit and insights
            "UserActivities",
            "PerformanceMetrics",
            "ApiRequests",
            "PageViews",
            "AuditLogs",

            // Phase 4: Product-dependent tables
            "ProductPrices",
            "StockMovements",
            "ProductStorageLocations",
            "ProductBatches",

            // Phase 3: Warehouse-dependent tables
            "Notifications",
            "ApiKeys",
            "Products",
            "StorageLocations",

            // Phase 2c: Session management (user-dependent)
            "SecurityEvents",
            "SessionActivities",
            "UserSessions",

            // Phase 2b: User-related tables
            "UserNotificationSettings",
            "PasswordResetTokens",

            // Phase 2: Base tables
            "Rooms",
            "Categories",
            "Users",

            // Phase 1: Independent tables
            "Warehouses"
        };

        foreach (var table in tables)
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync($"DELETE FROM \"{table}\"", ct);
                _logger.LogInformation("Deleted all from table: {Table}", table);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete from table: {Table}", table);
            }
        }
    }

    private async Task<int> ExportTableAsync<T>(DbContext context, DbSet<T> dbSet, string outputPath, CancellationToken ct) where T : class
    {
        var data = await dbSet.AsNoTracking().ToListAsync(ct);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters =
            {
                new System.Text.Json.Serialization.JsonStringEnumConverter(),
                new UtcDateTimeConverter(),
                new UtcNullableDateTimeConverter()
            }
        };

        var json = JsonSerializer.Serialize(data, options);
        await File.WriteAllTextAsync(outputPath, json, ct);

        _logger.LogInformation("Exported {Count} records to {File}", data.Count, Path.GetFileName(outputPath));
        return data.Count;
    }

    private async Task ImportTableAsync<T>(DbContext context, DbSet<T> dbSet, string inputPath, CancellationToken ct) where T : class
    {
        if (!File.Exists(inputPath))
        {
            _logger.LogWarning("File not found: {Path}", inputPath);
            return;
        }

        var json = await File.ReadAllTextAsync(inputPath, ct);

        var options = new JsonSerializerOptions
        {
            Converters =
            {
                new System.Text.Json.Serialization.JsonStringEnumConverter(),
                new UtcDateTimeConverter(),
                new UtcNullableDateTimeConverter()
            }
        };

        var data = JsonSerializer.Deserialize<List<T>>(json, options);

        if (data == null || !data.Any())
        {
            _logger.LogInformation("No data in {File}", Path.GetFileName(inputPath));
            return;
        }

        // Insert data (table was already truncated)
        await dbSet.AddRangeAsync(data, ct);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Imported {Count} records to {Table}", data.Count, typeof(T).Name);
    }
}
