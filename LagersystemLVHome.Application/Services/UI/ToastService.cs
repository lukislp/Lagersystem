using LagersystemLVHome.Application.Configuration;

namespace LagersystemLVHome.Application.Services;

public enum ToastType
{
    Success,
    Info,
    Warning,
    Error
}

public sealed class ToastMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ToastType Type { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int DurationMs { get; set; }
    public bool CanDismiss { get; set; } = true;
    public Action? OnClick { get; set; }
    public Action? OnUndo { get; set; }
    public bool ShowUndo => OnUndo != null;
}

public sealed class ToastService : IToastService
{
    public event Action<string, string, string?, int, string?>? OnShow;

    public void ShowSuccess(string message, string? title = null, int duration = 3000)
    {
        Show(message, "success", title, duration);
    }

    public void ShowError(string message, string? title = null, int duration = 5000)
    {
        Show(message, "error", title, duration);
    }

    public void ShowWarning(string message, string? title = null, int duration = 4000)
    {
        Show(message, "warning", title, duration);
    }

    public void ShowInfo(string message, string? title = null, int duration = 3000)
    {
        Show(message, "info", title, duration);
    }

    public void Show(string message, string type = "info", string? title = null, int duration = 3000, string? additionalClass = null)
    {
        OnShow?.Invoke(message, type, title, duration, additionalClass);
    }
}
