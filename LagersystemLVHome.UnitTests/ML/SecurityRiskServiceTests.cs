using LagersystemLVHome.Data;
using LagersystemLVHome.Infrastructure.ML.Models;
using LagersystemLVHome.Infrastructure.ML.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace LagersystemLVHome.UnitTests.ML;

/// <summary>
/// <see cref="SecurityRiskService"/> is a rule-based (no ML.NET) scoring service.
/// These tests pin the scoring thresholds, the optional <see cref="IRateLimitService"/>
/// integration (resolved lazily via <see cref="IServiceProvider"/>), and the
/// exception-swallowing behaviour of each public method.
/// </summary>
public class SecurityRiskServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        // Prefixed with the class name: EF Core's InMemory provider keys databases by name in
        // a store shared across the whole test process, so an unqualified nameof(TestMethod)
        // can collide with an identically-named test in a different test class.
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>()
                .UseInMemoryDatabase(nameof(SecurityRiskServiceTests) + "." + name).Options);

    private static IServiceProvider NoRateLimitServiceProvider()
    {
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IRateLimitService)).Returns((object?)null);
        return sp;
    }

    /// <summary>Configures every member the production code calls unconditionally,
    /// with values that never trigger a bonus, so individual tests only need to
    /// override the specific call(s) they care about.</summary>
    private static IRateLimitService BaselineRateLimitService()
    {
        var rl = Substitute.For<IRateLimitService>();
        rl.DetectDDoS(Arg.Any<TimeSpan>()).Returns(new DDoSDetection { IsDDoSPattern = false });
        rl.DetectBurstAttack(Arg.Any<string>()).Returns(new BurstAttackDetection { IsBurstAttack = false });
        rl.DetectBruteForce(Arg.Any<string>()).Returns(new BruteForceDetection { IsBruteForce = false, TargetedEndpoints = new List<string>() });
        rl.GetGlobalStatistics().Returns(new RateLimitStatistics { TotalRequests = 0, BlockedRequests = 0, BlockRate = 0, ActiveBuckets = 0 });
        rl.GetRecentRequests(Arg.Any<int>()).Returns(new List<RequestLog>());
        return rl;
    }

    private static IServiceProvider RateLimitServiceProvider(IRateLimitService rl)
    {
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IRateLimitService)).Returns(rl);
        return sp;
    }

    private static SecurityRiskService CreateSut(IDbContextFactory<InventoryDbContext> factory, IServiceProvider? sp = null)
        => new(factory, NullLogger<SecurityRiskService>.Instance, sp ?? NoRateLimitServiceProvider());

    private static Warehouse MakeWarehouse(int id = 1) => new() { Id = id, Name = "WH" + id, Address = "a" };

    private static User MakeUser(
        int id,
        bool twoFactor = true,
        DateTime? createdAt = null,
        DateTime? lastPasswordChangeAt = null) => new()
        {
            Id = id,
            Username = "u" + id,
            Email = $"u{id}@x.local",
            DisplayName = "User " + id,
            PasswordHash = "x",
            WarehouseId = 1,
            TwoFactorEnabled = twoFactor,
            CreatedAt = createdAt ?? DateTime.UtcNow.AddDays(-200),
            LastPasswordChangeAt = lastPasswordChangeAt ?? DateTime.UtcNow.AddDays(-10),
            IsActive = true,
            IsDeleted = false
        };

    private static AuditLog Log(int? userId, string action, DateTime timestamp, string ip = "10.0.0.1")
        => new() { UserId = userId, Action = action, Timestamp = timestamp, Entity = "x" };

    private static async Task SeedAsync(IDbContextFactory<InventoryDbContext> factory, User user, IEnumerable<AuditLog>? logs = null)
    {
        await using var db = factory.CreateDbContext();
        db.Warehouses.Add(MakeWarehouse());
        db.Users.Add(user);
        if (logs != null) db.AuditLogs.AddRange(logs);
        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------
    // AssessUserRiskAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task AssessUserRiskAsync_UserNotFound_ReturnsFallbackAssessment()
    {
        var factory = CreateFactory(nameof(AssessUserRiskAsync_UserNotFound_ReturnsFallbackAssessment));
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(999);

        result.Username.Should().Be("Unknown");
        result.RiskLevel.Should().Be(RiskLevel.Low);
        result.RiskScore.Should().Be(0);
        result.RiskFactors.Should().BeEmpty();
        result.Recommendations.Should().BeEmpty();
    }

    [Fact]
    public async Task AssessUserRiskAsync_DbContextCreationFailure_Propagates()
    {
        // The DbContext is created *before* the try/catch in AssessUserRiskAsync, so a
        // failure at that point is not swallowed by the method's own exception handling.
        var factory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));
        var sut = CreateSut(factory);

        var act = async () => await sut.AssessUserRiskAsync(1);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AssessUserRiskAsync_QuietVeteranUser_IsLowRisk()
    {
        var factory = CreateFactory(nameof(AssessUserRiskAsync_QuietVeteranUser_IsLowRisk));
        var user = MakeUser(1, twoFactor: true, createdAt: DateTime.UtcNow.AddDays(-400), lastPasswordChangeAt: DateTime.UtcNow.AddDays(-5));
        var logs = new[]
        {
            Log(1, "LOGIN_SUCCESS", DateTime.UtcNow.AddHours(-1)),
            Log(1, "LOGIN_SUCCESS", DateTime.UtcNow.AddHours(-2)),
            // PasswordChangeFrequency is derived from PASSWORD_CHANGED audit events (not
            // User.LastPasswordChangeAt); without these the "rare password changes" bonus
            // would fire for this 400-day-old account and this would no longer be a clean
            // zero-risk baseline.
            Log(1, "PASSWORD_CHANGED", DateTime.UtcNow.AddDays(-200)),
            Log(1, "PASSWORD_CHANGED", DateTime.UtcNow.AddDays(-100)),
        };
        await SeedAsync(factory, user, logs);
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskLevel.Should().Be(RiskLevel.Low);
        result.RiskScore.Should().Be(0);
        result.RequiresTwoFactor.Should().BeFalse();
        result.RequiresPasswordChange.Should().BeFalse();
        result.SuggestAccountReview.Should().BeFalse();
        result.Username.Should().Be("u1");
    }

    [Theory]
    [InlineData(2, 2, 30, "Sehr hohe Fehlerquote bei Logins")] // ratio 1.0 > 0.5
    [InlineData(5, 2, 20, "Hohe Fehlerquote bei Logins")]      // ratio 0.4, in (0.3, 0.5]
    [InlineData(4, 1, 15, "Erhöhte Fehlerquote bei Logins")]   // ratio 0.25, in (0.2, 0.3]
    public async Task AssessUserRiskAsync_FailedLoginRatio_AddsExpectedFactor(
        int successCount, int failedCount, double expectedImpact, string expectedFactor)
    {
        var factory = CreateFactory($"{nameof(AssessUserRiskAsync_FailedLoginRatio_AddsExpectedFactor)}_{successCount}_{failedCount}");
        var user = MakeUser(1);
        var logs = new List<AuditLog>();
        for (int i = 0; i < successCount; i++) logs.Add(Log(1, "LOGIN_SUCCESS", DateTime.UtcNow.AddHours(-i - 1)));
        for (int i = 0; i < failedCount; i++) logs.Add(Log(1, "LOGIN_FAILED", DateTime.UtcNow.AddHours(-i - 1)));
        await SeedAsync(factory, user, logs);
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskFactors.Should().ContainSingle(f => f.Factor == expectedFactor && f.Impact == expectedImpact);
    }

    [Fact]
    public async Task AssessUserRiskAsync_FailedLoginRatioBelowThreshold_AddsNoFactor()
    {
        var factory = CreateFactory(nameof(AssessUserRiskAsync_FailedLoginRatioBelowThreshold_AddsNoFactor));
        var user = MakeUser(1);
        var logs = new List<AuditLog>();
        for (int i = 0; i < 10; i++) logs.Add(Log(1, "LOGIN_SUCCESS", DateTime.UtcNow.AddHours(-i - 1)));
        logs.Add(Log(1, "LOGIN_FAILED", DateTime.UtcNow.AddHours(-1))); // ratio 0.1
        await SeedAsync(factory, user, logs);
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskFactors.Should().NotContain(f => f.Factor.Contains("Fehlerquote"));
    }

    [Fact]
    public async Task AssessUserRiskAsync_TwoFactorDisabled_AddsFactorAndRequiresTwoFactorWhenScoreHighEnough()
    {
        var factory = CreateFactory(nameof(AssessUserRiskAsync_TwoFactorDisabled_AddsFactorAndRequiresTwoFactorWhenScoreHighEnough));
        var user = MakeUser(1, twoFactor: false, createdAt: DateTime.UtcNow.AddDays(-3)); // new account: +10, no 2FA: +15 => 25 (< 40)
        await SeedAsync(factory, user);
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskFactors.Should().ContainSingle(f => f.Factor == "2FA nicht aktiviert" && f.Impact == 15);
        result.RequiresTwoFactor.Should().BeFalse("score is below the 40-point threshold for enforcement");
    }

    [Fact]
    public async Task AssessUserRiskAsync_TwoFactorDisabledWithHighScore_RequiresTwoFactor()
    {
        var factory = CreateFactory(nameof(AssessUserRiskAsync_TwoFactorDisabledWithHighScore_RequiresTwoFactor));
        var user = MakeUser(1, twoFactor: false, createdAt: DateTime.UtcNow.AddDays(-3));
        var logs = new List<AuditLog>();
        for (int i = 0; i < 25; i++) logs.Add(Log(1, "PRODUCT_DELETE", DateTime.UtcNow.AddHours(-i - 1))); // sensitive >20 => +20
        await SeedAsync(factory, user, logs);
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskScore.Should().BeGreaterThanOrEqualTo(40);
        result.RequiresTwoFactor.Should().BeTrue();
    }

    [Theory]
    [InlineData(21, 20, "Sehr viele sensible Aktionen")]
    [InlineData(11, 12, "Viele sensible Aktionen")]
    [InlineData(6, 6, "Sensible Aktionen")]
    public async Task AssessUserRiskAsync_SensitiveActionsCount_AddsExpectedFactor(int count, double expectedImpact, string expectedFactor)
    {
        var factory = CreateFactory($"{nameof(AssessUserRiskAsync_SensitiveActionsCount_AddsExpectedFactor)}_{count}");
        var user = MakeUser(1);
        var logs = new List<AuditLog>();
        for (int i = 0; i < count; i++) logs.Add(Log(1, "PRODUCT_DELETE", DateTime.UtcNow.AddHours(-i - 1)));
        await SeedAsync(factory, user, logs);
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskFactors.Should().ContainSingle(f => f.Factor == expectedFactor && f.Impact == expectedImpact);
    }

    [Theory]
    [InlineData(0.6, 15, "Häufige Aktivität zu ungewöhnlichen Zeiten")]
    [InlineData(0.4, 10, "Aktivität zu ungewöhnlichen Zeiten")]
    public async Task AssessUserRiskAsync_UnusualHourActivity_AddsExpectedFactor(double nightFraction, double expectedImpact, string expectedFactor)
    {
        var factory = CreateFactory($"{nameof(AssessUserRiskAsync_UnusualHourActivity_AddsExpectedFactor)}_{nightFraction}");
        var user = MakeUser(1);
        var logs = new List<AuditLog>();
        var total = 10;
        var nightCount = (int)Math.Round(total * nightFraction);
        for (int i = 0; i < nightCount; i++)
            logs.Add(Log(1, "VIEW", DateTime.UtcNow.Date.AddHours(2))); // hour 2 -> night
        for (int i = 0; i < total - nightCount; i++)
            logs.Add(Log(1, "VIEW", DateTime.UtcNow.Date.AddHours(14))); // hour 14 -> day
        await SeedAsync(factory, user, logs);
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskFactors.Should().ContainSingle(f => f.Factor == expectedFactor && f.Impact == expectedImpact);
    }

    [Theory]
    [InlineData(21, 10, "Sehr häufige IP-Wechsel")]
    [InlineData(11, 6, "Häufige IP-Wechsel")]
    public async Task AssessUserRiskAsync_IpAddressVariety_AddsExpectedFactor(int ipCount, double expectedImpact, string expectedFactor)
    {
        var factory = CreateFactory($"{nameof(AssessUserRiskAsync_IpAddressVariety_AddsExpectedFactor)}_{ipCount}");
        var user = MakeUser(1);
        var logs = new List<AuditLog>();
        for (int i = 0; i < ipCount; i++)
            logs.Add(new AuditLog { UserId = 1, Action = "VIEW", Timestamp = DateTime.UtcNow.AddHours(-i - 1), IpAddress = $"10.0.0.{i}" });
        await SeedAsync(factory, user, logs);
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskFactors.Should().ContainSingle(f => f.Factor == expectedFactor && f.Impact == expectedImpact);
    }

    [Theory]
    [InlineData(21, 15, "Sehr viele Daten-Exports")]
    [InlineData(11, 10, "Viele Daten-Exports")]
    [InlineData(6, 5, "Daten-Exports")]
    public async Task AssessUserRiskAsync_DataExportCount_AddsExpectedFactor(int count, double expectedImpact, string expectedFactor)
    {
        var factory = CreateFactory($"{nameof(AssessUserRiskAsync_DataExportCount_AddsExpectedFactor)}_{count}");
        var user = MakeUser(1);
        var logs = new List<AuditLog>();
        for (int i = 0; i < count; i++) logs.Add(Log(1, "DATA_EXPORT", DateTime.UtcNow.AddHours(-i - 1)));
        await SeedAsync(factory, user, logs);
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskFactors.Should().ContainSingle(f => f.Factor == expectedFactor && f.Impact == expectedImpact);
    }

    [Theory]
    [InlineData(3, 10, "Sehr neuer Account")]
    [InlineData(15, 5, "Neuer Account")]
    public async Task AssessUserRiskAsync_AccountAge_AddsExpectedFactor(int ageDays, double expectedImpact, string expectedFactor)
    {
        var factory = CreateFactory($"{nameof(AssessUserRiskAsync_AccountAge_AddsExpectedFactor)}_{ageDays}");
        var user = MakeUser(1, createdAt: DateTime.UtcNow.AddDays(-ageDays));
        await SeedAsync(factory, user);
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskFactors.Should().ContainSingle(f => f.Factor == expectedFactor && f.Impact == expectedImpact);
    }

    [Fact]
    public async Task AssessUserRiskAsync_RarePasswordChangesOnOldAccount_AddsFactor()
    {
        var factory = CreateFactory(nameof(AssessUserRiskAsync_RarePasswordChangesOnOldAccount_AddsFactor));
        // AccountAge > 90 and no PASSWORD_CHANGED logs -> frequency 0 < 0.1
        var user = MakeUser(1, createdAt: DateTime.UtcNow.AddDays(-365), lastPasswordChangeAt: DateTime.UtcNow.AddDays(-10));
        await SeedAsync(factory, user);
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskFactors.Should().ContainSingle(f => f.Factor == "Seltene Passwort-Änderungen" && f.Impact == 5);
    }

    [Fact]
    public async Task AssessUserRiskAsync_RequiresPasswordChange_WhenNeverChanged()
    {
        var factory = CreateFactory(nameof(AssessUserRiskAsync_RequiresPasswordChange_WhenNeverChanged));
        var user = MakeUser(1);
        user.LastPasswordChangeAt = null; // MakeUser's default coalesces null to "recent", so set directly.
        await SeedAsync(factory, user);
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(1);

        result.RequiresPasswordChange.Should().BeTrue();
    }

    [Fact]
    public async Task AssessUserRiskAsync_RequiresPasswordChange_WhenOlderThan90Days()
    {
        var factory = CreateFactory(nameof(AssessUserRiskAsync_RequiresPasswordChange_WhenOlderThan90Days));
        var user = MakeUser(1, lastPasswordChangeAt: DateTime.UtcNow.AddDays(-91));
        await SeedAsync(factory, user);
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(1);

        result.RequiresPasswordChange.Should().BeTrue();
    }

    [Fact]
    public async Task AssessUserRiskAsync_MaximalRiskFactors_IsCriticalAndScoreCappedAt100()
    {
        var factory = CreateFactory(nameof(AssessUserRiskAsync_MaximalRiskFactors_IsCriticalAndScoreCappedAt100));
        var user = MakeUser(1, twoFactor: false, createdAt: DateTime.UtcNow.AddDays(-2), lastPasswordChangeAt: null);
        var logs = new List<AuditLog>();
        for (int i = 0; i < 3; i++) logs.Add(Log(1, "LOGIN_SUCCESS", DateTime.UtcNow.AddHours(-i - 1)));
        for (int i = 0; i < 10; i++) logs.Add(Log(1, "LOGIN_FAILED", DateTime.UtcNow.AddHours(-i - 1))); // ratio > 0.5
        for (int i = 0; i < 25; i++) logs.Add(Log(1, "PRODUCT_DELETE", DateTime.UtcNow.Date.AddHours(2))); // sensitive + night
        for (int i = 0; i < 25; i++) logs.Add(Log(1, "DATA_EXPORT", DateTime.UtcNow.Date.AddHours(3)));
        for (int i = 0; i < 25; i++)
            logs.Add(new AuditLog { UserId = 1, Action = "VIEW", Timestamp = DateTime.UtcNow.Date.AddHours(4), IpAddress = $"10.0.0.{i}" });
        await SeedAsync(factory, user, logs);
        var sut = CreateSut(factory);

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskScore.Should().Be(100);
        result.RiskLevel.Should().Be(RiskLevel.Critical);
        result.SuggestAccountReview.Should().BeTrue();
        result.RequiresTwoFactor.Should().BeTrue();
        result.Recommendations.Should().Contain(r => r.Contains("sofort", StringComparison.OrdinalIgnoreCase) || r.Contains("SOFORT"));
    }

    [Fact]
    public async Task AssessUserRiskAsync_RateLimitDDoSDetected_AddsFactor()
    {
        var factory = CreateFactory(nameof(AssessUserRiskAsync_RateLimitDDoSDetected_AddsFactor));
        var user = MakeUser(1);
        await SeedAsync(factory, user);
        var rl = BaselineRateLimitService();
        rl.DetectDDoS(Arg.Any<TimeSpan>()).Returns(new DDoSDetection { IsDDoSPattern = true, UniqueIPsInvolved = 50, TotalRequests = 5000, AverageRequestsPerIP = 100 });
        var sut = CreateSut(factory, RateLimitServiceProvider(rl));

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskFactors.Should().ContainSingle(f => f.Factor == "DDoS Angriff erkannt" && f.Impact == 20);
    }

    [Fact]
    public async Task AssessUserRiskAsync_RateLimitBurstAttackDetected_AddsFactor()
    {
        var factory = CreateFactory(nameof(AssessUserRiskAsync_RateLimitBurstAttackDetected_AddsFactor));
        var user = MakeUser(1);
        await SeedAsync(factory, user);
        var rl = BaselineRateLimitService();
        rl.DetectBurstAttack("ip:user_1").Returns(new BurstAttackDetection
        {
            IsBurstAttack = true,
            RequestsInBurst = 100,
            BurstDuration = TimeSpan.FromSeconds(2),
            RequestsPerSecond = 50
        });
        var sut = CreateSut(factory, RateLimitServiceProvider(rl));

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskFactors.Should().ContainSingle(f => f.Factor == "Burst Attack erkannt" && f.Impact == 15);
    }

    [Fact]
    public async Task AssessUserRiskAsync_RateLimitBruteForceDetected_AddsFactor()
    {
        var factory = CreateFactory(nameof(AssessUserRiskAsync_RateLimitBruteForceDetected_AddsFactor));
        var user = MakeUser(1);
        await SeedAsync(factory, user);
        var rl = BaselineRateLimitService();
        rl.DetectBruteForce("ip:user_1").Returns(new BruteForceDetection
        {
            IsBruteForce = true,
            FailedAttempts = 30,
            TargetedEndpoints = new List<string> { "/login", "/api/x" }
        });
        var sut = CreateSut(factory, RateLimitServiceProvider(rl));

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskFactors.Should().ContainSingle(f => f.Factor == "Brute-Force Angriff erkannt" && f.Impact == 15);
    }

    [Theory]
    [InlineData(150, 10)]  // BlockRate/10 = 15, capped at 10
    [InlineData(50, 5)]    // BlockRate/10 = 5
    public async Task AssessUserRiskAsync_HighBlockRate_AddsCappedFactor(double blockRate, double expectedImpact)
    {
        var factory = CreateFactory($"{nameof(AssessUserRiskAsync_HighBlockRate_AddsCappedFactor)}_{blockRate}");
        var user = MakeUser(1);
        await SeedAsync(factory, user);
        var rl = BaselineRateLimitService();
        rl.GetGlobalStatistics().Returns(new RateLimitStatistics { BlockedRequests = 200, TotalRequests = 1000, BlockRate = blockRate, ActiveBuckets = 0 });
        var sut = CreateSut(factory, RateLimitServiceProvider(rl));

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskFactors.Should().ContainSingle(f => f.Factor == "Hohe Block-Rate" && f.Impact == expectedImpact);
    }

    [Fact]
    public async Task AssessUserRiskAsync_RateLimitServiceThrows_IsSwallowedAndScoringContinues()
    {
        var factory = CreateFactory(nameof(AssessUserRiskAsync_RateLimitServiceThrows_IsSwallowedAndScoringContinues));
        var user = MakeUser(1, twoFactor: false);
        await SeedAsync(factory, user);
        var rl = Substitute.For<IRateLimitService>();
        rl.DetectDDoS(Arg.Any<TimeSpan>()).Throws(new InvalidOperationException("rate limiter down"));
        var sut = CreateSut(factory, RateLimitServiceProvider(rl));

        var result = await sut.AssessUserRiskAsync(1);

        result.RiskFactors.Should().ContainSingle(f => f.Factor == "2FA nicht aktiviert");
        result.RiskFactors.Should().NotContain(f => f.Factor.Contains("DDoS"));
    }

    // ---------------------------------------------------------------
    // GetHighRiskUsersAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetHighRiskUsersAsync_ReturnsOnlyActiveNonDeletedHighRiskUsers_OrderedDescending()
    {
        var factory = CreateFactory(nameof(GetHighRiskUsersAsync_ReturnsOnlyActiveNonDeletedHighRiskUsers_OrderedDescending));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());

            // High risk: no 2FA, new account, many sensitive actions => score well above 50 (High)
            var highRiskUser1 = MakeUser(1, twoFactor: false, createdAt: DateTime.UtcNow.AddDays(-2));
            var highRiskUser2 = MakeUser(2, twoFactor: false, createdAt: DateTime.UtcNow.AddDays(-2));
            // Low risk: quiet veteran
            var lowRiskUser = MakeUser(3);
            // Inactive: would be high risk but excluded by the initial query
            var inactiveUser = MakeUser(4, twoFactor: false, createdAt: DateTime.UtcNow.AddDays(-2));
            inactiveUser.IsActive = false;
            // Deleted: would be high risk but excluded
            var deletedUser = MakeUser(5, twoFactor: false, createdAt: DateTime.UtcNow.AddDays(-2));
            deletedUser.IsDeleted = true;

            db.Users.AddRange(highRiskUser1, highRiskUser2, lowRiskUser, inactiveUser, deletedUser);

            var logs = new List<AuditLog>();
            // Sensitive actions (+20) + no 2FA (+15) + new account (+10) + failed-login
            // ratio > 0.5 (+30) comfortably clears the 50-point "High" threshold for both.
            for (int i = 0; i < 25; i++) logs.Add(Log(1, "PRODUCT_DELETE", DateTime.UtcNow.AddHours(-i - 1)));
            logs.Add(Log(1, "LOGIN_SUCCESS", DateTime.UtcNow.AddHours(-1)));
            logs.Add(Log(1, "LOGIN_FAILED", DateTime.UtcNow.AddHours(-1)));
            logs.Add(Log(1, "LOGIN_FAILED", DateTime.UtcNow.AddHours(-1)));

            for (int i = 0; i < 12; i++) logs.Add(Log(2, "PRODUCT_DELETE", DateTime.UtcNow.AddHours(-i - 1)));
            logs.Add(Log(2, "LOGIN_SUCCESS", DateTime.UtcNow.AddHours(-1)));
            logs.Add(Log(2, "LOGIN_FAILED", DateTime.UtcNow.AddHours(-1)));
            logs.Add(Log(2, "LOGIN_FAILED", DateTime.UtcNow.AddHours(-1)));
            db.AuditLogs.AddRange(logs);

            await db.SaveChangesAsync();
        }

        var sut = CreateSut(factory);

        var result = await sut.GetHighRiskUsersAsync();

        result.Should().OnlyContain(a => a.UserId == 1 || a.UserId == 2);
        result.Select(a => a.UserId).Should().BeInDescendingOrder();
        result.Should().OnlyContain(a => a.RiskLevel >= RiskLevel.High);
    }

    [Fact]
    public async Task GetHighRiskUsersAsync_NoActiveUsers_ReturnsEmpty()
    {
        var factory = CreateFactory(nameof(GetHighRiskUsersAsync_NoActiveUsers_ReturnsEmpty));
        var sut = CreateSut(factory);

        var result = await sut.GetHighRiskUsersAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHighRiskUsersAsync_DbContextCreationFailure_Propagates()
    {
        // Same as AssessUserRiskAsync: context creation happens before the try/catch.
        var factory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));
        var sut = CreateSut(factory);

        var act = async () => await sut.GetHighRiskUsersAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---------------------------------------------------------------
    // UpdateAllRiskScoresAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task UpdateAllRiskScoresAsync_ProcessesAllActiveUsersWithoutThrowing()
    {
        var factory = CreateFactory(nameof(UpdateAllRiskScoresAsync_ProcessesAllActiveUsersWithoutThrowing));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            // 12 users so the "processed % 10 == 0" progress-log branch is exercised.
            for (int i = 1; i <= 12; i++) db.Users.Add(MakeUser(i));
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var act = async () => await sut.UpdateAllRiskScoresAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateAllRiskScoresAsync_DbFailure_Rethrows()
    {
        var factory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));
        var sut = CreateSut(factory);

        var act = async () => await sut.UpdateAllRiskScoresAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---------------------------------------------------------------
    // TrainModelAsync / IsModelReady
    // ---------------------------------------------------------------

    [Fact]
    public void IsModelReady_AlwaysTrue()
    {
        var factory = CreateFactory(nameof(IsModelReady_AlwaysTrue));
        var sut = CreateSut(factory);

        sut.IsModelReady.Should().BeTrue();
    }

    [Fact]
    public async Task TrainModelAsync_ReturnsTrueWithoutTraining()
    {
        var factory = CreateFactory(nameof(TrainModelAsync_ReturnsTrueWithoutTraining));
        var sut = CreateSut(factory);

        var result = await sut.TrainModelAsync();

        result.Should().BeTrue();
    }

    // ---------------------------------------------------------------
    // CalculateGlobalSystemRiskAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task CalculateGlobalSystemRiskAsync_NoRateLimitService_ReturnsTen()
    {
        var factory = CreateFactory(nameof(CalculateGlobalSystemRiskAsync_NoRateLimitService_ReturnsTen));
        var sut = CreateSut(factory);

        var score = await sut.CalculateGlobalSystemRiskAsync();

        score.Should().Be(10);
    }

    [Fact]
    public async Task CalculateGlobalSystemRiskAsync_DDoSPattern_AddsThirtyFive()
    {
        var factory = CreateFactory(nameof(CalculateGlobalSystemRiskAsync_DDoSPattern_AddsThirtyFive));
        var rl = BaselineRateLimitService();
        rl.DetectDDoS(Arg.Any<TimeSpan>()).Returns(new DDoSDetection { IsDDoSPattern = true, UniqueIPsInvolved = 40, TotalRequests = 3000 });
        var sut = CreateSut(factory, RateLimitServiceProvider(rl));

        var score = await sut.CalculateGlobalSystemRiskAsync();

        score.Should().Be(35);
    }

    [Fact]
    public async Task CalculateGlobalSystemRiskAsync_ElevatedRequestRate_AddsFifteen()
    {
        var factory = CreateFactory(nameof(CalculateGlobalSystemRiskAsync_ElevatedRequestRate_AddsFifteen));
        var rl = BaselineRateLimitService();
        rl.DetectDDoS(Arg.Any<TimeSpan>()).Returns(new DDoSDetection { IsDDoSPattern = false, TotalRequests = 600 });
        var sut = CreateSut(factory, RateLimitServiceProvider(rl));

        var score = await sut.CalculateGlobalSystemRiskAsync();

        score.Should().Be(15);
    }

    [Theory]
    [InlineData(60, 25)]
    [InlineData(35, 15)]
    [InlineData(15, 8)]
    public async Task CalculateGlobalSystemRiskAsync_BlockRateBuckets_AddExpectedScore(double blockRate, double expected)
    {
        var factory = CreateFactory($"{nameof(CalculateGlobalSystemRiskAsync_BlockRateBuckets_AddExpectedScore)}_{blockRate}");
        var rl = BaselineRateLimitService();
        rl.GetGlobalStatistics().Returns(new RateLimitStatistics { BlockRate = blockRate });
        var sut = CreateSut(factory, RateLimitServiceProvider(rl));

        var score = await sut.CalculateGlobalSystemRiskAsync();

        score.Should().Be(expected);
    }

    [Theory]
    [InlineData(11, 20)]
    [InlineData(6, 12)]
    [InlineData(1, 6)]
    public async Task CalculateGlobalSystemRiskAsync_BurstAttackIPs_AddExpectedScore(int suspiciousGroups, double expected)
    {
        var factory = CreateFactory($"{nameof(CalculateGlobalSystemRiskAsync_BurstAttackIPs_AddExpectedScore)}_{suspiciousGroups}");
        var rl = BaselineRateLimitService();
        var requests = new List<RequestLog>();
        for (int g = 0; g < suspiciousGroups; g++)
            for (int i = 0; i < 21; i++)
                requests.Add(new RequestLog { Identifier = $"ip{g}", Endpoint = "/x", IsSuccess = true, Timestamp = DateTime.UtcNow });
        rl.GetRecentRequests(Arg.Any<int>()).Returns(requests);
        var sut = CreateSut(factory, RateLimitServiceProvider(rl));

        var score = await sut.CalculateGlobalSystemRiskAsync();

        score.Should().Be(expected);
    }

    [Theory]
    [InlineData(51, 15)]
    [InlineData(21, 10)]
    public async Task CalculateGlobalSystemRiskAsync_FailedLogins_AddExpectedScore(int failedCount, double expected)
    {
        var factory = CreateFactory($"{nameof(CalculateGlobalSystemRiskAsync_FailedLogins_AddExpectedScore)}_{failedCount}");
        var rl = BaselineRateLimitService();
        var requests = new List<RequestLog>();
        for (int i = 0; i < failedCount; i++)
            requests.Add(new RequestLog { Identifier = $"ip{i}", Endpoint = "/login", IsSuccess = false, Timestamp = DateTime.UtcNow });
        rl.GetRecentRequests(Arg.Any<int>()).Returns(requests);
        var sut = CreateSut(factory, RateLimitServiceProvider(rl));

        var score = await sut.CalculateGlobalSystemRiskAsync();

        score.Should().Be(expected);
    }

    [Theory]
    [InlineData(1001, 5)]
    [InlineData(501, 3)]
    public async Task CalculateGlobalSystemRiskAsync_ActiveBuckets_AddExpectedScore(int activeBuckets, double expected)
    {
        var factory = CreateFactory($"{nameof(CalculateGlobalSystemRiskAsync_ActiveBuckets_AddExpectedScore)}_{activeBuckets}");
        var rl = BaselineRateLimitService();
        rl.GetGlobalStatistics().Returns(new RateLimitStatistics { ActiveBuckets = activeBuckets });
        var sut = CreateSut(factory, RateLimitServiceProvider(rl));

        var score = await sut.CalculateGlobalSystemRiskAsync();

        score.Should().Be(expected);
    }

    [Fact]
    public async Task CalculateGlobalSystemRiskAsync_EverythingMaxed_IsCappedAtOneHundred()
    {
        var factory = CreateFactory(nameof(CalculateGlobalSystemRiskAsync_EverythingMaxed_IsCappedAtOneHundred));
        var rl = BaselineRateLimitService();
        rl.DetectDDoS(Arg.Any<TimeSpan>()).Returns(new DDoSDetection { IsDDoSPattern = true, TotalRequests = 9999, UniqueIPsInvolved = 500 });
        rl.GetGlobalStatistics().Returns(new RateLimitStatistics { BlockRate = 90, ActiveBuckets = 2000, BlockedRequests = 500, TotalRequests = 1000 });
        var requests = new List<RequestLog>();
        for (int g = 0; g < 12; g++)
            for (int i = 0; i < 25; i++)
                requests.Add(new RequestLog { Identifier = $"ip{g}", Endpoint = "/login", IsSuccess = false, Timestamp = DateTime.UtcNow });
        rl.GetRecentRequests(Arg.Any<int>()).Returns(requests);
        var sut = CreateSut(factory, RateLimitServiceProvider(rl));

        var score = await sut.CalculateGlobalSystemRiskAsync();

        score.Should().Be(100);
    }

    [Fact]
    public async Task CalculateGlobalSystemRiskAsync_RateLimitServiceThrows_ReturnsTwenty()
    {
        var factory = CreateFactory(nameof(CalculateGlobalSystemRiskAsync_RateLimitServiceThrows_ReturnsTwenty));
        var rl = Substitute.For<IRateLimitService>();
        rl.DetectDDoS(Arg.Any<TimeSpan>()).Throws(new InvalidOperationException("boom"));
        var sut = CreateSut(factory, RateLimitServiceProvider(rl));

        var score = await sut.CalculateGlobalSystemRiskAsync();

        score.Should().Be(20);
    }
}
