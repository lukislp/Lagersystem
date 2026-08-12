using System.IO.Compression;
using LagersystemLVHome.Application.Configuration;

namespace LagersystemLVHome.Application.Services;

public interface IBackupService
{
    Task CreateBackupAsync(string? customName = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<BackupInfo>> GetBackupsAsync(CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(string backupFileName, CancellationToken cancellationToken = default);
    Task DeleteBackupAsync(string backupFileName, CancellationToken cancellationToken = default);
    Task CleanupOldBackupsAsync(CancellationToken cancellationToken = default);
}
