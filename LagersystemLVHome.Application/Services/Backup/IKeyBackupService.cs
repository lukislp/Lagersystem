using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO.Compression;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Service for automatic backup of provider encryption keys.
/// Uses existing local providers (Local Storage / Network Share).
/// </summary>
public interface IKeyBackupService
{
    Task<Domain.Models.KeyBackupSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task UpdateSettingsAsync(Domain.Models.KeyBackupSettings settings, CancellationToken cancellationToken = default);
    Task<KeyBackupResult> CreateKeyBackupAsync(CancellationToken cancellationToken = default);
    Task<List<Domain.Models.KeyBackupHistory>> GetHistoryAsync(CancellationToken cancellationToken = default);
    Task<bool> RestoreKeysFromBackupAsync(int historyId, string? password, CancellationToken cancellationToken = default);
    Task DeleteKeyBackupAsync(int historyId, CancellationToken cancellationToken = default);
    Task<List<BackupProvider>> GetAvailableLocalProvidersAsync(CancellationToken cancellationToken = default);
}
