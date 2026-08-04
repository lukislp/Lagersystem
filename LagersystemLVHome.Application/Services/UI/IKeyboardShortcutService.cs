namespace LagersystemLVHome.Application.Services;

public interface IKeyboardShortcutService
{
    Task RegisterShortcutAsync<T>(string keys, Microsoft.JSInterop.DotNetObjectReference<T> dotNetRef, string methodName, string description = "", CancellationToken cancellationToken = default) where T : class;
    Task UnregisterShortcutAsync(string keys, CancellationToken cancellationToken = default);
    Task EnableAsync(CancellationToken cancellationToken = default);
    Task DisableAsync(CancellationToken cancellationToken = default);
}
