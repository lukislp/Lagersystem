using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services.BackupProviders;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Backup management service for multi-provider backups using the factory pattern.
/// </summary>
public interface IBackupManagementService
{
    Task<BackupResult> CreateBackupAsync(CancellationToken ct = default);
    Task<List<BackupProvider>> GetProvidersAsync(CancellationToken cancellationToken = default);
    Task<BackupProvider> AddProviderAsync(BackupProvider provider, CancellationToken cancellationToken = default);
    Task<BackupProvider> UpdateProviderAsync(BackupProvider provider, CancellationToken cancellationToken = default);
    Task DeleteProviderAsync(int id, CancellationToken cancellationToken = default);
    Task<List<BackupHistory>> GetHistoryAsync(int? providerId = null, int limit = 50, CancellationToken cancellationToken = default);
    Task<BackupSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<BackupSettings> UpdateSettingsAsync(BackupSettings settings, CancellationToken cancellationToken = default);
    Task<bool> TestProviderAsync(int providerId, CancellationToken cancellationToken = default);
    Task CleanupOldBackupsAsync(int retentionDays, CancellationToken cancellationToken = default);
    Task<bool> ValidateBackupAsync(int historyId, CancellationToken cancellationToken = default);
    Task DeleteBackupAsync(int historyId, CancellationToken cancellationToken = default);
    Task CleanupBackupsByProviderSettingsAsync(CancellationToken cancellationToken = default);
}
