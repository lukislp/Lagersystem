namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Service for PDF report generation.
/// </summary>
public interface IPdfReportService
{
    /// <summary>
    /// Generates a weekly report (Application Insights + Security Center).
    /// </summary>
    Task<byte[]> GenerateWeeklyReportAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

    Task<byte[]> GenerateInsightsReportAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

    Task<byte[]> GenerateSecurityReportAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
