namespace LagersystemLVHome.Application.Services;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body, bool isHtml = true, CancellationToken cancellationToken = default);
    Task SendEmailWithAttachmentAsync(string to, string subject, string body, byte[] attachmentData, string attachmentFilename, bool isHtml = true, CancellationToken cancellationToken = default);
    Task SendPasswordResetEmailAsync(string to, string username, string resetToken, CancellationToken cancellationToken = default);
    Task SendAccountApprovedEmailAsync(string to, string username, CancellationToken cancellationToken = default);
    Task SendAccountRejectedEmailAsync(string to, string username, string? reason, CancellationToken cancellationToken = default);
    Task SendWelcomeEmailAsync(string to, string username, CancellationToken cancellationToken = default);
    Task SendTwoFactorCodeEmailAsync(string to, string code, CancellationToken cancellationToken = default);
    Task SendAccountDeletionConfirmationAsync(string to, string username, CancellationToken cancellationToken = default);
    Task SendLowStockAlertAsync(string to, string productName, int currentStock, int minStock, CancellationToken cancellationToken = default);
}
