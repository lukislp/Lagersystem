using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services.BackupProviders;

/// <summary>
/// Interface for all backup provider upload implementations.
/// </summary>
public interface IBackupProviderUploader
{
    BackupProviderType SupportedProviderType { get; }

    /// <summary>
    /// Uploads a backup to the provider.
    /// </summary>
    /// <param name="provider">Provider configuration.</param>
    /// <param name="filePath">Path to the backup file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if successful, false otherwise.</returns>
    Task<bool> UploadAsync(BackupProvider provider, string filePath, CancellationToken ct = default);

    /// <param name="history">Backup history entry.</param>
    /// <returns>True if the backup exists and is valid.</returns>
    Task<bool> ValidateAsync(BackupHistory history, CancellationToken cancellationToken = default);

    /// <param name="history">Backup history entry.</param>
    /// <returns>True if successfully deleted.</returns>
    Task<bool> DeleteAsync(BackupHistory history, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the provider connection.
    /// </summary>
    /// <param name="provider">Provider configuration.</param>
    /// <returns>True if connection is successful.</returns>
    Task<bool> TestConnectionAsync(BackupProvider provider, CancellationToken cancellationToken = default);
}
