using System.Threading;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Npgsql;

namespace LagersystemLVHome.Application.Services;

public sealed class DatabaseProviderService : IDatabaseProviderService
{
    private readonly DatabaseSettings _settings;
    private readonly ILogger<DatabaseProviderService> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly string _toolsPath;
    private readonly string _secureConnectionString;

    public DatabaseProvider Provider => _settings.Provider;

    public DatabaseProviderService(
        DatabaseSettings settings,
        ILogger<DatabaseProviderService> logger,
        IWebHostEnvironment environment,
        string secureConnectionString)
    {
        _settings = settings;
        _logger = logger;
        _environment = environment;
        _secureConnectionString = secureConnectionString;

        // Legacy: pg_dump/mysqldump are no longer used.
        // JsonBackupHelper creates pure .NET JSON backups (see BackupManagementService.cs).
        // BackupDatabaseAsync/RestoreDatabaseAsync below are legacy code.
        _toolsPath = Path.Combine(_environment.ContentRootPath, "Tools");
    }

    public void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString)
    {
        switch (_settings.Provider)
        {
            case DatabaseProvider.SQLite:
                options.UseSqlite(connectionString, sqliteOptions =>
                {
                    sqliteOptions.CommandTimeout(_settings.CommandTimeout);
                    sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                });
                break;

            case DatabaseProvider.PostgreSQL:
                options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.CommandTimeout(_settings.CommandTimeout);
                    npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);

                    if (_settings.EnableRetryOnFailure)
                    {
                        npgsqlOptions.EnableRetryOnFailure(
                            maxRetryCount: _settings.MaxRetryCount,
                            maxRetryDelay: TimeSpan.FromSeconds(30),
                            errorCodesToAdd: null);
                    }
                });
                break;

            case DatabaseProvider.MySQL:
                options.UseMySql(connectionString,
                    AutoDetectMySqlServerVersionWithRetry(connectionString),
                    mySqlOptions =>
                    {
                        mySqlOptions.CommandTimeout(_settings.CommandTimeout);
                        mySqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);

                        if (_settings.EnableRetryOnFailure)
                        {
                            mySqlOptions.EnableRetryOnFailure(
                                maxRetryCount: _settings.MaxRetryCount,
                                maxRetryDelay: TimeSpan.FromSeconds(30),
                                errorNumbersToAdd: null);
                        }
                    });
                break;

            default:
                throw new NotSupportedException($"Database provider {_settings.Provider} is not supported");
        }

        options.EnableSensitiveDataLogging(false);
        options.EnableDetailedErrors(true);
    }

    /// <summary>
    /// ServerVersion.AutoDetect opens a raw connection to negotiate the server version, before
    /// EF Core's own EnableRetryOnFailure exists to cover it. A MySQL server can start accepting
    /// TCP connections (and answer a health-check ping) slightly before its user/database grants
    /// are fully live, so this can fail transiently right after the server comes up - retry with
    /// a short backoff instead of crashing the app on that race.
    /// </summary>
    private static ServerVersion AutoDetectMySqlServerVersionWithRetry(string connectionString)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return ServerVersion.AutoDetect(connectionString);
            }
            catch when (attempt < maxAttempts)
            {
                Thread.Sleep(TimeSpan.FromSeconds(2 * attempt));
            }
        }

        return ServerVersion.AutoDetect(connectionString);
    }

    /// <summary>
    /// Ensures the database exists, creating it if necessary (PostgreSQL/MySQL).
    /// </summary>
    public async Task<bool> EnsureDatabaseExistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            switch (_settings.Provider)
            {
                case DatabaseProvider.SQLite:
                    _logger.LogInformation("SQLite database will be created automatically");
                    return true;

                case DatabaseProvider.PostgreSQL:
                    return await EnsurePostgreSQLDatabaseExistsAsync();

                case DatabaseProvider.MySQL:
                    return await EnsureMySQLDatabaseExistsAsync();

                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure database exists for {Provider}", _settings.Provider);
            return false;
        }
    }

    /// <summary>
    /// Creates a PostgreSQL database if it does not exist (SQL-injection protected).
    /// </summary>
    private async Task<bool> EnsurePostgreSQLDatabaseExistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(_secureConnectionString);
            var databaseName = builder.Database;

            if (!IsValidDatabaseName(databaseName))
            {
                _logger.LogError("Invalid database name: {DatabaseName}", databaseName);
                throw new ArgumentException($"Invalid database name: {databaseName}");
            }

            // Connect to the 'postgres' system database to create the target database
            builder.Database = "postgres";
            var systemConnectionString = builder.ToString();

            await using var connection = new NpgsqlConnection(systemConnectionString);
            await connection.OpenAsync();

            await using var checkCmd = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @databaseName",
                connection);
            checkCmd.Parameters.AddWithValue("@databaseName", databaseName);

            var exists = await checkCmd.ExecuteScalarAsync();

            if (exists == null)
            {
                await using var createCmd = new NpgsqlCommand(
                    $"CREATE DATABASE {QuoteIdentifier(databaseName)} ENCODING = 'UTF8'",
                    connection);

                await createCmd.ExecuteNonQueryAsync();
                _logger.LogInformation("PostgreSQL database '{DatabaseName}' created successfully", databaseName);
            }
            else
            {
                _logger.LogInformation("PostgreSQL database '{DatabaseName}' already exists", databaseName);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create PostgreSQL database");
            return false;
        }
    }

    /// <summary>
    /// Creates a MySQL database if it does not exist (SQL-injection protected).
    /// </summary>
    private async Task<bool> EnsureMySQLDatabaseExistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var builder = new MySqlConnector.MySqlConnectionStringBuilder(_secureConnectionString);
            var databaseName = builder.Database;

            if (!IsValidDatabaseName(databaseName))
            {
                _logger.LogError("Invalid database name: {DatabaseName}", databaseName);
                throw new ArgumentException($"Invalid database name: {databaseName}");
            }

            // Connect without specifying a database
            builder.Database = "";
            var systemConnectionString = builder.ToString();

            await using var connection = new MySqlConnector.MySqlConnection(systemConnectionString);
            await connection.OpenAsync();

            var escapedDbName = databaseName.Replace("`", "``");
            await using var cmd = new MySqlConnector.MySqlCommand(
                $"CREATE DATABASE IF NOT EXISTS `{escapedDbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",
                connection);

            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("MySQL database '{DatabaseName}' created/verified successfully", databaseName);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create MySQL database");
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>();
            ConfigureDbContext(optionsBuilder, _secureConnectionString);

            await using var context = new InventoryDbContext(optionsBuilder.Options);
            var canConnect = await context.Database.CanConnectAsync();

            if (canConnect)
            {
                _logger.LogInformation("Database connection test successful for {Provider}", _settings.Provider);
            }
            else
            {
                _logger.LogWarning("Database connection test failed for {Provider}", _settings.Provider);
            }

            return canConnect;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database connection test failed for {Provider}", _settings.Provider);
            return false;
        }
    }

    public async Task BackupDatabaseAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        // Legacy: BackupManagementService uses JsonBackupHelper for pure .NET JSON backups.
        // See: JsonBackupHelper.CreateJsonBackupAsync()

        switch (_settings.Provider)
        {
            case DatabaseProvider.SQLite:
                await BackupSQLiteAsync(backupPath);
                break;

            case DatabaseProvider.PostgreSQL:
                await BackupPostgreSQLAsync(backupPath);
                break;

            case DatabaseProvider.MySQL:
                await BackupMySQLAsync(backupPath);
                break;
        }
    }

    public async Task RestoreDatabaseAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        // Legacy: DatabaseRestoreService uses JsonBackupHelper for pure .NET JSON restore.
        // See: JsonBackupHelper.RestoreFromJsonBackupAsync()

        switch (_settings.Provider)
        {
            case DatabaseProvider.SQLite:
                await RestoreSQLiteAsync(backupPath);
                break;

            case DatabaseProvider.PostgreSQL:
                await RestorePostgreSQLAsync(backupPath);
                break;

            case DatabaseProvider.MySQL:
                await RestoreMySQLAsync(backupPath);
                break;
        }
    }

    private async Task BackupSQLiteAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        var sourceFile = _settings.ConnectionString.Replace("Data Source=", "").Trim();

        if (File.Exists(sourceFile))
        {
            await Task.Run(() => File.Copy(sourceFile, backupPath, overwrite: true));
            _logger.LogInformation("SQLite database backed up to {BackupPath}", backupPath);
        }
        else
        {
            throw new FileNotFoundException($"Database file not found: {sourceFile}");
        }
    }

    private async Task RestoreSQLiteAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        var targetFile = _settings.ConnectionString.Replace("Data Source=", "").Trim();

        if (File.Exists(backupPath))
        {
            await Task.Run(() => File.Copy(backupPath, targetFile, overwrite: true));
            _logger.LogInformation("SQLite database restored from {BackupPath}", backupPath);
        }
        else
        {
            throw new FileNotFoundException($"Backup file not found: {backupPath}");
        }
    }

    private async Task BackupPostgreSQLAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        var connectionString = _settings.ConnectionString;
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);

        var pgDumpPath = Path.Combine(_toolsPath, "postgres", "pgsql", "bin", "pg_dump.exe");

        if (!File.Exists(pgDumpPath))
        {
            _logger.LogWarning("Bundled pg_dump.exe not found at {Path}, using system PATH", pgDumpPath);
            pgDumpPath = "pg_dump";
        }

        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = pgDumpPath,
                Arguments = $"-h {builder.Host} -p {builder.Port} -U {builder.Username} -d {builder.Database} -F c -f \"{backupPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Environment = { ["PGPASSWORD"] = builder.Password ?? string.Empty }
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            _logger.LogInformation("PostgreSQL database backed up to {BackupPath}", backupPath);
        }
        else
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"PostgreSQL backup failed: {error}");
        }
    }

    private async Task RestorePostgreSQLAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        var connectionString = _settings.ConnectionString;
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);

        var pgRestorePath = Path.Combine(_toolsPath, "postgres", "pgsql", "bin", "pg_restore.exe");

        if (!File.Exists(pgRestorePath))
        {
            _logger.LogWarning("Bundled pg_restore.exe not found at {Path}, using system PATH", pgRestorePath);
            pgRestorePath = "pg_restore";
        }

        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = pgRestorePath,
                Arguments = $"-h {builder.Host} -p {builder.Port} -U {builder.Username} -d {builder.Database} -c \"{backupPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Environment = { ["PGPASSWORD"] = builder.Password ?? string.Empty }
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            _logger.LogInformation("PostgreSQL database restored from {BackupPath}", backupPath);
        }
        else
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"PostgreSQL restore failed: {error}");
        }
    }

    private async Task BackupMySQLAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        var connectionString = _settings.ConnectionString;
        var builder = new MySqlConnector.MySqlConnectionStringBuilder(connectionString);

        var mysqldumpPath = Path.Combine(_toolsPath, "mysql", "mysqldump.exe");

        if (!File.Exists(mysqldumpPath))
        {
            _logger.LogWarning("Bundled mysqldump.exe not found, using system PATH");
            mysqldumpPath = "mysqldump";
        }

        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = mysqldumpPath,
                Arguments = $"-h {builder.Server} -P {builder.Port} -u {builder.UserID} -p{builder.Password} {builder.Database} --result-file=\"{backupPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            _logger.LogInformation("MySQL database backed up to {BackupPath}", backupPath);
        }
        else
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"MySQL backup failed: {error}");
        }
    }

    private async Task RestoreMySQLAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        var connectionString = _settings.ConnectionString;
        var builder = new MySqlConnector.MySqlConnectionStringBuilder(connectionString);

        var mysqlPath = Path.Combine(_toolsPath, "mysql", "mysql.exe");

        if (!File.Exists(mysqlPath))
        {
            _logger.LogWarning("Bundled mysql.exe not found, using system PATH");
            mysqlPath = "mysql";
        }

        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = mysqlPath,
                Arguments = $"-h {builder.Server} -P {builder.Port} -u {builder.UserID} -p{builder.Password} {builder.Database} < \"{backupPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            _logger.LogInformation("MySQL database restored from {BackupPath}", backupPath);
        }
        else
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"MySQL restore failed: {error}");
        }
    }

    /// <summary>
    /// Validates that a database name contains only safe characters.
    /// </summary>
    private bool IsValidDatabaseName(string databaseName)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(
            databaseName,
            @"^[a-zA-Z0-9_\-]+$"
        );
    }

    /// <summary>
    /// Quotes a PostgreSQL identifier to prevent SQL injection.
    /// </summary>
    private string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
