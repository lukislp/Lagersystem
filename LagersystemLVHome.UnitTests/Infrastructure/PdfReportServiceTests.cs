using System.Text;
using System.Text.RegularExpressions;
using LagersystemLVHome.Infrastructure.ML.Models;
using LagersystemLVHome.Infrastructure.ML.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Infrastructure;

public class PdfReportServiceTests
{
    private static readonly DateTime From = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc);

    private static PdfReportService Build(
        IApplicationInsightsService? insightsService = null,
        IAnomalyDetectionService? anomalyService = null,
        ISecurityRiskService? securityRiskService = null,
        IRateLimitService? rateLimitService = null)
    {
        return new PdfReportService(
            insightsService ?? MakeInsightsService(MakeSparseStats()),
            anomalyService ?? Substitute.For<IAnomalyDetectionService>(),
            securityRiskService ?? MakeSecurityRiskService(new List<SecurityRiskAssessment>(), globalRisk: 0),
            rateLimitService ?? MakeRateLimitService(Array.Empty<RequestLog>()),
            NullLogger<PdfReportService>.Instance);
    }

    private static IApplicationInsightsService MakeInsightsService(ApplicationInsightsStats stats)
    {
        var service = Substitute.For<IApplicationInsightsService>();
        service.GetDashboardStatsAsync(Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(stats);
        return service;
    }

    private static ISecurityRiskService MakeSecurityRiskService(List<SecurityRiskAssessment> highRiskUsers, double globalRisk)
    {
        var service = Substitute.For<ISecurityRiskService>();
        service.GetHighRiskUsersAsync(Arg.Any<CancellationToken>()).Returns(highRiskUsers);
        service.CalculateGlobalSystemRiskAsync(Arg.Any<CancellationToken>()).Returns(globalRisk);
        return service;
    }

    private static IRateLimitService MakeRateLimitService(
        IReadOnlyList<RequestLog> recentRequests,
        Action<IRateLimitService>? configureDetections = null)
    {
        var service = Substitute.For<IRateLimitService>();
        service.GetRecentRequests(Arg.Any<int>()).Returns(recentRequests.ToList());

        // Safe defaults so any identifier the service iterates over resolves to a
        // "nothing detected" result unless a specific test overrides it below.
        service.DetectBurstAttack(Arg.Any<string>()).Returns(new BurstAttackDetection { IsBurstAttack = false });
        service.DetectBruteForce(Arg.Any<string>()).Returns(new BruteForceDetection { IsBruteForce = false });
        service.DetectDDoS(Arg.Any<TimeSpan>()).Returns(new DDoSDetection { IsDDoSPattern = false });
        service.DetectSlowRateAttack().Returns(new SlowRateAttackDetection { IsSlowRateAttack = false });

        configureDetections?.Invoke(service);
        return service;
    }

    private static ApplicationInsightsStats MakeRichStats() => new()
    {
        TotalPageViews = 1000,
        TotalApiRequests = 500,
        ActiveUsers = 42,
        UniqueVisitors = 30,
        AvgSessionDuration = 5.5,
        BounceRate = 15.0,      // < 30 -> "Gut" branch
        ErrorRate = 0.2,        // < 1 -> "Gesund" branch
        AvgPageLoadTimeMs = 120,
        AvgApiResponseTimeMs = 80,
        ApiSuccessRate = 99.2,
        TopPages = new() { new("/home", 100), new("/products", 80) },
        TopUsers = new() { new("alice", 50) },
        TopApiEndpoints = new() { new("/api/products", 200) },
        SlowPages = new() { new("/reports", 3.2) },
        FastestPages = new() { new("/home", 0.1) },
        MostUsedFeatures = new() { new("Scan", 40), new("Search", 20) },
        DeviceTypes = new() { ["Desktop"] = 60, ["Mobile"] = 40 },
        Browsers = new() { ["Chrome"] = 70, ["Firefox"] = 30 },
        OperatingSystems = new() { ["Windows"] = 80, ["macOS"] = 20 },
        TopCountries = new() { new("Germany", 90) },
        TopReferrers = new() { new(new string('x', 60), 10), new("short.example", 5) },
        PeakHours = new() { new("14:00", 120) },
        UserRetention = 60.0,   // > 40 -> "Gut" branch
        NewVsReturningUsers = new() { ["New Users"] = 20, ["Returning Users"] = 80 },
        RoleActivity = new() { ["Admin"] = 5 },
        WarehouseActivity = new() { ["WH1"] = 15 },
        TopErrorPages = new() { new("/broken", 3) },
        ApiEndpointPerformance = new() { new("/api/slow", 500.0) },
        HourlyPageViews = new()
        {
            new HourlyStats { Hour = From.AddHours(1), Count = 10 },
            new HourlyStats { Hour = From.AddHours(2), Count = 5 },
            new HourlyStats { Hour = From.AddDays(1).AddHours(3), Count = 7 }
        },
        HourlyApiRequests = new()
        {
            new HourlyStats { Hour = From.AddHours(1), Count = 3 }
        }
    };

    private static ApplicationInsightsStats MakeSparseStats() => new()
    {
        TotalPageViews = 10,
        TotalApiRequests = 5,
        ActiveUsers = 1,
        BounceRate = 45.0,   // >= 30 -> "Hoch" branch
        ErrorRate = 5.0,     // >= 1 -> "Prüfen" branch
        UserRetention = 20.0 // <= 40 -> "Mittel" branch
        // MostUsedFeatures, Browsers, TopReferrers, NewVsReturningUsers, DeviceTypes left empty
        // to exercise the "not Any()" branches.
    };

    private static List<SecurityRiskAssessment> MakeHighRiskUsers() => new()
    {
        new SecurityRiskAssessment
        {
            UserId = 1,
            Username = "eve",
            RiskLevel = RiskLevel.Critical,
            RiskScore = 95,
            RiskFactors = new() { new RiskFactor { Factor = "Multiple failed logins" }, new RiskFactor { Factor = "Unusual IP" } }
        },
        new SecurityRiskAssessment
        {
            UserId = 2,
            Username = "mallory",
            RiskLevel = RiskLevel.High,
            RiskScore = 80,
            RiskFactors = new() { new RiskFactor { Factor = "New device" } }
        },
        new SecurityRiskAssessment
        {
            UserId = 3,
            Username = "trent",
            RiskLevel = RiskLevel.Medium,
            RiskScore = 40,
            RiskFactors = new() { }
        }
    };

    private static List<RequestLog> MakeRequestLogs(params string[] identifiers) =>
        identifiers.Select(id => new RequestLog
        {
            Timestamp = DateTime.UtcNow,
            Identifier = id,
            Endpoint = "/api/x",
            IsSuccess = true,
            RequestCount = 1
        }).ToList();

    /// <summary>
    /// Counts PDF page objects ("/Type /Page", excluding "/Type /Pages") in the raw
    /// bytes. QuestPDF writes page dictionaries as plain (uncompressed) PDF objects,
    /// so this is a cheap structural sanity check without needing a full PDF parser.
    /// </summary>
    private static int CountPdfPages(byte[] pdfBytes)
    {
        var text = Encoding.Latin1.GetString(pdfBytes);
        return Regex.Matches(text, @"/Type\s*/Page(?!s)\b").Count;
    }

    private static void AssertIsValidPdf(byte[] bytes)
    {
        bytes.Should().NotBeNull();
        bytes.Should().NotBeEmpty();
        bytes.Length.Should().BeGreaterThan(500, "a rendered report should be more than a trivial stub");
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        CountPdfPages(bytes).Should().BeGreaterThanOrEqualTo(1);
    }

    // --- GenerateWeeklyReportAsync ---

    [Fact]
    public async Task GenerateWeeklyReportAsync_RichDataWithThreats_ReturnsMultiPagePdf()
    {
        var logs = MakeRequestLogs("burst-1", "brute-1", "normal-1");
        var rateLimit = MakeRateLimitService(logs, service =>
        {
            service.DetectBurstAttack("burst-1").Returns(new BurstAttackDetection
            {
                IsBurstAttack = true,
                Identifier = "burst-1",
                RequestsInBurst = 200,
                BurstDuration = TimeSpan.FromSeconds(3),
                RequestsPerSecond = 66.6
            });
            service.DetectBruteForce("brute-1").Returns(new BruteForceDetection
            {
                IsBruteForce = true,
                Identifier = "brute-1",
                FailedAttempts = 30,
                AttackDuration = TimeSpan.FromMinutes(4),
                TargetedEndpoints = new() { "/login", "/api/auth" }
            });
            service.DetectDDoS(Arg.Any<TimeSpan>()).Returns(new DDoSDetection
            {
                IsDDoSPattern = true,
                UniqueIPsInvolved = 500,
                TotalRequests = 20000,
                AverageRequestsPerIP = 40,
                SuspiciousIPs = Enumerable.Range(1, 15).Select(i => $"10.0.0.{i}").ToList()
            });
            service.DetectSlowRateAttack().Returns(new SlowRateAttackDetection
            {
                IsSlowRateAttack = true,
                SuspiciousPatternCount = 12,
                ConsistentOffenders = Enumerable.Range(1, 15).Select(i => $"offender-{i}").ToList()
            });
        });

        var sut = Build(
            MakeInsightsService(MakeRichStats()),
            securityRiskService: MakeSecurityRiskService(MakeHighRiskUsers(), globalRisk: 90),
            rateLimitService: rateLimit);

        var bytes = await sut.GenerateWeeklyReportAsync(From, To);

        AssertIsValidPdf(bytes);
        // ComposeContent explicitly inserts 3 PageBreak() calls between the four
        // sections, so a full weekly report must be at least 4 pages regardless
        // of how much content overflows within each section.
        CountPdfPages(bytes).Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task GenerateWeeklyReportAsync_SparseDataNoThreatsDetected_ReturnsValidPdf()
    {
        var logs = MakeRequestLogs("visitor-1");
        var rateLimit = MakeRateLimitService(logs); // all Detect* default to "nothing found"

        var sut = Build(
            MakeInsightsService(MakeSparseStats()),
            securityRiskService: MakeSecurityRiskService(new List<SecurityRiskAssessment>(), globalRisk: 5),
            rateLimitService: rateLimit);

        var bytes = await sut.GenerateWeeklyReportAsync(From, To);

        AssertIsValidPdf(bytes);
        CountPdfPages(bytes).Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task GenerateWeeklyReportAsync_NoRecentRequests_SkipsThreatDetectionEntirely()
    {
        var rateLimit = MakeRateLimitService(Array.Empty<RequestLog>());
        var securityRisk = MakeSecurityRiskService(new List<SecurityRiskAssessment>(), globalRisk: 0);
        var sut = Build(MakeInsightsService(MakeSparseStats()), securityRiskService: securityRisk, rateLimitService: rateLimit);

        var bytes = await sut.GenerateWeeklyReportAsync(From, To);

        AssertIsValidPdf(bytes);
        rateLimit.DidNotReceive().DetectBurstAttack(Arg.Any<string>());
        rateLimit.DidNotReceive().DetectBruteForce(Arg.Any<string>());
        rateLimit.DidNotReceive().DetectDDoS(Arg.Any<TimeSpan>());
        rateLimit.DidNotReceive().DetectSlowRateAttack();
        await securityRisk.DidNotReceive().CalculateGlobalSystemRiskAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateWeeklyReportAsync_SecurityRiskServiceThrows_FallsBackToDefaultSecurityDataWithoutThrowing()
    {
        var securityRisk = Substitute.For<ISecurityRiskService>();
        securityRisk.GetHighRiskUsersAsync(Arg.Any<CancellationToken>())
            .Returns<List<SecurityRiskAssessment>>(_ => throw new InvalidOperationException("ML service unavailable"));
        securityRisk.CalculateGlobalSystemRiskAsync(Arg.Any<CancellationToken>()).Returns(0.0);

        var sut = Build(
            MakeInsightsService(MakeSparseStats()),
            securityRiskService: securityRisk,
            rateLimitService: MakeRateLimitService(Array.Empty<RequestLog>()));

        var act = async () => await sut.GenerateWeeklyReportAsync(From, To);

        var bytes = await act.Should().NotThrowAsync();
        AssertIsValidPdf(bytes.Subject);
    }

    [Fact]
    public async Task GenerateWeeklyReportAsync_RateLimitServiceThrows_FallsBackToDefaultThreatsDataWithoutThrowing()
    {
        var rateLimit = Substitute.For<IRateLimitService>();
        rateLimit.GetRecentRequests(Arg.Any<int>()).Returns(_ => throw new InvalidOperationException("rate limiter offline"));

        var sut = Build(
            MakeInsightsService(MakeSparseStats()),
            securityRiskService: MakeSecurityRiskService(new List<SecurityRiskAssessment>(), globalRisk: 0),
            rateLimitService: rateLimit);

        var act = async () => await sut.GenerateWeeklyReportAsync(From, To);

        var bytes = await act.Should().NotThrowAsync();
        AssertIsValidPdf(bytes.Subject);
    }

    [Theory]
    [InlineData(90.0)] // >= 75 -> "KRITISCH" band
    [InlineData(60.0)] // >= 50 -> "HOCH" band
    [InlineData(30.0)] // >= 25 -> "MITTEL" band
    [InlineData(10.0)] // < 25  -> "NIEDRIG" band
    public async Task GenerateWeeklyReportAsync_VariousGlobalRiskScores_ReturnsValidPdf(double globalRiskScore)
    {
        var rateLimit = MakeRateLimitService(MakeRequestLogs("id-1"));
        var securityRisk = MakeSecurityRiskService(new List<SecurityRiskAssessment>(), globalRiskScore);
        var sut = Build(MakeInsightsService(MakeSparseStats()), securityRiskService: securityRisk, rateLimitService: rateLimit);

        var bytes = await sut.GenerateWeeklyReportAsync(From, To);

        AssertIsValidPdf(bytes);
    }

    // --- GenerateInsightsReportAsync ---

    [Fact]
    public async Task GenerateInsightsReportAsync_RichData_ReturnsValidPdf()
    {
        var sut = Build(MakeInsightsService(MakeRichStats()));

        var bytes = await sut.GenerateInsightsReportAsync(From, To);

        AssertIsValidPdf(bytes);
    }

    [Fact]
    public async Task GenerateInsightsReportAsync_SparseData_ReturnsValidPdf()
    {
        var sut = Build(MakeInsightsService(MakeSparseStats()));

        var bytes = await sut.GenerateInsightsReportAsync(From, To);

        AssertIsValidPdf(bytes);
    }

    // --- GenerateSecurityReportAsync ---

    [Fact]
    public async Task GenerateSecurityReportAsync_WithHighRiskUsers_ReturnsValidPdf()
    {
        var sut = Build(
            MakeInsightsService(MakeSparseStats()),
            securityRiskService: MakeSecurityRiskService(MakeHighRiskUsers(), globalRisk: 50));

        var bytes = await sut.GenerateSecurityReportAsync(From, To);

        AssertIsValidPdf(bytes);
    }

    [Fact]
    public async Task GenerateSecurityReportAsync_NoHighRiskUsers_ReturnsValidPdf()
    {
        var sut = Build(
            MakeInsightsService(MakeSparseStats()),
            securityRiskService: MakeSecurityRiskService(new List<SecurityRiskAssessment>(), globalRisk: 0));

        var bytes = await sut.GenerateSecurityReportAsync(From, To);

        AssertIsValidPdf(bytes);
    }
}
