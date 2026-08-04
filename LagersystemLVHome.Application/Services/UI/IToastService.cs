using LagersystemLVHome.Application.Configuration;

namespace LagersystemLVHome.Application.Services;

public interface IToastService
{
    event Action<string, string, string?, int, string?>? OnShow;
    void ShowSuccess(string message, string? title = null, int duration = 3000);
    void ShowError(string message, string? title = null, int duration = 5000);
    void ShowWarning(string message, string? title = null, int duration = 4000);
    void ShowInfo(string message, string? title = null, int duration = 3000);
    void Show(string message, string type = "info", string? title = null, int duration = 3000, string? additionalClass = null);
}
