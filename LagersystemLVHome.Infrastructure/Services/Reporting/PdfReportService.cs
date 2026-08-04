using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using LagersystemLVHome.Infrastructure.ML.Services;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Service for professional PDF report generation using QuestPDF.
/// </summary>
public class PdfReportService : IPdfReportService
{
    private readonly IApplicationInsightsService _insightsService;
    private readonly IAnomalyDetectionService _anomalyService;
    private readonly ISecurityRiskService _securityRiskService;
    private readonly IRateLimitService _rateLimitService;
    private readonly ILogger<PdfReportService> _logger;

    public PdfReportService(
        IApplicationInsightsService insightsService,
        IAnomalyDetectionService anomalyService,
        ISecurityRiskService securityRiskService,
        IRateLimitService rateLimitService,
        ILogger<PdfReportService> logger)
    {
        _insightsService = insightsService;
        _anomalyService = anomalyService;
        _securityRiskService = securityRiskService;
        _rateLimitService = rateLimitService;
        _logger = logger;

        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<byte[]> GenerateWeeklyReportAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating weekly report from {From} to {To}", from, to);

        var reportData = await CollectReportDataAsync(from, to);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(header => ComposeHeader(header, reportData));
                page.Content().Element(content => ComposeContent(content, reportData));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    public async Task<byte[]> GenerateInsightsReportAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var reportData = await CollectReportDataAsync(from, to);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(header => ComposeInsightsHeader(header, reportData));
                page.Content().Element(content => ComposeInsightsContent(content, reportData));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    public async Task<byte[]> GenerateSecurityReportAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var reportData = await CollectReportDataAsync(from, to);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(header => ComposeSecurityHeader(header, reportData));
                page.Content().Element(content => ComposeSecurityContent(content, reportData));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    private async Task<WeeklyReportData> CollectReportDataAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var reportData = new WeeklyReportData
        {
            ReportStart = from,
            ReportEnd = to
        };

        var stats = await _insightsService.GetDashboardStatsAsync(from, to);
        reportData.InsightsData = new ApplicationInsightsReportData
        {
            TotalPageViews = stats.TotalPageViews,
            TotalApiRequests = stats.TotalApiRequests,
            ActiveUsers = stats.ActiveUsers,
            UniqueVisitors = stats.UniqueVisitors,
            AvgSessionDurationMinutes = stats.AvgSessionDuration,
            BounceRatePercent = stats.BounceRate,
            ErrorRatePercent = stats.ErrorRate,
            AvgPageLoadTimeMs = stats.AvgPageLoadTimeMs,
            AvgApiResponseTimeMs = stats.AvgApiResponseTimeMs,
            ApiSuccessRatePercent = stats.ApiSuccessRate,
            TopPages = stats.TopPages.Take(10).ToList(),
            TopUsers = stats.TopUsers.Take(10).ToList(),
            TopApiEndpoints = stats.TopApiEndpoints.Take(10).ToList(),
            SlowestPages = stats.SlowPages.Take(5).ToList(),
            FastestPages = stats.FastestPages.Take(5).ToList(),
            MostUsedFeatures = stats.MostUsedFeatures.Take(8).ToList(),
            DeviceTypes = stats.DeviceTypes,
            Browsers = stats.Browsers,
            OperatingSystems = stats.OperatingSystems,
            TopCountries = stats.TopCountries.Take(10).ToList(),
            TopReferrers = stats.TopReferrers.Take(10).ToList(),
            PeakHours = stats.PeakHours.Take(5).ToList(),
            UserRetention = stats.UserRetention,
            NewVsReturningUsers = stats.NewVsReturningUsers,
            RoleActivity = stats.RoleActivity,
            WarehouseActivity = stats.WarehouseActivity,
            TopErrorPages = stats.TopErrorPages.Take(5).ToList(),
            ApiEndpointPerformance = stats.ApiEndpointPerformance.Take(5).ToList(),
            DailyPageViews = ConvertToDailyStats(stats.HourlyPageViews),
            DailyApiRequests = ConvertToDailyStats(stats.HourlyApiRequests)
        };

        try
        {
            var highRiskUsers = await _securityRiskService.GetHighRiskUsersAsync();

            reportData.SecurityData = new SecurityCenterReportData
            {
                HighRiskUsersCount = highRiskUsers.Count,
                HighRiskUsersList = highRiskUsers.Select(u => new SecurityRiskReportItem
                {
                    Username = u.Username,
                    RiskLevel = u.RiskLevel.ToString(),
                    RiskScore = u.RiskScore,
                    RiskFactors = u.RiskFactors?.Select(rf => rf.Factor ?? "Unknown").ToList() ?? new()
                }).ToList(),
                TotalAnomalies = 0,
                CriticalAnomalies = 0,
                TotalSecurityEvents = 0,
                LowRiskCount = 0,
                MediumRiskCount = 0,
                HighRiskCount = highRiskUsers.Count(u => u.RiskLevel.ToString() == "High"),
                CriticalRiskCount = highRiskUsers.Count(u => u.RiskLevel.ToString() == "Critical"),
                TotalAuditLogs = 0,
                FailedLoginAttempts = 0,
                UnauthorizedAccessAttempts = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting security data");
        }

        try
        {
            reportData.SecurityThreats = await CollectSecurityThreatsDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting security threats data");
            reportData.SecurityThreats = new SecurityThreatsReportData();
        }

        return reportData;
    }

    private void ComposeHeader(IContainer container, WeeklyReportData data)
    {
        container.Column(column =>
        {
            column.Item().Background(Colors.Blue.Darken2).Padding(15).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("LagerSystem W\u00f6chentlicher Report").FontSize(20).Bold().FontColor(Colors.White);
                    col.Item().Text($"Woche: {data.ReportStart:dd.MM.yyyy} - {data.ReportEnd:dd.MM.yyyy}").FontSize(12).FontColor(Colors.Grey.Lighten3);
                });

                row.ConstantItem(100).AlignRight().Text($"Erstellt: {DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(9).FontColor(Colors.Grey.Lighten3);
            });

            column.Item().PaddingVertical(5);
        });
    }

    private void ComposeInsightsHeader(IContainer container, WeeklyReportData data)
    {
        container.Background(Colors.Blue.Darken2).Padding(15).Text("Application Insights Report")
            .FontSize(20).Bold().FontColor(Colors.White);
    }

    private void ComposeSecurityHeader(IContainer container, WeeklyReportData data)
    {
        container.Background(Colors.Red.Darken2).Padding(15).Text("Security Center Report")
            .FontSize(20).Bold().FontColor(Colors.White);
    }

    private void ComposeContent(IContainer container, WeeklyReportData data)
    {
        container.Column(column =>
        {
            column.Spacing(15);

            column.Item().Element(c => ComposeExecutiveSummary(c, data));
            column.Item().PageBreak();
            column.Item().Element(c => ComposeInsightsSection(c, data));
            column.Item().PageBreak();
            column.Item().Element(c => ComposeSecuritySection(c, data));
            column.Item().PageBreak();
            column.Item().Element(c => ComposeSecurityThreatsSection(c, data));
        });
    }

    private void ComposeExecutiveSummary(IContainer container, WeeklyReportData data)
    {
        container.Column(column =>
        {
            column.Item().Text("Zusammenfassung").FontSize(16).Bold();
            column.Item().PaddingBottom(10);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Metrik").Bold();
                table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Wert").Bold();

                table.Cell().Padding(5).Text("Gesamt Page Views");
                table.Cell().Padding(5).Text(data.InsightsData.TotalPageViews.ToString("N0"));

                table.Cell().Padding(5).Text("Gesamt API Requests");
                table.Cell().Padding(5).Text(data.InsightsData.TotalApiRequests.ToString("N0"));

                table.Cell().Padding(5).Text("Aktive Benutzer");
                table.Cell().Padding(5).Text(data.InsightsData.ActiveUsers.ToString("N0"));

                table.Cell().Padding(5).Text("Bounce Rate");
                table.Cell().Padding(5).Text($"{data.InsightsData.BounceRatePercent:F1}%");

                table.Cell().Background(Colors.Red.Lighten4).Padding(5).Text("High Risk Users").Bold();
                table.Cell().Background(Colors.Red.Lighten4).Padding(5).Text(data.SecurityData.HighRiskUsersCount.ToString()).Bold();

                table.Cell().Padding(5).Text("Kritische Anomalien");
                table.Cell().Padding(5).Text(data.SecurityData.CriticalAnomalies.ToString());

                table.Cell().Background(Colors.Red.Lighten3).Padding(5).Text("Security Threats (24h)").Bold();
                table.Cell().Background(Colors.Red.Lighten3).Padding(5).Text(data.SecurityThreats.TotalThreats.ToString()).Bold();

                table.Cell().Padding(5).Text("Global System Risk");
                var riskText = data.SecurityThreats.GlobalRiskScore >= 75 ? "KRITISCH" :
                    data.SecurityThreats.GlobalRiskScore >= 50 ? "HOCH" :
                    data.SecurityThreats.GlobalRiskScore >= 25 ? "MITTEL" : "NIEDRIG";
                table.Cell().Padding(5).Text($"{data.SecurityThreats.GlobalRiskScore:F0}/100 ({riskText})");
            });
        });
    }

    private void ComposeInsightsSection(IContainer container, WeeklyReportData data)
    {
        container.Column(column =>
        {
            column.Item().Text("Application Insights").FontSize(16).Bold().FontColor(Colors.Blue.Darken2);
            column.Item().PaddingBottom(10);

            // KPI cards
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Cell().Background(Colors.Blue.Lighten3).Padding(10).Column(col =>
                {
                    col.Item().Text("Page Views").FontSize(10).Bold().FontColor(Colors.White);
                    col.Item().Text(data.InsightsData.TotalPageViews.ToString("N0")).FontSize(18).Bold().FontColor(Colors.White);
                });

                table.Cell().Background(Colors.Green.Lighten3).Padding(10).Column(col =>
                {
                    col.Item().Text("API Requests").FontSize(10).Bold().FontColor(Colors.White);
                    col.Item().Text(data.InsightsData.TotalApiRequests.ToString("N0")).FontSize(18).Bold().FontColor(Colors.White);
                });

                table.Cell().Background(Colors.Orange.Lighten3).Padding(10).Column(col =>
                {
                    col.Item().Text("Durchschn. Antwortzeit").FontSize(10).Bold().FontColor(Colors.White);
                    col.Item().Text($"{data.InsightsData.AvgApiResponseTimeMs:F0}ms").FontSize(18).Bold().FontColor(Colors.White);
                });
            });

            column.Item().PaddingVertical(10);

            // Top Pages
            column.Item().Text("Top Pages").FontSize(12).Bold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(1);
                });

                table.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text("Seite").Bold();
                table.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text("Aufrufe").Bold();

                foreach (var page in data.InsightsData.TopPages.Take(10))
                {
                    table.Cell().Padding(3).Text(page.Key);
                    table.Cell().Padding(3).AlignRight().Text(page.Value.ToString("N0"));
                }
            });

            column.Item().PaddingVertical(10);

            // Performance metrics
            column.Item().Text("Performance-Metriken").FontSize(12).Bold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(1);
                });

                table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Metrik").Bold();
                table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Wert").Bold();
                table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter().Text("Status").Bold();

                table.Cell().Padding(5).Text("Bounce Rate");
                table.Cell().Padding(5).AlignRight().Text($"{data.InsightsData.BounceRatePercent:F1}%");
                var bounceColor = data.InsightsData.BounceRatePercent < 30 ? Colors.Green.Darken1 : Colors.Orange.Darken1;
                table.Cell().Padding(5).AlignCenter().Text(data.InsightsData.BounceRatePercent < 30 ? "Gut" : "Hoch")
                    .FontColor(bounceColor);

                table.Cell().Padding(5).Text("Error Rate");
                table.Cell().Padding(5).AlignRight().Text($"{data.InsightsData.ErrorRatePercent:F1}%");
                var errorColor = data.InsightsData.ErrorRatePercent < 1 ? Colors.Green.Darken1 : Colors.Red.Darken1;
                table.Cell().Padding(5).AlignCenter().Text(data.InsightsData.ErrorRatePercent < 1 ? "Gesund" : "Pr\u00fcfen")
                    .FontColor(errorColor);

                table.Cell().Padding(5).Text("Durchschn. Sitzungsdauer");
                table.Cell().Padding(5).AlignRight().Text($"{data.InsightsData.AvgSessionDurationMinutes:F1}m");
                table.Cell().Padding(5).AlignCenter().Text("-").FontColor(Colors.Blue.Darken1);
            });

            column.Item().PaddingVertical(10);

            // Device distribution
            column.Item().Text("Ger\u00e4te-Verteilung").FontSize(12).Bold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(2);
                });

                table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Ger\u00e4t").Bold();
                table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Anzahl").Bold();
                table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Anteil (%)").Bold();

                var totalDevices = data.InsightsData.DeviceTypes.Values.Sum();
                foreach (var device in data.InsightsData.DeviceTypes)
                {
                    var percentage = (double)device.Value / totalDevices * 100;
                    table.Cell().Padding(5).Text(device.Key);
                    table.Cell().Padding(5).AlignRight().Text(device.Value.ToString("N0"));
                    table.Cell().Padding(5).Text($"{percentage:F1}%");
                }
            });

            column.Item().PaddingVertical(10);

            // Most used features
            if (data.InsightsData.MostUsedFeatures.Any())
            {
                column.Item().Text("Meistgenutzte Features").FontSize(12).Bold();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                    });

                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Feature").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Nutzung").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter().Text("Trend").Bold();

                    foreach (var feature in data.InsightsData.MostUsedFeatures)
                    {
                        table.Cell().Padding(5).Text(feature.Key);
                        table.Cell().Padding(5).AlignRight().Text(feature.Value.ToString("N0"));
                        table.Cell().Padding(5).AlignCenter().Text("-").FontColor(Colors.Blue.Darken1).Bold();
                    }
                });

                column.Item().PaddingVertical(10);
            }

            // Browser distribution
            if (data.InsightsData.Browsers.Any())
            {
                column.Item().Text("Browser-Verteilung").FontSize(12).Bold();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                    });

                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Browser").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Anzahl").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Anteil (%)").Bold();

                    var totalBrowsers = data.InsightsData.Browsers.Values.Sum();
                    foreach (var browser in data.InsightsData.Browsers.Take(5))
                    {
                        var percentage = (double)browser.Value / totalBrowsers * 100;
                        table.Cell().Padding(5).Text(browser.Key);
                        table.Cell().Padding(5).AlignRight().Text(browser.Value.ToString("N0"));
                        table.Cell().Padding(5).Text($"{percentage:F1}%");
                    }
                });

                column.Item().PaddingVertical(10);
            }

            // Traffic sources
            if (data.InsightsData.TopReferrers.Any())
            {
                column.Item().Text("Traffic-Quellen (Top Referrers)").FontSize(12).Bold();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(1);
                    });

                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Quelle").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Besuche").Bold();

                    foreach (var referrer in data.InsightsData.TopReferrers.Take(5))
                    {
                        var displayText = referrer.Key.Length > 40 ? referrer.Key.Substring(0, 37) + "..." : referrer.Key;
                        table.Cell().Padding(5).Text(displayText).FontSize(8);
                        table.Cell().Padding(5).AlignRight().Text(referrer.Value.ToString("N0"));
                    }
                });

                column.Item().PaddingVertical(10);
            }

            // User engagement
            column.Item().Text("Benutzer-Engagement").FontSize(12).Bold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(1);
                });

                table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Metrik").Bold();
                table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Wert").Bold();
                table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter().Text("Status").Bold();

                table.Cell().Padding(5).Text("Benutzer-Retention");
                table.Cell().Padding(5).AlignRight().Text($"{data.InsightsData.UserRetention:F1}%");
                var retentionColor = data.InsightsData.UserRetention > 40 ? Colors.Green.Darken1 : Colors.Orange.Darken1;
                table.Cell().Padding(5).AlignCenter().Text(data.InsightsData.UserRetention > 40 ? "Gut" : "Mittel")
                    .FontColor(retentionColor);

                if (data.InsightsData.NewVsReturningUsers.Any())
                {
                    var newUsers = data.InsightsData.NewVsReturningUsers.GetValueOrDefault("New Users", 0);
                    var returningUsers = data.InsightsData.NewVsReturningUsers.GetValueOrDefault("Returning Users", 0);

                    table.Cell().Padding(5).Text("Neue Benutzer");
                    table.Cell().Padding(5).AlignRight().Text(newUsers.ToString("N0"));
                    table.Cell().Padding(5).AlignCenter().Text("-").FontColor(Colors.Blue.Darken1);

                    table.Cell().Padding(5).Text("Wiederkehrende Benutzer");
                    table.Cell().Padding(5).AlignRight().Text(returningUsers.ToString("N0"));
                    table.Cell().Padding(5).AlignCenter().Text("-").FontColor(Colors.Green.Darken1);
                }
            });
        });
    }

    private void ComposeInsightsContent(IContainer container, WeeklyReportData data)
    {
        container.Element(c => ComposeInsightsSection(c, data));
    }

    private void ComposeSecuritySection(IContainer container, WeeklyReportData data)
    {
        container.Column(column =>
        {
            column.Item().Text("Security Center").FontSize(16).Bold().FontColor(Colors.Red.Darken2);
            column.Item().PaddingBottom(10);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Cell().Background(Colors.Red.Lighten3).Padding(10).Column(col =>
                {
                    col.Item().Text("High Risk Users").FontSize(10).Bold().FontColor(Colors.White);
                    col.Item().Text(data.SecurityData.HighRiskUsersCount.ToString()).FontSize(18).Bold().FontColor(Colors.White);
                });

                table.Cell().Background(Colors.Orange.Lighten3).Padding(10).Column(col =>
                {
                    col.Item().Text("Anomalien").FontSize(10).Bold().FontColor(Colors.White);
                    col.Item().Text(data.SecurityData.TotalAnomalies.ToString()).FontSize(18).Bold().FontColor(Colors.White);
                });

                table.Cell().Background(Colors.Yellow.Lighten3).Padding(10).Column(col =>
                {
                    col.Item().Text("Fehlgeschlagene Logins").FontSize(10).Bold().FontColor(Colors.Grey.Darken3);
                    col.Item().Text(data.SecurityData.FailedLoginAttempts.ToString()).FontSize(18).Bold().FontColor(Colors.Grey.Darken3);
                });
            });

            column.Item().PaddingVertical(10);

            if (data.SecurityData.HighRiskUsersList.Any())
            {
                column.Item().Text("High Risk Users - Details").FontSize(12).Bold();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                    });

                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Benutzername").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter().Text("Risiko-Level").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Score").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Risikofaktoren").Bold();

                    foreach (var user in data.SecurityData.HighRiskUsersList.Take(10))
                    {
                        table.Cell().Padding(5).Text(user.Username);
                        var riskColor = user.RiskLevel == "Critical" ? Colors.Red.Darken1 :
                            user.RiskLevel == "High" ? Colors.Orange.Darken1 : Colors.Yellow.Darken1;
                        table.Cell().Padding(5).AlignCenter().Text(user.RiskLevel).FontColor(riskColor).Bold();
                        table.Cell().Padding(5).AlignRight().Text($"{user.RiskScore:F1}");
                        table.Cell().Padding(5).Text(string.Join(", ", user.RiskFactors.Take(2))).FontSize(8);
                    }
                });
            }

            column.Item().PaddingVertical(10);

            // Security recommendations
            column.Item().Text("Sicherheits-Empfehlungen").FontSize(12).Bold();
            column.Item().Background(Colors.Blue.Lighten4).Padding(10).Column(col =>
            {
                col.Item().Text("\u2022 2FA f\u00fcr alle High-Risk-Benutzer aktivieren").FontSize(9);
                col.Item().Text("\u2022 Passw\u00f6rter der Benutzer mit mehreren fehlgeschlagenen Login-Versuchen zur\u00fccksetzen").FontSize(9);
                col.Item().Text("\u2022 Audit-Logs der letzten kritischen Aktionen \u00fcberpr\u00fcfen").FontSize(9);
                col.Item().Text("\u2022 Ungew\u00f6hnliche IP-Adressen blocken").FontSize(9);
            });
        });
    }

    private void ComposeSecurityContent(IContainer container, WeeklyReportData data)
    {
        container.Element(c => ComposeSecuritySection(c, data));
    }

    /// <summary>
    /// Collects security threats data from the RateLimitService (in-memory).
    /// </summary>
    private async Task<SecurityThreatsReportData> CollectSecurityThreatsDataAsync(CancellationToken cancellationToken = default)
    {
        var threatsData = new SecurityThreatsReportData();

        var recentRequests = _rateLimitService.GetRecentRequests(2000);

        if (!recentRequests.Any())
        {
            _logger.LogWarning("No request logs found for security threats analysis");
            return threatsData;
        }

        var identifiers = recentRequests.Select(r => r.Identifier).Distinct().ToList();

        // Burst attack detection
        foreach (var identifier in identifiers.Take(50))
        {
            var detection = _rateLimitService.DetectBurstAttack(identifier);
            if (detection.IsBurstAttack)
            {
                threatsData.BurstAttacks.Add(new ThreatIncident
                {
                    Timestamp = DateTime.UtcNow,
                    Identifier = detection.Identifier,
                    RequestCount = detection.RequestsInBurst,
                    DurationSeconds = detection.BurstDuration.TotalSeconds,
                    RequestsPerSecond = detection.RequestsPerSecond,
                    Severity = "Critical"
                });
            }
        }

        // Brute-force detection
        foreach (var identifier in identifiers.Take(50))
        {
            var detection = _rateLimitService.DetectBruteForce(identifier);
            if (detection.IsBruteForce)
            {
                threatsData.BruteForceAttacks.Add(new ThreatIncident
                {
                    Timestamp = DateTime.UtcNow,
                    Identifier = detection.Identifier,
                    FailedAttempts = detection.FailedAttempts,
                    DurationMinutes = detection.AttackDuration.TotalMinutes,
                    TargetedEndpoints = detection.TargetedEndpoints,
                    Severity = "High"
                });
            }
        }

        // DDoS pattern detection
        var ddosDetection = _rateLimitService.DetectDDoS(TimeSpan.FromMinutes(5));
        if (ddosDetection.IsDDoSPattern)
        {
            threatsData.DDoSPatterns.Add(new ThreatIncident
            {
                Timestamp = DateTime.UtcNow,
                UniqueIPs = ddosDetection.UniqueIPsInvolved,
                TotalRequests = ddosDetection.TotalRequests,
                AverageRequestsPerIP = ddosDetection.AverageRequestsPerIP,
                SuspiciousIPs = ddosDetection.SuspiciousIPs.Take(10).ToList(),
                Severity = "Critical"
            });
        }

        // Slow-rate attack detection
        var slowRateDetection = _rateLimitService.DetectSlowRateAttack();
        if (slowRateDetection.IsSlowRateAttack)
        {
            threatsData.SlowRateAttacks.Add(new ThreatIncident
            {
                Timestamp = DateTime.UtcNow,
                SuspiciousPatternCount = slowRateDetection.SuspiciousPatternCount,
                ConsistentOffenders = slowRateDetection.ConsistentOffenders.Take(5).ToList(),
                Severity = "Medium"
            });
        }

        threatsData.GlobalRiskScore = await _securityRiskService.CalculateGlobalSystemRiskAsync();

        threatsData.TotalThreats = threatsData.BurstAttacks.Count +
            threatsData.BruteForceAttacks.Count +
            threatsData.DDoSPatterns.Count +
            threatsData.SlowRateAttacks.Count;

        _logger.LogInformation("Security threats summary: {Total} threats detected", threatsData.TotalThreats);

        return threatsData;
    }

    /// <summary>
    /// Composes the security threats section (burst, brute-force, DDoS, slow-rate).
    /// </summary>
    private void ComposeSecurityThreatsSection(IContainer container, WeeklyReportData data)
    {
        container.Column(column =>
        {
            column.Item().Text("Security Threats (Echtzeit-Monitoring)").FontSize(16).Bold().FontColor(Colors.Red.Darken3);
            column.Item().PaddingBottom(10);

            // Global risk score KPI
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                var riskColor = data.SecurityThreats.GlobalRiskScore >= 75 ? Colors.Red.Darken1 :
                    data.SecurityThreats.GlobalRiskScore >= 50 ? Colors.Orange.Darken1 :
                    data.SecurityThreats.GlobalRiskScore >= 25 ? Colors.Yellow.Darken1 : Colors.Green.Darken1;

                table.Cell().Background(riskColor).Padding(10).Column(col =>
                {
                    col.Item().Text("Global System Risk").FontSize(10).Bold().FontColor(Colors.White);
                    col.Item().Text($"{data.SecurityThreats.GlobalRiskScore:F0}/100").FontSize(18).Bold().FontColor(Colors.White);
                });

                table.Cell().Background(Colors.Red.Lighten3).Padding(10).Column(col =>
                {
                    col.Item().Text("Gesamt Threats").FontSize(10).Bold().FontColor(Colors.White);
                    col.Item().Text(data.SecurityThreats.TotalThreats.ToString()).FontSize(18).Bold().FontColor(Colors.White);
                });

                table.Cell().Background(Colors.Orange.Lighten3).Padding(10).Column(col =>
                {
                    col.Item().Text("Zeitfenster").FontSize(10).Bold().FontColor(Colors.White);
                    col.Item().Text("Letzte 24h").FontSize(18).Bold().FontColor(Colors.White);
                });
            });

            column.Item().PaddingVertical(10);

            // Burst attacks
            if (data.SecurityThreats.BurstAttacks.Any())
            {
                column.Item().Text($"Burst Attacks ({data.SecurityThreats.BurstAttacks.Count})").FontSize(12).Bold().FontColor(Colors.Red.Darken2);
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                    });

                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Angreifer").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Requests").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Dauer (s)").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Rate (req/s)").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter().Text("Status").Bold();

                    foreach (var burst in data.SecurityThreats.BurstAttacks.Take(10))
                    {
                        table.Cell().Padding(5).Text(burst.Identifier).FontSize(8);
                        table.Cell().Padding(5).AlignRight().Text(burst.RequestCount.ToString());
                        table.Cell().Padding(5).AlignRight().Text($"{burst.DurationSeconds:F1}");
                        table.Cell().Padding(5).AlignRight().Text($"{burst.RequestsPerSecond:F1}");
                        table.Cell().Padding(5).AlignCenter().Text("KRITISCH").FontColor(Colors.Red.Darken1).Bold();
                    }
                });

                column.Item().PaddingVertical(10);
            }

            // Brute-force attacks
            if (data.SecurityThreats.BruteForceAttacks.Any())
            {
                column.Item().Text($"Brute-Force Attacks ({data.SecurityThreats.BruteForceAttacks.Count})").FontSize(12).Bold().FontColor(Colors.Orange.Darken2);
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                    });

                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Angreifer").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Fehlversuche").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Dauer (min)").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Ziel-Endpoints").Bold();

                    foreach (var brute in data.SecurityThreats.BruteForceAttacks.Take(10))
                    {
                        table.Cell().Padding(5).Text(brute.Identifier).FontSize(8);
                        table.Cell().Padding(5).AlignRight().Text(brute.FailedAttempts.ToString());
                        table.Cell().Padding(5).AlignRight().Text($"{brute.DurationMinutes:F1}");
                        table.Cell().Padding(5).Text(string.Join(", ", brute.TargetedEndpoints.Take(2))).FontSize(7);
                    }
                });

                column.Item().PaddingVertical(10);
            }

            // DDoS patterns
            if (data.SecurityThreats.DDoSPatterns.Any())
            {
                column.Item().Text($"DDoS Patterns ({data.SecurityThreats.DDoSPatterns.Count})").FontSize(12).Bold().FontColor(Colors.Red.Darken3);
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                    });

                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Unique IPs").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Total Requests").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("\u00d8 Req/IP").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Top Offender IPs").Bold();

                    foreach (var ddos in data.SecurityThreats.DDoSPatterns.Take(5))
                    {
                        table.Cell().Padding(5).AlignRight().Text(ddos.UniqueIPs.ToString());
                        table.Cell().Padding(5).AlignRight().Text(ddos.TotalRequests.ToString());
                        table.Cell().Padding(5).AlignRight().Text($"{ddos.AverageRequestsPerIP:F1}");
                        table.Cell().Padding(5).Text(string.Join(", ", ddos.SuspiciousIPs.Take(5))).FontSize(7);
                    }
                });

                column.Item().PaddingVertical(10);
            }

            // Slow-rate attacks
            if (data.SecurityThreats.SlowRateAttacks.Any())
            {
                column.Item().Text($"Slow-Rate Attacks ({data.SecurityThreats.SlowRateAttacks.Count})").FontSize(12).Bold().FontColor(Colors.Yellow.Darken2);
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(3);
                    });

                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Verd\u00e4chtige IPs").Bold();
                    table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Offender IPs").Bold();

                    foreach (var slow in data.SecurityThreats.SlowRateAttacks.Take(5))
                    {
                        table.Cell().Padding(5).AlignRight().Text(slow.SuspiciousPatternCount.ToString());
                        table.Cell().Padding(5).Text(string.Join(", ", slow.ConsistentOffenders.Take(10))).FontSize(7);
                    }
                });

                column.Item().PaddingVertical(10);
            }

            // No threats detected
            if (data.SecurityThreats.TotalThreats == 0)
            {
                column.Item().Background(Colors.Green.Lighten4).Padding(15).Column(col =>
                {
                    col.Item().Text("KEINE SECURITY THREATS ERKANNT").FontSize(14).Bold().FontColor(Colors.Green.Darken2).AlignCenter();
                    col.Item().Text("Alle Systeme sicher. Keine Burst-, Brute-Force-, DDoS- oder Slow-Rate-Angriffe erkannt.").FontSize(10).FontColor(Colors.Grey.Darken2).AlignCenter();
                });
            }

            // Recommendations
            column.Item().PaddingVertical(10);
            column.Item().Text("Sicherheits-Empfehlungen").FontSize(12).Bold();
            column.Item().Background(Colors.Blue.Lighten4).Padding(10).Column(col =>
            {
                if (data.SecurityThreats.BurstAttacks.Any())
                    col.Item().Text("\u2022 Burst Attack erkannt -> Rate-Limits versch\u00e4rfen").FontSize(9);

                if (data.SecurityThreats.BruteForceAttacks.Any())
                    col.Item().Text("\u2022 Brute-Force erkannt -> IP-Blacklist aktualisieren + 2FA aktivieren").FontSize(9);

                if (data.SecurityThreats.DDoSPatterns.Any())
                    col.Item().Text("\u2022 DDoS Pattern erkannt -> CDN/WAF aktivieren + Netzwerk-Verteidigung").FontSize(9);

                if (data.SecurityThreats.SlowRateAttacks.Any())
                    col.Item().Text("\u2022 Slow-Rate Attack erkannt -> Langzeit-Monitoring aktivieren").FontSize(9);

                if (data.SecurityThreats.GlobalRiskScore >= 75)
                    col.Item().Text("\u2022 KRITISCHES RISIKO -> Sofortige \u00dcberpr\u00fcfung aller Systeme!").FontSize(9).Bold().FontColor(Colors.Red.Darken2);

                col.Item().Text("\u2022 Security Dashboard regelm\u00e4\u00dfig \u00fcberpr\u00fcfen: /admin/security-threats").FontSize(9);
                col.Item().Text("\u2022 Audit-Logs auf ungew\u00f6hnliche Aktivit\u00e4ten pr\u00fcfen").FontSize(9);
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Text("Generated by LagerSystem LV Home")
            .FontSize(9).FontColor(Colors.Grey.Darken1);
    }

    private List<DailyStats> ConvertToDailyStats(List<HourlyStats> hourlyStats)
    {
        return hourlyStats
            .GroupBy(h => h.Hour.Date)
            .Select(g => new DailyStats
            {
                Date = g.Key,
                Count = g.Sum(x => x.Count)
            })
            .OrderBy(d => d.Date)
            .ToList();
    }
}
