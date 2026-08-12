using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Mail;

namespace LagersystemLVHome.Application.Services;

public sealed class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public EmailService(EmailSettings settings, ILogger<EmailService> logger, IServiceProvider serviceProvider)
    {
        _settings = settings;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    private async Task<string> GetUserDisplayNameAsync(string email, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

            var user = await dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

            if (user != null && !string.IsNullOrWhiteSpace(user.DisplayName))
            {
                return user.DisplayName;
            }

            // Fallback: use part before @
            return email.Split('@')[0];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading user display name for {Email}", email);
            return email.Split('@')[0];
        }
    }

    public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = true, CancellationToken cancellationToken = default)
    {
        if (!_settings.EnableEmail)
        {
            _logger.LogWarning("Email sending disabled. Email would be sent to: {To}, Subject: {Subject}", to, subject);
            return;
        }

        try
        {
            using var smtpClient = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
            {
                EnableSsl = _settings.UseSsl,
                Credentials = new NetworkCredential(_settings.SmtpUsername, _settings.SmtpPassword),
                Timeout = 10000
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml
            };

            mailMessage.To.Add(to);

            await smtpClient.SendMailAsync(mailMessage);
            _logger.LogInformation("Email successfully sent to: {To}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email to: {To}", to);
            throw;
        }
    }

    public async Task SendEmailWithAttachmentAsync(string to, string subject, string body, byte[] attachmentData, string attachmentFilename, bool isHtml = true, CancellationToken cancellationToken = default)
    {
        if (!_settings.EnableEmail)
        {
            _logger.LogWarning("Email sending disabled. Email with attachment would be sent to: {To}, Subject: {Subject}", to, subject);
            return;
        }

        try
        {
            using var smtpClient = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
            {
                EnableSsl = _settings.UseSsl,
                Credentials = new NetworkCredential(_settings.SmtpUsername, _settings.SmtpPassword),
                Timeout = 30000
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml
            };

            mailMessage.To.Add(to);

            // Add PDF attachment
            using var attachmentStream = new MemoryStream(attachmentData);
            var attachment = new Attachment(attachmentStream, attachmentFilename, "application/pdf");
            mailMessage.Attachments.Add(attachment);

            await smtpClient.SendMailAsync(mailMessage);
            _logger.LogInformation("Email with PDF attachment ({Size} KB) successfully sent to: {To}", attachmentData.Length / 1024, to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email with attachment to: {To}", to);
            throw;
        }
    }

    public async Task SendPasswordResetEmailAsync(string to, string username, string resetToken, CancellationToken cancellationToken = default)
    {
        var displayName = await GetUserDisplayNameAsync(to);
        var resetLink = $"{_settings.ApplicationUrl.TrimEnd('/')}/reset-password?token={resetToken}";

        var body = $@"
            <html>
            <body style='font-family: Arial, sans-serif; padding: 20px;'>
            <p>Hallo {displayName},</p>
            <p>Sie haben eine Anfrage zum Zuruecksetzen Ihres Passworts gestellt.</p>
            <p>Klicken Sie auf den folgenden Link, um Ihr Passwort zurueckzusetzen:</p>
            <p style='margin: 30px 0;'>
                <a href='{resetLink}' style='background-color: #4CAF50; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block;'>Passwort zuruecksetzen</a>
            </p>
            <p>Dieser Link ist 24 Stunden gueltig.</p>
            <p>Falls Sie diese Anfrage nicht gestellt haben, koennen Sie diese E-Mail einfach ignorieren.</p>
            <br>
            <p>Mit freundlichen Gruessen,<br>Ihr LagerSystem Team</p>
            </body>
            </html>";

        await SendEmailAsync(to, "Passwort zuruecksetzen", body);
    }

    public async Task SendAccountApprovedEmailAsync(string to, string username, CancellationToken cancellationToken = default)
    {
        var displayName = await GetUserDisplayNameAsync(to);
        var loginLink = $"{_settings.ApplicationUrl.TrimEnd('/')}/login";

        var body = $@"
            <html>
            <body style='font-family: Arial, sans-serif; padding: 20px;'>
            <p>Hallo {displayName},</p>
            <p>Ihr Account wurde von einem Administrator genehmigt!</p>
            <p>Sie koennen sich jetzt anmelden und das LagerSystem verwenden.</p>
            <p style='margin: 30px 0;'>
                <a href='{loginLink}' style='background-color: #4CAF50; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block;'>Jetzt anmelden</a>
            </p>
            <p>Viel Erfolg!</p>
            <br>
            <p>Mit freundlichen Gruessen,<br>Ihr LagerSystem Team</p>
            </body>
            </html>";

        await SendEmailAsync(to, "Account genehmigt - LagerSystem", body);
    }

    public async Task SendAccountRejectedEmailAsync(string to, string username, string? reason, CancellationToken cancellationToken = default)
    {
        var displayName = await GetUserDisplayNameAsync(to);

        var body = $@"
            <html>
            <body style='font-family: Arial, sans-serif; padding: 20px;'>
            <p>Hallo {displayName},</p>
            <p>Leider wurde Ihre Account-Anfrage abgelehnt.</p>
            {(string.IsNullOrEmpty(reason) ? "" : $"<p><strong>Grund:</strong> {reason}</p>")}
            <p>Bei Fragen wenden Sie sich bitte an den Administrator.</p>
            <br>
            <p>Mit freundlichen Gruessen,<br>Ihr LagerSystem Team</p>
            </body>
            </html>";

        await SendEmailAsync(to, "Account abgelehnt - LagerSystem", body);
    }

    public async Task SendWelcomeEmailAsync(string to, string username, CancellationToken cancellationToken = default)
    {
        var displayName = await GetUserDisplayNameAsync(to);

        var body = $@"
            <html>
            <body style='font-family: Arial, sans-serif; padding: 20px;'>
            <p>Hallo {displayName},</p>
            <p>Willkommen bei LagerSystem!</p>
            <p>Vielen Dank fuer Ihre Registrierung!</p>
            <p>Ihr Account wartet auf Genehmigung durch einen Administrator.</p>
            <p>Sie erhalten eine weitere E-Mail, sobald Ihr Account freigeschaltet wurde.</p>
            <br>
            <p>Mit freundlichen Gruessen,<br>Ihr LagerSystem Team</p>
            </body>
            </html>";

        await SendEmailAsync(to, "Willkommen bei LagerSystem", body);
    }

    public async Task SendTwoFactorCodeEmailAsync(string to, string code, CancellationToken cancellationToken = default)
    {
        var displayName = await GetUserDisplayNameAsync(to);

        var body = $@"
            <html>
            <body style='font-family: Arial, sans-serif; padding: 20px; text-align: center;'>
            <p>Hallo {displayName},</p>
            <p>Ihr Bestaetigungscode lautet:</p>
            <h1 style='font-size: 48px; color: #4CAF50; letter-spacing: 10px; margin: 30px 0;'>{code}</h1>
            <p>Dieser Code ist 5 Minuten gueltig.</p>
            <p>Falls Sie diese Anfrage nicht gestellt haben, ignorieren Sie diese E-Mail.</p>
            <br>
            <p>Mit freundlichen Gruessen,<br>Ihr LagerSystem Team</p>
            </body>
            </html>";

        await SendEmailAsync(to, "Ihr 2FA-Code - LagerSystem", body);
    }

    public async Task SendAccountDeletionConfirmationAsync(string to, string username, CancellationToken cancellationToken = default)
    {
        var displayName = string.IsNullOrWhiteSpace(username) ? await GetUserDisplayNameAsync(to) : username;

        var body = $@"
            <html>
            <body style='font-family: Arial, sans-serif; padding: 20px;'>
            <p>Hallo {displayName},</p>
            <p>Ihr Account wurde erfolgreich geloescht.</p>
            <p>Alle Ihre personenbezogenen Daten wurden gemaess DSGVO anonymisiert oder entfernt.</p>
            <p>Wir bedauern, dass Sie uns verlassen.</p>
            <br>
            <p>Mit freundlichen Gruessen,<br>Ihr LagerSystem Team</p>
            </body>
            </html>";

        await SendEmailAsync(to, "Account geloescht - LagerSystem", body);
    }

    public async Task SendLowStockAlertAsync(string to, string productName, int currentStock, int minStock, CancellationToken cancellationToken = default)
    {
        var displayName = await GetUserDisplayNameAsync(to);
        var dashboardLink = $"{_settings.ApplicationUrl.TrimEnd('/')}/low-stock";

        var body = $@"
            <html>
            <body style='font-family: Arial, sans-serif; padding: 20px;'>
            <p>Hallo {displayName},</p>
            <p>Achtung! Der Bestand fuer folgendes Produkt ist niedrig:</p>
            <div style='background-color: #fff3cd; border-left: 4px solid #ffc107; padding: 15px; margin: 20px 0; border-radius: 4px;'>
                <p><strong>Produkt:</strong> {productName}</p>
                <p><strong>Aktueller Bestand:</strong> {currentStock}</p>
                <p><strong>Mindestbestand:</strong> {minStock}</p>
            </div>
            <p>Bitte bestellen Sie Nachschub!</p>
            <p style='margin: 30px 0;'>
                <a href='{dashboardLink}' style='background-color: #ffc107; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block;'>Niedrige Bestaende anzeigen</a>
            </p>
            <br>
            <p>Mit freundlichen Gruessen,<br>Ihr LagerSystem</p>
            </body>
            </html>";

        await SendEmailAsync(to, $"Niedriger Bestand: {productName}", body);
    }
}
