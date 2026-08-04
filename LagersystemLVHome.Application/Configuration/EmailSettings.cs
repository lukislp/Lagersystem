namespace LagersystemLVHome.Application.Configuration;

/// <summary>
/// SMTP email configuration.
/// </summary>
public class EmailSettings
{
    public bool EnableEmail { get; set; } = false;
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string SenderEmail { get; set; } = "";
    public string SenderName { get; set; } = "LagerSystem";
    public string SmtpUsername { get; set; } = "";
    public string SmtpPassword { get; set; } = "";

    /// <summary>
    /// Application URL (e.g. https://localhost:5001 or https://yourdomain.com).
    /// </summary>
    public string ApplicationUrl { get; set; } = "https://localhost:5001";

    /// <summary>
    /// Path to email templates.
    /// </summary>
    public string TemplatesPath { get; set; } = "EmailTemplates";
}
