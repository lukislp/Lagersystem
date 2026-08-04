using Microsoft.JSInterop;

namespace LagersystemLVHome.Application.Services;

public sealed class KeyboardShortcutService : IKeyboardShortcutService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<KeyboardShortcutService> _logger;

    public KeyboardShortcutService(IJSRuntime jsRuntime, ILogger<KeyboardShortcutService> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task RegisterShortcutAsync<T>(string keys, DotNetObjectReference<T> dotNetRef, string methodName, string description = "", CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("registerKeyboardShortcut", keys, dotNetRef, methodName, description);
            _logger.LogDebug("Registered keyboard shortcut: {Keys} - {Description}", keys, description);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register keyboard shortcut: {Keys}", keys);
        }
    }

    public async Task UnregisterShortcutAsync(string keys, CancellationToken cancellationToken = default)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("unregisterKeyboardShortcut", keys);
            _logger.LogDebug("Unregistered keyboard shortcut: {Keys}", keys);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister keyboard shortcut: {Keys}", keys);
        }
    }

    public async Task EnableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("enableKeyboardShortcuts");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable keyboard shortcuts");
        }
    }

    public async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("disableKeyboardShortcuts");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable keyboard shortcuts");
        }
    }
}
