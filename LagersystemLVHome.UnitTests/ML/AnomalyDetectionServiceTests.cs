using LagersystemLVHome.Data;
using LagersystemLVHome.Infrastructure.ML.Models;
using LagersystemLVHome.Infrastructure.ML.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace LagersystemLVHome.UnitTests.ML;

/// <summary>
/// <see cref="AnomalyDetectionService"/> combines an ML.NET RandomizedPca anomaly-detection
/// model with a rule-based fallback score, taking the maximum of the two
/// (<c>Math.Max(mlScore, ruleBasedScore)</c>). Because the ML score cannot be pinned to an
/// exact value without over-fitting the test to today's ML.NET internals, most rule-based
/// threshold tests here push the *rule-based* component to an extreme so the final score
/// (and therefore the risk-level bucket) is deterministic regardless of what the model adds.
/// Each test gets an isolated temp "ContentRootPath" so the on-disk model file
/// (<c>ML/Data/anomaly-detection-model.zip</c>) never leaks between tests.
/// </summary>
public class AnomalyDetectionServiceTests : IDisposable
{
    private readonly List<string> _tempRoots = new();

    public void Dispose()
    {
        foreach (var root in _tempRoots)
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

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
                .UseInMemoryDatabase(nameof(AnomalyDetectionServiceTests) + "." + name).Options);

    private string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "lg-anomaly-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        return root;
    }

    private IWebHostEnvironment EnvFor(string contentRoot)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(contentRoot);
        return env;
    }

    private AnomalyDetectionService CreateSut(IDbContextFactory<InventoryDbContext> factory, string? contentRoot = null)
        => new(factory, NullLogger<AnomalyDetectionService>.Instance, EnvFor(contentRoot ?? NewTempRoot()));

    private static Warehouse MakeWarehouse(int id = 1) => new() { Id = id, Name = "WH" + id, Address = "a" };

    private static User MakeUser(int id) => new()
    {
        Id = id,
        Username = "u" + id,
        Email = $"u{id}@x.local",
        DisplayName = "User " + id,
        PasswordHash = "x",
        WarehouseId = 1
    };

    private static AuditLog Log(int userId, string action, DateTime timestamp, string ip = "10.0.0.1")
        => new() { UserId = userId, Action = action, Timestamp = timestamp, Entity = "x" };

    /// <summary>Trains a model on a background population of "normal" users so
    /// <see cref="AnomalyDetectionService.IsModelReady"/> becomes true. 10 users x 10 benign,
    /// spread-out, daytime logins each satisfies both the &gt;=100-total-logs and
    /// &gt;=10-qualifying-users thresholds in TrainModelAsync.</summary>
    private static async Task SeedTrainableBaselineAsync(IDbContextFactory<InventoryDbContext> factory, int userCount = 10, int logsPerUser = 10)
    {
        await using var db = factory.CreateDbContext();
        db.Warehouses.Add(MakeWarehouse());
        for (int u = 1; u <= userCount; u++)
        {
            db.Users.Add(MakeUser(u));
            for (int i = 0; i < logsPerUser; i++)
            {
                db.AuditLogs.Add(Log(u, "VIEW", DateTime.UtcNow.AddDays(-1).Date.AddHours(9 + (i % 8)).AddMinutes(i), ip: "10.0.0.1"));
            }
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds a trainable baseline and returns an untrained SUT pointed at it;
    /// callers still need to call <c>TrainModelAsync()</c> themselves.</summary>
    private async Task<AnomalyDetectionService> CreateTrainedSutAsync(
        IDbContextFactory<InventoryDbContext> factory, string? contentRoot = null, int userCount = 10, int logsPerUser = 10)
    {
        await SeedTrainableBaselineAsync(factory, userCount, logsPerUser);
        return CreateSut(factory, contentRoot);
    }

    // ---------------------------------------------------------------
    // IsModelReady / TrainModelAsync
    // ---------------------------------------------------------------

    [Fact]
    public void IsModelReady_FalseBeforeTraining()
    {
        var sut = CreateSut(CreateFactory(nameof(IsModelReady_FalseBeforeTraining)));

        sut.IsModelReady.Should().BeFalse();
        sut.LastTrainingDate.Should().BeNull();
    }

    [Fact]
    public async Task TrainModelAsync_FewerThanOneHundredLogs_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(TrainModelAsync_FewerThanOneHundredLogs_ReturnsFalse));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            for (int i = 0; i < 50; i++) db.AuditLogs.Add(Log(1, "VIEW", DateTime.UtcNow.AddDays(-1).AddMinutes(i)));
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var result = await sut.TrainModelAsync();

        result.Should().BeFalse();
        sut.IsModelReady.Should().BeFalse();
    }

    [Fact]
    public async Task TrainModelAsync_EnoughLogsButFewerThanTenQualifyingUsers_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(TrainModelAsync_EnoughLogsButFewerThanTenQualifyingUsers_ReturnsFalse));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            // A single user contributing >=100 logs: >=100 total logs, but only 1 "qualifying" user group.
            db.Users.Add(MakeUser(1));
            for (int i = 0; i < 120; i++) db.AuditLogs.Add(Log(1, "VIEW", DateTime.UtcNow.AddDays(-1).AddMinutes(i)));
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var result = await sut.TrainModelAsync();

        result.Should().BeFalse();
        sut.IsModelReady.Should().BeFalse();
    }

    [Fact]
    public async Task TrainModelAsync_EnoughUsersAndLogs_TrainsAndPersistsModel()
    {
        var contentRoot = NewTempRoot();
        var factory = CreateFactory(nameof(TrainModelAsync_EnoughUsersAndLogs_TrainsAndPersistsModel));
        await SeedTrainableBaselineAsync(factory);
        var sut = CreateSut(factory, contentRoot);

        var result = await sut.TrainModelAsync();

        result.Should().BeTrue();
        sut.IsModelReady.Should().BeTrue();
        sut.LastTrainingDate.Should().NotBeNull();
        sut.LastTrainingDate!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        File.Exists(Path.Combine(contentRoot, "ML", "Data", "anomaly-detection-model.zip")).Should().BeTrue();
    }

    [Fact]
    public async Task TrainModelAsync_DbFailure_ReturnsFalse()
    {
        var factory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));
        var sut = CreateSut(factory);

        var result = await sut.TrainModelAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Constructor_ReloadsPreviouslyTrainedModelFromDisk()
    {
        var contentRoot = NewTempRoot();
        var factory = CreateFactory(nameof(Constructor_ReloadsPreviouslyTrainedModelFromDisk));
        await SeedTrainableBaselineAsync(factory);
        var trainer = CreateSut(factory, contentRoot);
        (await trainer.TrainModelAsync()).Should().BeTrue();

        var reloaded = CreateSut(factory, contentRoot);

        reloaded.IsModelReady.Should().BeTrue();
        reloaded.LastTrainingDate.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_CorruptModelFile_IsSwallowedAndModelStaysNotReady()
    {
        var contentRoot = NewTempRoot();
        var modelDir = Path.Combine(contentRoot, "ML", "Data");
        Directory.CreateDirectory(modelDir);
        File.WriteAllBytes(Path.Combine(modelDir, "anomaly-detection-model.zip"), new byte[] { 9, 9, 9, 9 });
        var factory = CreateFactory(nameof(Constructor_CorruptModelFile_IsSwallowedAndModelStaysNotReady));
        AnomalyDetectionService? sut = null;

        var act = () => sut = CreateSut(factory, contentRoot);

        act.Should().NotThrow();
        sut!.IsModelReady.Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // AnalyzeUserBehaviorAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task AnalyzeUserBehaviorAsync_ModelNotReady_ReturnsPlaceholderResult()
    {
        var factory = CreateFactory(nameof(AnalyzeUserBehaviorAsync_ModelNotReady_ReturnsPlaceholderResult));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var result = await sut.AnalyzeUserBehaviorAsync(1);

        result.AnomalyScore.Should().Be(0);
        result.DetectedPatterns.Should().Contain("Modell noch nicht trainiert");
        result.RecommendedAction.Should().Be("Bitte ML-Modell trainieren");
    }

    [Fact]
    public async Task AnalyzeUserBehaviorAsync_UserNotFound_Throws()
    {
        var factory = CreateFactory(nameof(AnalyzeUserBehaviorAsync_UserNotFound_Throws));
        var sut = await CreateTrainedSutAsync(factory);
        (await sut.TrainModelAsync()).Should().BeTrue();

        var act = async () => await sut.AnalyzeUserBehaviorAsync(999);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AnalyzeUserBehaviorAsync_NoLogsInPeriod_ReturnsNoActivityResult()
    {
        var factory = CreateFactory(nameof(AnalyzeUserBehaviorAsync_NoLogsInPeriod_ReturnsNoActivityResult));
        var sut = await CreateTrainedSutAsync(factory);
        (await sut.TrainModelAsync()).Should().BeTrue();
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(500));
            await db.SaveChangesAsync();
        }

        var result = await sut.AnalyzeUserBehaviorAsync(500, from: DateTime.UtcNow.AddDays(-1));

        result.AnomalyScore.Should().Be(0);
        result.DetectedPatterns.Should().Contain("Keine Aktivitäten im Zeitraum");
        result.RecommendedAction.Should().Be("Keine Analyse möglich - keine Daten");
    }

    [Fact]
    public async Task AnalyzeUserBehaviorAsync_ExtremeSuspiciousActivity_IsCriticalRisk()
    {
        var factory = CreateFactory(nameof(AnalyzeUserBehaviorAsync_ExtremeSuspiciousActivity_IsCriticalRisk));
        var sut = await CreateTrainedSutAsync(factory);
        (await sut.TrainModelAsync()).Should().BeTrue();
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(500));
            var logs = new List<AuditLog>();
            // All-night activity (30 pts), 10+ failed logins (25 pts capped),
            // 10+ sensitive actions (25 pts capped), >5 unique IPs (15 pts capped),
            // and >50 total logs (5 pts) => rule-based score maxes out at 100,
            // guaranteeing Critical regardless of what the ML component contributes.
            var baseDate = DateTime.UtcNow.AddDays(-1).Date;
            for (int i = 0; i < 15; i++) logs.Add(Log(500, "LOGIN_FAILED", baseDate.AddHours(2).AddMinutes(i)));
            for (int i = 0; i < 15; i++) logs.Add(Log(500, "PRODUCT_DELETE", baseDate.AddHours(2).AddMinutes(20 + i)));
            for (int i = 0; i < 12; i++)
                logs.Add(new AuditLog { UserId = 500, Action = "VIEW", Timestamp = baseDate.AddHours(3).AddMinutes(i), IpAddress = $"192.168.0.{i}" });
            for (int i = 0; i < 30; i++) logs.Add(Log(500, "VIEW", baseDate.AddHours(4).AddMinutes(i)));
            db.AuditLogs.AddRange(logs);
            await db.SaveChangesAsync();
        }

        var result = await sut.AnalyzeUserBehaviorAsync(500, from: DateTime.UtcNow.AddDays(-2));

        result.AnomalyScore.Should().Be(100);
        result.RiskLevel.Should().Be(AnomalyRiskLevel.Critical);
        result.IsHighRisk.Should().BeTrue();
        result.RecommendedAction.Should().Be("Account sofort überprüfen und ggf. sperren");
        result.DetectedPatterns.Should().Contain(p => p.Contains("Nachtaktivität"));
        result.DetectedPatterns.Should().Contain(p => p.Contains("fehlgeschlagene Login-Versuche"));
        result.DetectedPatterns.Should().Contain(p => p.Contains("IP-Wechsel"));
        result.DetectedPatterns.Should().Contain(p => p.Contains("Sensible Aktionen"));
    }

    [Fact]
    public async Task AnalyzeUserBehaviorAsync_MassActionsInSameMinute_AddsMassActionPattern()
    {
        var factory = CreateFactory(nameof(AnalyzeUserBehaviorAsync_MassActionsInSameMinute_AddsMassActionPattern));
        var sut = await CreateTrainedSutAsync(factory);
        (await sut.TrainModelAsync()).Should().BeTrue();
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(501));
            var sameMinute = DateTime.UtcNow.AddDays(-1).Date.AddHours(14).AddMinutes(30);
            var logs = new List<AuditLog>();
            for (int i = 0; i < 11; i++) logs.Add(Log(501, "VIEW", sameMinute.AddSeconds(i)));
            db.AuditLogs.AddRange(logs);
            await db.SaveChangesAsync();
        }

        var result = await sut.AnalyzeUserBehaviorAsync(501, from: DateTime.UtcNow.AddDays(-2));

        result.DetectedPatterns.Should().Contain(p => p.Contains("Massenaktionen erkannt"));
    }

    [Fact]
    public async Task AnalyzeUserBehaviorAsync_BenignActivity_HasNoSuspiciousPatterns()
    {
        var factory = CreateFactory(nameof(AnalyzeUserBehaviorAsync_BenignActivity_HasNoSuspiciousPatterns));
        var sut = await CreateTrainedSutAsync(factory);
        (await sut.TrainModelAsync()).Should().BeTrue();
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(502));
            var logs = new List<AuditLog>
            {
                Log(502, "LOGIN_SUCCESS", DateTime.UtcNow.AddDays(-1).Date.AddHours(10)),
                Log(502, "VIEW", DateTime.UtcNow.AddDays(-1).Date.AddHours(10).AddMinutes(5)),
            };
            db.AuditLogs.AddRange(logs);
            await db.SaveChangesAsync();
        }

        var result = await sut.AnalyzeUserBehaviorAsync(502, from: DateTime.UtcNow.AddDays(-2));

        result.DetectedPatterns.Should().Contain("Keine auffälligen Muster erkannt");
    }

    /// <summary>Trains a model against a working factory/content-root, then constructs a
    /// second SUT pointed at a DB-context factory that always throws, reusing the same
    /// content root so the constructor's LoadModelIfExists picks up the already-trained
    /// model file (making IsModelReady true without needing the failing factory to work).</summary>
    private async Task<AnomalyDetectionService> CreateReadyButDbFailingSutAsync(string testName)
    {
        var trainingFactory = CreateFactory(testName + "_train");
        var contentRoot = NewTempRoot();
        var trainer = await CreateTrainedSutAsync(trainingFactory, contentRoot);
        (await trainer.TrainModelAsync()).Should().BeTrue();

        var failingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        failingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));

        var readyButFailingSut = CreateSut(failingFactory, contentRoot);
        readyButFailingSut.IsModelReady.Should().BeTrue();
        return readyButFailingSut;
    }

    [Fact]
    public async Task AnalyzeUserBehaviorAsync_DbFailure_Throws()
    {
        var sut = await CreateReadyButDbFailingSutAsync(nameof(AnalyzeUserBehaviorAsync_DbFailure_Throws));

        var act = async () => await sut.AnalyzeUserBehaviorAsync(1);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---------------------------------------------------------------
    // DetectAnomaliesAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task DetectAnomaliesAsync_ModelNotReady_ReturnsEmpty()
    {
        var factory = CreateFactory(nameof(DetectAnomaliesAsync_ModelNotReady_ReturnsEmpty));
        var sut = CreateSut(factory);

        var result = await sut.DetectAnomaliesAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectAnomaliesAsync_ReturnsOnlyHighRiskUsers_OrderedDescending()
    {
        var factory = CreateFactory(nameof(DetectAnomaliesAsync_ReturnsOnlyHighRiskUsers_OrderedDescending));
        var sut = await CreateTrainedSutAsync(factory);
        (await sut.TrainModelAsync()).Should().BeTrue();

        var from = DateTime.UtcNow.AddDays(-2);
        var to = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(600));
            db.Users.Add(MakeUser(601));
            var baseDate = DateTime.UtcNow.AddDays(-1).Date;
            var logs = new List<AuditLog>();
            // User 600: extreme, maxed-out rule-based score -> guaranteed high risk (>=60).
            for (int i = 0; i < 15; i++) logs.Add(Log(600, "LOGIN_FAILED", baseDate.AddHours(2).AddMinutes(i)));
            for (int i = 0; i < 15; i++) logs.Add(Log(600, "PRODUCT_DELETE", baseDate.AddHours(2).AddMinutes(20 + i)));
            for (int i = 0; i < 12; i++)
                logs.Add(new AuditLog { UserId = 600, Action = "VIEW", Timestamp = baseDate.AddHours(3).AddMinutes(i), IpAddress = $"172.16.0.{i}" });
            // User 601: single benign login.
            logs.Add(Log(601, "LOGIN_SUCCESS", baseDate.AddHours(10)));
            db.AuditLogs.AddRange(logs);
            await db.SaveChangesAsync();
        }

        var result = await sut.DetectAnomaliesAsync(from, to);

        result.Should().OnlyContain(r => r.IsHighRisk);
        result.Should().Contain(r => r.UserId == 600);
        result.Select(r => r.AnomalyScore).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task DetectAnomaliesAsync_DbFailure_Throws()
    {
        var sut = await CreateReadyButDbFailingSutAsync(nameof(DetectAnomaliesAsync_DbFailure_Throws));

        var act = async () => await sut.DetectAnomaliesAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
