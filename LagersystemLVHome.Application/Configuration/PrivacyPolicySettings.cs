namespace LagersystemLVHome.Application.Configuration;

/// <summary>
/// Configuration for Privacy Policy / GDPR compliance.
/// All customizable texts and company information.
/// </summary>
public class PrivacyPolicySettings
{
    /// <summary>
    /// Company information section.
    /// </summary>
    public CompanyInfo Company { get; set; } = new();

    /// <summary>
    /// Data protection officer information.
    /// </summary>
    public DataProtectionOfficer DPO { get; set; } = new();

    /// <summary>
    /// Retention periods for different data types (in days).
    /// </summary>
    public RetentionPeriods Retention { get; set; } = new();

    /// <summary>
    /// Cookie settings.
    /// </summary>
    public CookieSettings Cookies { get; set; } = new();

    /// <summary>
    /// External services configuration.
    /// </summary>
    public ExternalServices External { get; set; } = new();

    /// <summary>
    /// Privacy policy version.
    /// </summary>
    public string Version { get; set; } = "2.0";

    /// <summary>
    /// Show legal disclaimer at bottom.
    /// </summary>
    public bool ShowLegalDisclaimer { get; set; } = true;

    /// <summary>
    /// Custom footer text.
    /// </summary>
    public string? CustomFooterText { get; set; }
}

/// <summary>
/// Company information.
/// </summary>
public class CompanyInfo
{
    /// <summary>
    /// Company name.
    /// </summary>
    public string Name { get; set; } = "[Ihr Firmenname]";

    /// <summary>
    /// Company address.
    /// </summary>
    public string Address { get; set; } = "[Ihre Adresse]";

    /// <summary>
    /// Company email.
    /// </summary>
    public string Email { get; set; } = "[Ihre E-Mail-Adresse]";

    /// <summary>
    /// Company phone.
    /// </summary>
    public string Phone { get; set; } = "[Ihre Telefonnummer]";

    /// <summary>
    /// Company website.
    /// </summary>
    public string? Website { get; set; }
}

/// <summary>
/// Data protection officer information.
/// </summary>
public class DataProtectionOfficer
{
    /// <summary>
    /// DPO name.
    /// </summary>
    public string Name { get; set; } = "[Name des Datenschutzbeauftragten]";

    /// <summary>
    /// DPO email.
    /// </summary>
    public string Email { get; set; } = "datenschutz@[ihre-domain].de";

    /// <summary>
    /// DPO phone (optional).
    /// </summary>
    public string? Phone { get; set; }
}

/// <summary>
/// Data retention periods (in days).
/// </summary>
public class RetentionPeriods
{
    /// <summary>
    /// User data retention (until account deletion).
    /// </summary>
    public string UserData { get; set; } = "Bis zur Konto-Löschung durch den Benutzer";

    /// <summary>
    /// Sessions and device fingerprints retention.
    /// </summary>
    public int SessionsAndFingerprints { get; set; } = 30;

    /// <summary>
    /// Login logs retention.
    /// </summary>
    public int LoginLogs { get; set; } = 90;

    /// <summary>
    /// Audit logs retention.
    /// </summary>
    public int AuditLogs { get; set; } = 90;

    /// <summary>
    /// Analytics data retention.
    /// </summary>
    public int AnalyticsData { get; set; } = 90;

    /// <summary>
    /// Deleted accounts soft-delete retention.
    /// </summary>
    public int DeletedAccounts { get; set; } = 90;

    /// <summary>
    /// Daily backup retention.
    /// </summary>
    public int DailyBackups { get; set; } = 30;

    /// <summary>
    /// Weekly backup retention.
    /// </summary>
    public int WeeklyBackups { get; set; } = 90;

    /// <summary>
    /// Monthly backup retention.
    /// </summary>
    public int MonthlyBackups { get; set; } = 365;
}

/// <summary>
/// Cookie configuration.
/// </summary>
public class CookieSettings
{
    /// <summary>
    /// Session cookie name.
    /// </summary>
    public string Name { get; set; } = ".AspNetCore.Cookies";

    /// <summary>
    /// Cookie expiration in hours.
    /// </summary>
    public int ExpirationHours { get; set; } = 8;

    /// <summary>
    /// HttpOnly flag.
    /// </summary>
    public bool HttpOnly { get; set; } = true;

    /// <summary>
    /// Secure flag.
    /// </summary>
    public bool Secure { get; set; } = true;

    /// <summary>
    /// SameSite policy.
    /// </summary>
    public string SameSite { get; set; } = "Lax";
}

/// <summary>
/// External services configuration.
/// </summary>
public class ExternalServices
{
    /// <summary>
    /// Use Cloudflare CDN/DDoS protection.
    /// </summary>
    public bool UseCloudflare { get; set; } = false;

    /// <summary>
    /// Cloudflare information.
    /// </summary>
    public CloudflareInfo Cloudflare { get; set; } = new();
}

/// <summary>
/// Cloudflare service information.
/// </summary>
public class CloudflareInfo
{
    /// <summary>
    /// Service name.
    /// </summary>
    public string Name { get; set; } = "Cloudflare Inc.";

    /// <summary>
    /// Service address.
    /// </summary>
    public string Address { get; set; } = "101 Townsend St, San Francisco, CA 94107, USA";

    /// <summary>
    /// Privacy policy URL.
    /// </summary>
    public string PrivacyPolicyUrl { get; set; } = "https://www.cloudflare.com/de-de/privacypolicy/";

    /// <summary>
    /// Data processing agreement URL.
    /// </summary>
    public string DpaUrl { get; set; } = "https://www.cloudflare.com/de-de/cloudflare-customer-dpa/";
}
