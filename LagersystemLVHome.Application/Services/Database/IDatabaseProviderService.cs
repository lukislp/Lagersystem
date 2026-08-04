using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Npgsql;

namespace LagersystemLVHome.Application.Services;

public interface IDatabaseProviderService
{
    void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString);
    DatabaseProvider Provider { get; }
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<bool> EnsureDatabaseExistsAsync(CancellationToken cancellationToken = default);
    Task BackupDatabaseAsync(string backupPath, CancellationToken cancellationToken = default);
    Task RestoreDatabaseAsync(string backupPath, CancellationToken cancellationToken = default);
}
