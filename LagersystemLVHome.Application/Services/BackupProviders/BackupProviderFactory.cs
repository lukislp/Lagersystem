using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services.BackupProviders;

/// <summary>
/// Factory for selecting the appropriate backup provider uploader.
/// </summary>
public sealed class BackupProviderFactory
{
    private readonly IEnumerable<IBackupProviderUploader> _uploaders;
    private readonly ILogger<BackupProviderFactory> _logger;

    public BackupProviderFactory(
        IEnumerable<IBackupProviderUploader> uploaders,
        ILogger<BackupProviderFactory> logger)
    {
        _uploaders = uploaders;
        _logger = logger;
    }

    public IBackupProviderUploader GetUploader(BackupProviderType providerType)
    {
        var uploader = _uploaders.FirstOrDefault(u => u.SupportedProviderType == providerType);

        if (uploader == null)
        {
            throw new NotSupportedException($"Provider type {providerType} is not supported or not registered");
        }

        return uploader;
    }

    public bool IsSupported(BackupProviderType providerType)
    {
        return _uploaders.Any(u => u.SupportedProviderType == providerType);
    }

    public IEnumerable<BackupProviderType> GetSupportedProviderTypes()
    {
        return _uploaders.Select(u => u.SupportedProviderType);
    }
}
