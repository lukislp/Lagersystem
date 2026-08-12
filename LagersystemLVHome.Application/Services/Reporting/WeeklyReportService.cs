using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Background service for weekly PDF reports.
/// Runs every Sunday at 15:00.
/// </summary>
public sealed class WeeklyReportService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WeeklyReportService> _logger;
    private Timer? _weeklyTimer;

    public WeeklyReportService(
        IServiceProvider serviceProvider,
        ILogger<WeeklyReportService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Weekly report background service started");

        var timeUntilNextSunday = GetTimeUntilNextSunday();
        _logger.LogInformation("Next weekly report scheduled in: {Time}", timeUntilNextSunday);

        _weeklyTimer = new Timer(
            async _ => await ExecuteWeeklyReportAsync(),
            null,
            timeUntilNextSunday,
            TimeSpan.FromDays(7));

        await Task.CompletedTask;
    }

    private async Task ExecuteWeeklyReportAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("======================================");
            _logger.LogInformation("Starting weekly report generation...");
            _logger.LogInformation("Current time: {Time}", DateTime.Now.ToString("dddd, dd.MM.yyyy HH:mm:ss"));
            _logger.LogInformation("======================================");

            using var scope = _serviceProvider.CreateScope();
            var pdfService = scope.ServiceProvider.GetRequiredService<IPdfReportService>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<Data.InventoryDbContext>();

            // Report period: last 7 days
            var to = DateTime.UtcNow;
            var from = to.AddDays(-7);

            _logger.LogInformation("Report period: {From} to {To}", from, to);

            var pdfBytes = await pdfService.GenerateWeeklyReportAsync(from, to);
            _logger.LogInformation("PDF generated successfully ({Size} KB)", pdfBytes.Length / 1024);

            // Find all SuperAdmins
            var superAdmins = dbContext.Users
                .Where(u => u.Role == Domain.Models.UserRole.SuperAdmin && u.ApprovalStatus == Domain.Models.UserApprovalStatus.Approved)
                .ToList();

            _logger.LogInformation("Found {Count} SuperAdmin(s) to send report to", superAdmins.Count);

            foreach (var admin in superAdmins)
            {
                try
                {
                    await SendWeeklyReportEmailAsync(emailService, admin.Email, admin.Username, pdfBytes, from, to);
                    _logger.LogInformation("Weekly report sent to {Email}", admin.Email);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send report to {Email}", admin.Email);
                }
            }

            _logger.LogInformation("Weekly report process completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating weekly report");
        }
    }

    private async Task SendWeeklyReportEmailAsync(
        IEmailService emailService,
        string toEmail,
        string username,
        byte[] pdfBytes,
        DateTime fromDate,
        DateTime toDate, CancellationToken cancellationToken = default)
    {
        var subject = $"LagerSystem Weekly Report - Week {GetWeekNumber(toDate)}";

        var body = $@"
<!DOCTYPE html>
<html lang=""de"">
<head>
    <meta charset=""UTF-8"">
    <style>
    body {{ font-family: 'Segoe UI', Arial, sans-serif; line-height: 1.6; color: #333; }}
    .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
    .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; text-align: center; border-radius: 10px 10px 0 0; }}
    .header h1 {{ margin: 0; font-size: 28px; }}
    .header p {{ margin: 10px 0 0; opacity: 0.9; }}
    .content {{ background: white; padding: 30px; border: 1px solid #e0e0e0; border-top: none; border-radius: 0 0 10px 10px; }}
    .highlight {{ background: #f8f9fa; padding: 15px; border-left: 4px solid #667eea; margin: 20px 0; }}
    .button {{ display: inline-block; background: #667eea; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; margin-top: 20px; }}
    .footer {{ text-align: center; margin-top: 30px; color: #999; font-size: 12px; }}
    .icon {{ font-size: 48px; margin-bottom: 10px; }}
    .emoji {{ font-family: 'Segoe UI Emoji', 'Segoe UI Symbol', 'Noto Color Emoji', 'Apple Color Emoji', sans-serif; }}
    </style>
</head>
<body>
    <div class='container'>
    <div class='header'>
    <div class='icon emoji'>&#128202;</div>
    <h1>W&#246;chentlicher System-Report</h1>
    <p>Woche: {fromDate:dd.MM.yyyy} - {toDate:dd.MM.yyyy}</p>
    </div>
    
    <div class='content'>
    <p>Hallo <strong>{username}</strong>,</p>
    
    <p>Ihr w&#246;chentlicher <strong>LagerSystem Report</strong> ist fertig! <span class='emoji'>&#127881;</span></p>
    
    <div class='highlight'>
    <strong><span class='emoji'>&#128203;</span> Report-Inhalt:</strong>
    <ul>
    <li><span class='emoji'>&#128202;</span> <strong>Application Insights</strong> - Performance, Benutzeraktivit&#228;t, API-Nutzung</li>
    <li><span class='emoji'>&#128274;</span> <strong>Security Center</strong> - Anomalien, Risikobewertungen, Audit-Logs</li>
    <li><span class='emoji'>&#128200;</span> <strong>Trends &amp; Analytics</strong> - W&#246;chentliche Vergleiche und Einblicke</li>
    </ul>
    </div>
    
    <p>Der vollst&#228;ndige Report ist als PDF-Dokument angeh&#228;ngt.</p>
    
    <p><strong>Wichtige Highlights:</strong></p>
    <ul>
    <li><span class='emoji'>&#9989;</span> System-Performance-&#220;bersicht</li>
    <li><span class='emoji'>&#128269;</span> Sicherheitsereignisse und Risikoanalyse</li>
    <li><span class='emoji'>&#128101;</span> Benutzeraktivit&#228;t und Engagement-Metriken</li>
    <li><span class='emoji'>&#128640;</span> API-Nutzung und Antwortzeiten</li>
    </ul>
    
    <p style='margin-top: 30px;'>
    <em>Dies ist ein automatischer w&#246;chentlicher Report, der jeden Sonntag um 15:00 Uhr versendet wird.</em>
    </p>
    </div>
    
    <div class='footer'>
    <p>Generiert von LagerSystem LV Home am {DateTime.Now:dd.MM.yyyy HH:mm}</p>
    <p>&copy; 2026 LagerSystem. Alle Rechte vorbehalten.</p>
    </div>
    </div>
</body>
</html>";

        await emailService.SendEmailWithAttachmentAsync(
            toEmail,
            subject,
            body,
            pdfBytes,
            $"LagerSystem_Weekly_Report_Week_{GetWeekNumber(toDate)}.pdf",
            isHtml: true);
    }

    private TimeSpan GetTimeUntilNextSunday()
    {
        var now = DateTime.Now;
        var nextSunday = DateTime.Today.AddHours(15);

        if (now.DayOfWeek == DayOfWeek.Sunday)
        {
            if (now.Hour >= 15)
            {
                nextSunday = nextSunday.AddDays(7);
            }
        }
        else
        {
            int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)now.DayOfWeek + 7) % 7;
            if (daysUntilSunday == 0) daysUntilSunday = 7;
            nextSunday = DateTime.Today.AddDays(daysUntilSunday).AddHours(15);
        }

        var timeUntil = nextSunday - now;

        _logger.LogDebug("Weekly Report Service: Now={Now}, Next={Next}, TimeUntil={Hours:F2}h",
            now.ToString("dddd, dd.MM.yyyy HH:mm:ss"),
            nextSunday.ToString("dddd, dd.MM.yyyy HH:mm:ss"),
            timeUntil.TotalHours);

        return timeUntil;
    }

    private int GetWeekNumber(DateTime date)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var weekNumber = culture.Calendar.GetWeekOfYear(
            date,
            System.Globalization.CalendarWeekRule.FirstFourDayWeek,
            DayOfWeek.Monday);
        return weekNumber;
    }

    public override void Dispose()
    {
        _weeklyTimer?.Dispose();
        base.Dispose();
    }
}
