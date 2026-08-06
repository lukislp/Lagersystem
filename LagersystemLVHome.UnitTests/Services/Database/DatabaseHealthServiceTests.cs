using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace LagersystemLVHome.UnitTests.Services.Database;

public class DatabaseHealthServiceTests : IDisposable
{
    // ---------- Context factories ----------
    // InMemory is used wherever we need plain LINQ/count semantics (or need a provider that
    // deliberately rejects relational-only operations like ExecuteSqlRaw, to trigger error paths).
    // Sqlite (kept-open shared connection) is used wherever the service needs a real relational
    // connection (raw ADO.NET commands, sqlite_master, version queries).

    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateInMemoryFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private sealed class SqliteContextFactory : IDbContextFactory<InventoryDbContext>, IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<InventoryDbContext> _options;

        public SqliteContextFactory()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(_connection).Options;
            using var ctx = new InventoryDbContext(_options);
            ctx.Database.EnsureCreated();
        }

        public InventoryDbContext CreateDbContext() => new(_options);

        public void Dispose() => _connection.Dispose();
    }

    private readonly List<SqliteContextFactory> _sqliteFactories = new();

    private SqliteContextFactory CreateSqliteFactory()
    {
        var factory = new SqliteContextFactory();
        _sqliteFactories.Add(factory);
        return factory;
    }

    public void Dispose()
    {
        foreach (var factory in _sqliteFactories)
        {
            factory.Dispose();
        }
    }

    private static DatabaseHealthService BuildSut(IDbContextFactory<InventoryDbContext> factory, DatabaseProvider provider = DatabaseProvider.SQLite)
        => new(factory, new DatabaseSettings { Provider = provider }, NullLogger<DatabaseHealthService>.Instance);

    private static (DatabaseHealthService sut, DatabaseSettings settings) BuildSutWithSettings(
        IDbContextFactory<InventoryDbContext> factory, DatabaseProvider provider = DatabaseProvider.SQLite)
    {
        var settings = new DatabaseSettings { Provider = provider };
        return (new DatabaseHealthService(factory, settings, NullLogger<DatabaseHealthService>.Instance), settings);
    }

    // ---------- Reflection helpers for private members not reachable (or not reachable
    // deterministically/cheaply) through the public IDatabaseHealthService surface alone.
    // Same pattern already used in this codebase for AuthHelpers (internal, reflection-only access). ----------

    private static async Task<T> InvokeAsync<T>(DatabaseHealthService sut, string methodName, params object[] args)
    {
        var method = typeof(DatabaseHealthService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<T>)method.Invoke(sut, args)!;
        return await task;
    }

    private static int InvokeCalculateHealthScore(DatabaseHealthService sut, DatabaseHealthReport report, List<TableStatistics> stats)
    {
        var method = typeof(DatabaseHealthService).GetMethod("CalculateHealthScore", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (int)method.Invoke(sut, new object[] { report, stats })!;
    }

    private static void InvokeGenerateRecommendations(DatabaseHealthService sut, DatabaseHealthReport report, List<TableStatistics> stats)
    {
        var method = typeof(DatabaseHealthService).GetMethod("GenerateRecommendations", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(sut, new object[] { report, stats });
    }

    private static DatabaseHealthReport HealthyReport() => new()
    {
        IsConnected = true,
        ConnectionLatency = TimeSpan.Zero,
        DatabaseSizeBytes = 0
    };

    // ==================== TestConnectionAsync ====================

    [Fact]
    public async Task TestConnectionAsync_Success_ReturnsSuccessWithLatency()
    {
        using var factory = CreateSqliteFactory();
        var sut = BuildSut(factory);

        var result = await sut.TestConnectionAsync();

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.Latency.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task TestConnectionAsync_Failure_ReturnsErrorMessage()
    {
        // InMemory does not support relational-only operations like ExecuteSqlRaw - a real
        // connection failure would behave the same way from TestConnectionAsync's point of view.
        var factory = CreateInMemoryFactory(nameof(TestConnectionAsync_Failure_ReturnsErrorMessage));
        var sut = BuildSut(factory);

        var result = await sut.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    // ==================== GetSlowQueriesAsync ====================

    [Fact]
    public async Task GetSlowQueriesAsync_AlwaysReturnsEmptyList()
    {
        var factory = CreateInMemoryFactory(nameof(GetSlowQueriesAsync_AlwaysReturnsEmptyList));
        var sut = BuildSut(factory);

        (await sut.GetSlowQueriesAsync()).Should().BeEmpty();
        (await sut.GetSlowQueriesAsync(count: 3)).Should().BeEmpty();
    }

    // ==================== GetHealthReportAsync ====================

    [Fact]
    public async Task GetHealthReportAsync_SuccessPath_SQLite_ReturnsExcellentEmptyReport()
    {
        using var factory = CreateSqliteFactory();
        var sut = BuildSut(factory);

        var report = await sut.GetHealthReportAsync();

        report.DatabaseProvider.Should().Be("SQLite");
        report.IsConnected.Should().BeTrue();
        report.DatabaseVersion.Should().MatchRegex(@"^\d+\.\d+");
        report.TotalTables.Should().BeGreaterThan(20);
        report.TotalRows.Should().Be(0);
        report.HealthScore.Should().Be(100);
        report.HealthStatus.Should().Be("Excellent");
        report.Warnings.Should().BeEmpty();
        report.Recommendations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHealthReportAsync_ConnectionFails_ReturnsZeroScoreWithWarning()
    {
        var factory = CreateInMemoryFactory(nameof(GetHealthReportAsync_ConnectionFails_ReturnsZeroScoreWithWarning));
        var sut = BuildSut(factory);

        var report = await sut.GetHealthReportAsync();

        report.IsConnected.Should().BeFalse();
        report.HealthScore.Should().Be(0);
        report.Warnings.Should().ContainSingle(w => w.Contains("Database connection failed"));
    }

    [Fact]
    public async Task GetHealthReportAsync_ContextCreationThrows_ReturnsZeroScoreWithErrorWarning()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db unavailable")));

        var sut = BuildSut(throwingFactory);

        var report = await sut.GetHealthReportAsync();

        report.IsConnected.Should().BeFalse();
        report.HealthScore.Should().Be(0);
        report.Warnings.Should().ContainSingle(w => w.Contains("Error generating report") && w.Contains("db unavailable"));
    }

    // ==================== GetTableStatisticsAsync ====================

    [Fact]
    public async Task GetTableStatisticsAsync_SQLite_ReturnsOrderedStatsWithPercentages()
    {
        using var factory = CreateSqliteFactory();
        await using (var seed = factory.CreateDbContext())
        {
            seed.Warehouses.Add(new Warehouse { Name = "W1", Code = "WH001", Address = "a" });
            await seed.SaveChangesAsync();
            var whId = seed.Warehouses.First().Id;
            seed.Users.AddRange(
                new User { Username = "u1", Email = "u1@x.local", PasswordHash = "x", WarehouseId = whId },
                new User { Username = "u2", Email = "u2@x.local", PasswordHash = "x", WarehouseId = whId });
            await seed.SaveChangesAsync();
        }

        var sut = BuildSut(factory);

        var stats = await sut.GetTableStatisticsAsync();

        stats.Should().NotBeEmpty();
        var users = stats.Single(s => s.TableName == "Users");
        var warehouses = stats.Single(s => s.TableName == "Warehouses");
        users.RowCount.Should().Be(2);
        warehouses.RowCount.Should().Be(1);
        stats.IndexOf(users).Should().BeLessThan(stats.IndexOf(warehouses), "stats must be ordered by size descending");
        (users.PercentageOfTotal + warehouses.PercentageOfTotal).Should().BeApproximately(100.0, 0.5);
    }

    [Fact]
    public async Task GetTableStatisticsAsync_UnknownProvider_UsesGenericTableStats()
    {
        var factory = CreateInMemoryFactory(nameof(GetTableStatisticsAsync_UnknownProvider_UsesGenericTableStats));
        await using (var seed = factory.CreateDbContext())
        {
            seed.Warehouses.Add(new Warehouse { Id = 1, Name = "W1", Code = "WH001", Address = "a" });
            seed.Users.Add(new User { Id = 1, Username = "u1", Email = "u1@x.local", PasswordHash = "x", WarehouseId = 1 });
            seed.Products.AddRange(
                new Product { Name = "p1", WarehouseId = 1, Price = 1 },
                new Product { Name = "p2", WarehouseId = 1, Price = 1 });
            await seed.SaveChangesAsync();
        }

        var sut = BuildSut(factory, (DatabaseProvider)99);

        var stats = await sut.GetTableStatisticsAsync();

        stats.Select(s => s.TableName).Should().BeEquivalentTo(
            new[] { "Users", "Products", "Categories", "StockMovements", "AuditLogs", "Notifications", "UserSessions" });
        stats.Single(s => s.TableName == "Users").RowCount.Should().Be(1);
        stats.Single(s => s.TableName == "Products").RowCount.Should().Be(2);
    }

    [Fact]
    public async Task GetTableStatisticsAsync_PostgreSQLProviderAgainstSqliteConnection_ReturnsEmptyWithoutThrowing()
    {
        // Provider says PostgreSQL, but the actual connection is SQLite - the Postgres-specific
        // SQL fails and is caught internally (GetPostgreSqlTableStatsAsync's own try/catch),
        // covering that code path without needing a real PostgreSQL server.
        using var factory = CreateSqliteFactory();
        var sut = BuildSut(factory, DatabaseProvider.PostgreSQL);

        var act = async () => await sut.GetTableStatisticsAsync();

        (await act.Should().NotThrowAsync()).Which.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTableStatisticsAsync_MySQLProviderAgainstSqliteConnection_ReturnsEmptyWithoutThrowing()
    {
        using var factory = CreateSqliteFactory();
        var sut = BuildSut(factory, DatabaseProvider.MySQL);

        var act = async () => await sut.GetTableStatisticsAsync();

        (await act.Should().NotThrowAsync()).Which.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTableStatisticsAsync_ContextCreationThrows_ReturnsEmptyList()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("boom")));

        var sut = BuildSut(throwingFactory);

        (await sut.GetTableStatisticsAsync()).Should().BeEmpty();
    }

    // ==================== GetIndexStatisticsAsync ====================

    [Fact]
    public async Task GetIndexStatisticsAsync_SQLiteProvider_ReturnsEmptyList()
    {
        using var factory = CreateSqliteFactory();
        var sut = BuildSut(factory, DatabaseProvider.SQLite);

        (await sut.GetIndexStatisticsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetIndexStatisticsAsync_PostgreSQLProviderAgainstSqliteConnection_ReturnsEmptyWithoutThrowing()
    {
        using var factory = CreateSqliteFactory();
        var sut = BuildSut(factory, DatabaseProvider.PostgreSQL);

        var act = async () => await sut.GetIndexStatisticsAsync();

        (await act.Should().NotThrowAsync()).Which.Should().BeEmpty();
    }

    [Fact]
    public async Task GetIndexStatisticsAsync_MySQLProviderAgainstSqliteConnection_ReturnsEmptyWithoutThrowing()
    {
        using var factory = CreateSqliteFactory();
        var sut = BuildSut(factory, DatabaseProvider.MySQL);

        var act = async () => await sut.GetIndexStatisticsAsync();

        (await act.Should().NotThrowAsync()).Which.Should().BeEmpty();
    }

    [Fact]
    public async Task GetIndexStatisticsAsync_ContextCreationThrows_ReturnsEmptyList()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("boom")));

        var sut = BuildSut(throwingFactory);

        (await sut.GetIndexStatisticsAsync()).Should().BeEmpty();
    }

    // ==================== GetDatabaseVersionAsync (private) ====================

    [Fact]
    public async Task GetDatabaseVersionAsync_SQLite_ReturnsVersionString()
    {
        using var factory = CreateSqliteFactory();
        var sut = BuildSut(factory, DatabaseProvider.SQLite);
        await using var ctx = factory.CreateDbContext();

        var version = await InvokeAsync<string>(sut, "GetDatabaseVersionAsync", ctx, CancellationToken.None);

        version.Should().MatchRegex(@"^\d+\.\d+");
    }

    [Fact]
    public async Task GetDatabaseVersionAsync_PostgreSQLProviderAgainstSqliteConnection_ReturnsUnknown()
    {
        using var factory = CreateSqliteFactory();
        var (sut, settings) = BuildSutWithSettings(factory, DatabaseProvider.SQLite);
        settings.Provider = DatabaseProvider.PostgreSQL;
        await using var ctx = factory.CreateDbContext();

        var version = await InvokeAsync<string>(sut, "GetDatabaseVersionAsync", ctx, CancellationToken.None);

        version.Should().Be("Unknown");
    }

    [Fact]
    public async Task GetDatabaseVersionAsync_UnsupportedProvider_ReturnsUnknownWithoutTouchingConnection()
    {
        // versionQuery is null for an unrecognized provider, so the method returns immediately -
        // proven here by using an InMemory context, which would throw if the connection were touched.
        var factory = CreateInMemoryFactory(nameof(GetDatabaseVersionAsync_UnsupportedProvider_ReturnsUnknownWithoutTouchingConnection));
        var sut = BuildSut(factory, (DatabaseProvider)99);
        await using var ctx = factory.CreateDbContext();

        var version = await InvokeAsync<string>(sut, "GetDatabaseVersionAsync", ctx, CancellationToken.None);

        version.Should().Be("Unknown");
    }

    // ==================== GetConnectionStatsAsync / provider sub-methods (private) ====================

    [Fact]
    public async Task GetConnectionStatsAsync_DefaultProvider_ReturnsZeroZero()
    {
        using var factory = CreateSqliteFactory();
        var sut = BuildSut(factory, DatabaseProvider.SQLite);
        await using var ctx = factory.CreateDbContext();

        var (active, max) = await InvokeAsync<(int, int)>(sut, "GetConnectionStatsAsync", ctx, CancellationToken.None);

        active.Should().Be(0);
        max.Should().Be(0);
    }

    [Fact]
    public async Task GetConnectionStatsAsync_PostgreSQLProviderAgainstSqliteConnection_ReturnsZeroZeroWithoutThrowing()
    {
        using var factory = CreateSqliteFactory();
        var (sut, settings) = BuildSutWithSettings(factory, DatabaseProvider.SQLite);
        settings.Provider = DatabaseProvider.PostgreSQL;
        await using var ctx = factory.CreateDbContext();

        var (active, max) = await InvokeAsync<(int, int)>(sut, "GetConnectionStatsAsync", ctx, CancellationToken.None);

        active.Should().Be(0);
        max.Should().Be(0);
    }

    [Fact]
    public async Task GetConnectionStatsAsync_MySQLProviderAgainstSqliteConnection_ReturnsZeroZeroWithoutThrowing()
    {
        using var factory = CreateSqliteFactory();
        var (sut, settings) = BuildSutWithSettings(factory, DatabaseProvider.SQLite);
        settings.Provider = DatabaseProvider.MySQL;
        await using var ctx = factory.CreateDbContext();

        var (active, max) = await InvokeAsync<(int, int)>(sut, "GetConnectionStatsAsync", ctx, CancellationToken.None);

        active.Should().Be(0);
        max.Should().Be(0);
    }

    // ==================== GetAverageQueryTimeAsync / provider sub-methods (private) ====================

    [Fact]
    public async Task GetAverageQueryTimeAsync_DefaultProvider_ReturnsZero()
    {
        using var factory = CreateSqliteFactory();
        var sut = BuildSut(factory, DatabaseProvider.SQLite);
        await using var ctx = factory.CreateDbContext();

        (await InvokeAsync<double>(sut, "GetAverageQueryTimeAsync", ctx, CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task GetAverageQueryTimeAsync_PostgreSQLProviderAgainstSqliteConnection_ReturnsZeroWithoutThrowing()
    {
        using var factory = CreateSqliteFactory();
        var (sut, settings) = BuildSutWithSettings(factory, DatabaseProvider.SQLite);
        settings.Provider = DatabaseProvider.PostgreSQL;
        await using var ctx = factory.CreateDbContext();

        (await InvokeAsync<double>(sut, "GetAverageQueryTimeAsync", ctx, CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task GetAverageQueryTimeAsync_MySQLProviderAgainstSqliteConnection_ReturnsZeroWithoutThrowing()
    {
        using var factory = CreateSqliteFactory();
        var (sut, settings) = BuildSutWithSettings(factory, DatabaseProvider.SQLite);
        settings.Provider = DatabaseProvider.MySQL;
        await using var ctx = factory.CreateDbContext();

        (await InvokeAsync<double>(sut, "GetAverageQueryTimeAsync", ctx, CancellationToken.None)).Should().Be(0);
    }

    // ==================== GetLastBackupDateAsync (private, currently unused by production code) ====================
    // BUG (suspected): GetHealthReportAsync never calls GetLastBackupDateAsync, so
    // DatabaseHealthReport.LastBackup/LastVacuum/NeedsVacuum are always left at their default
    // values (null/false) regardless of actual backup history. Verified directly via reflection
    // since there is no public call path to exercise it otherwise.

    [Fact]
    public async Task GetLastBackupDateAsync_NoSuccessfulBackups_ReturnsNull()
    {
        using var factory = CreateSqliteFactory();
        var sut = BuildSut(factory);
        await using var ctx = factory.CreateDbContext();

        (await InvokeAsync<DateTime?>(sut, "GetLastBackupDateAsync", ctx, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetLastBackupDateAsync_HasSuccessfulBackup_ReturnsMostRecentSuccessfulDate()
    {
        using var factory = CreateSqliteFactory();
        var sut = BuildSut(factory);
        var expected = DateTime.UtcNow.AddDays(-1);

        await using (var seed = factory.CreateDbContext())
        {
            seed.BackupProviders.Add(new BackupProvider { Name = "local", Type = BackupProviderType.Local });
            await seed.SaveChangesAsync();
            var providerId = seed.BackupProviders.First().Id;

            seed.BackupHistory.Add(new BackupHistory { BackupProviderId = providerId, FileName = "old.bak", BackupDate = DateTime.UtcNow.AddDays(-5), Status = BackupStatus.Success });
            seed.BackupHistory.Add(new BackupHistory { BackupProviderId = providerId, FileName = "failed.bak", BackupDate = DateTime.UtcNow, Status = BackupStatus.Failed });
            seed.BackupHistory.Add(new BackupHistory { BackupProviderId = providerId, FileName = "recent.bak", BackupDate = expected, Status = BackupStatus.Success });
            await seed.SaveChangesAsync();
        }

        await using var ctx = factory.CreateDbContext();
        var result = await InvokeAsync<DateTime?>(sut, "GetLastBackupDateAsync", ctx, CancellationToken.None);

        result.Should().BeCloseTo(expected, TimeSpan.FromSeconds(2));
    }

    // ==================== CalculateHealthScore (private) ====================

    [Fact]
    public void CalculateHealthScore_NotConnected_ReturnsZeroImmediately()
    {
        var sut = BuildSut(CreateInMemoryFactory(nameof(CalculateHealthScore_NotConnected_ReturnsZeroImmediately)));
        var report = new DatabaseHealthReport { IsConnected = false };

        InvokeCalculateHealthScore(sut, report, new List<TableStatistics>()).Should().Be(0);
    }

    [Theory]
    [InlineData(1500, 75, "Kritische Latenz")]
    [InlineData(600, 85, "Hohe Latenz")]
    [InlineData(300, 92, null)]
    [InlineData(150, 97, null)]
    [InlineData(50, 100, null)]
    public void CalculateHealthScore_LatencyBrackets_DeductExpectedPoints(int latencyMs, int expectedScore, string? expectedWarningSubstring)
    {
        var sut = BuildSut(CreateInMemoryFactory($"{nameof(CalculateHealthScore_LatencyBrackets_DeductExpectedPoints)}-{latencyMs}"));
        var report = HealthyReport();
        report.ConnectionLatency = TimeSpan.FromMilliseconds(latencyMs);

        var score = InvokeCalculateHealthScore(sut, report, new List<TableStatistics>());

        score.Should().Be(expectedScore);
        if (expectedWarningSubstring is not null)
        {
            report.Warnings.Should().ContainSingle(w => w.Contains(expectedWarningSubstring));
        }
        else
        {
            report.Warnings.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(60, 80, "Sehr grosse Datenbank")]
    [InlineData(25, 85, "Grosse Datenbank")]
    [InlineData(15, 90, null)]
    [InlineData(7, 95, null)]
    [InlineData(2, 100, null)]
    public void CalculateHealthScore_SizeBrackets_DeductExpectedPoints(int sizeGb, int expectedScore, string? expectedWarningSubstring)
    {
        var sut = BuildSut(CreateInMemoryFactory($"{nameof(CalculateHealthScore_SizeBrackets_DeductExpectedPoints)}-{sizeGb}"));
        var report = HealthyReport();
        report.DatabaseSizeBytes = (long)sizeGb * 1024 * 1024 * 1024;

        var score = InvokeCalculateHealthScore(sut, report, new List<TableStatistics>());

        score.Should().Be(expectedScore);
        if (expectedWarningSubstring is not null)
        {
            report.Warnings.Should().ContainSingle(w => w.Contains(expectedWarningSubstring));
        }
        else
        {
            report.Warnings.Should().BeEmpty();
        }
    }

    [Fact]
    public void CalculateHealthScore_MoreThanFiveLargeTables_DeductsFifteenPointsWithWarning()
    {
        var sut = BuildSut(CreateInMemoryFactory(nameof(CalculateHealthScore_MoreThanFiveLargeTables_DeductsFifteenPointsWithWarning)));
        var report = HealthyReport();
        var stats = Enumerable.Range(0, 6).Select(i => new TableStatistics { TableName = $"t{i}", RowCount = 2_000_000 }).ToList();

        var score = InvokeCalculateHealthScore(sut, report, stats);

        score.Should().Be(85);
        report.Warnings.Should().ContainSingle(w => w.Contains("Tabellen mit mehr als 1 Mio"));
    }

    [Fact]
    public void CalculateHealthScore_ThreeLargeTables_DeductsTenPointsWithWarning()
    {
        var sut = BuildSut(CreateInMemoryFactory(nameof(CalculateHealthScore_ThreeLargeTables_DeductsTenPointsWithWarning)));
        var report = HealthyReport();
        var stats = Enumerable.Range(0, 3).Select(i => new TableStatistics { TableName = $"t{i}", RowCount = 2_000_000 }).ToList();

        var score = InvokeCalculateHealthScore(sut, report, stats);

        score.Should().Be(90);
        report.Warnings.Should().ContainSingle(w => w.Contains("grosse Tabellen"));
    }

    [Fact]
    public void CalculateHealthScore_OneLargeTable_DeductsFivePointsSilently()
    {
        var sut = BuildSut(CreateInMemoryFactory(nameof(CalculateHealthScore_OneLargeTable_DeductsFivePointsSilently)));
        var report = HealthyReport();
        var stats = new List<TableStatistics> { new() { TableName = "t0", RowCount = 2_000_000 } };

        var score = InvokeCalculateHealthScore(sut, report, stats);

        score.Should().Be(95);
        report.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void CalculateHealthScore_HighIndexOverhead_DeductsFifteenPointsWithWarning()
    {
        var sut = BuildSut(CreateInMemoryFactory(nameof(CalculateHealthScore_HighIndexOverhead_DeductsFifteenPointsWithWarning)));
        var report = HealthyReport();
        var stats = new List<TableStatistics> { new() { TableName = "t0", SizeBytes = 1000, IndexSizeBytes = 3000 } }; // 300%

        var score = InvokeCalculateHealthScore(sut, report, stats);

        score.Should().Be(85);
        report.Warnings.Should().ContainSingle(w => w.Contains("Index-Overhead sehr hoch"));
    }

    [Fact]
    public void CalculateHealthScore_ModerateIndexOverhead_DeductsEightPointsSilently()
    {
        var sut = BuildSut(CreateInMemoryFactory(nameof(CalculateHealthScore_ModerateIndexOverhead_DeductsEightPointsSilently)));
        var report = HealthyReport();
        var stats = new List<TableStatistics> { new() { TableName = "t0", SizeBytes = 1000, IndexSizeBytes = 1500 } }; // 150%

        var score = InvokeCalculateHealthScore(sut, report, stats);

        score.Should().Be(92);
        report.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void CalculateHealthScore_LowIndexRatioOnLargeDataset_DeductsFivePointsWithWarning()
    {
        var sut = BuildSut(CreateInMemoryFactory(nameof(CalculateHealthScore_LowIndexRatioOnLargeDataset_DeductsFivePointsWithWarning)));
        var report = HealthyReport();
        var stats = new List<TableStatistics>
        {
            new() { TableName = "t0", SizeBytes = 200L * 1024 * 1024, IndexSizeBytes = 1L * 1024 * 1024 } // 200MB data, 0.5% index ratio
        };

        var score = InvokeCalculateHealthScore(sut, report, stats);

        score.Should().Be(95);
        report.Warnings.Should().ContainSingle(w => w.Contains("Wenige Indizes"));
    }

    // ==================== GenerateRecommendations (private) ====================

    [Fact]
    public void GenerateRecommendations_NoTriggeringConditions_ReturnsEmptyList()
    {
        var sut = BuildSut(CreateInMemoryFactory(nameof(GenerateRecommendations_NoTriggeringConditions_ReturnsEmptyList)));
        var report = HealthyReport();

        InvokeGenerateRecommendations(sut, report, new List<TableStatistics>());

        report.Recommendations.Should().BeEmpty();
    }

    [Fact]
    public void GenerateRecommendations_HighLatency_AddsNetworkRecommendation()
    {
        var sut = BuildSut(CreateInMemoryFactory(nameof(GenerateRecommendations_HighLatency_AddsNetworkRecommendation)));
        var report = HealthyReport();
        report.ConnectionLatency = TimeSpan.FromMilliseconds(300);

        InvokeGenerateRecommendations(sut, report, new List<TableStatistics>());

        report.Recommendations.Should().ContainSingle(r => r.Contains("Netzwerkverbindung"));
    }

    [Fact]
    public void GenerateRecommendations_LargeDatabase_AddsArchivingRecommendation()
    {
        var sut = BuildSut(CreateInMemoryFactory(nameof(GenerateRecommendations_LargeDatabase_AddsArchivingRecommendation)));
        var report = HealthyReport();
        report.DatabaseSizeBytes = 11L * 1024 * 1024 * 1024;

        InvokeGenerateRecommendations(sut, report, new List<TableStatistics>());

        report.Recommendations.Should().ContainSingle(r => r.Contains("Archivierung"));
    }

    [Fact]
    public void GenerateRecommendations_ManyAuditLogRows_AddsCleanupRecommendation()
    {
        var sut = BuildSut(CreateInMemoryFactory(nameof(GenerateRecommendations_ManyAuditLogRows_AddsCleanupRecommendation)));
        var report = HealthyReport();
        var stats = new List<TableStatistics> { new() { TableName = "AuditLogs", RowCount = 100_001 } };

        InvokeGenerateRecommendations(sut, report, stats);

        report.Recommendations.Should().ContainSingle(r => r.Contains("Audit-Logs bereinigen"));
    }

    [Fact]
    public void GenerateRecommendations_ManySessionRows_AddsCleanupRecommendation()
    {
        var sut = BuildSut(CreateInMemoryFactory(nameof(GenerateRecommendations_ManySessionRows_AddsCleanupRecommendation)));
        var report = HealthyReport();
        var stats = new List<TableStatistics> { new() { TableName = "UserSessions", RowCount = 10_001 } };

        InvokeGenerateRecommendations(sut, report, stats);

        report.Recommendations.Should().ContainSingle(r => r.Contains("Session-Bereinigung"));
    }

    [Fact]
    public void GenerateRecommendations_ManyNotificationRows_AddsCleanupRecommendation()
    {
        var sut = BuildSut(CreateInMemoryFactory(nameof(GenerateRecommendations_ManyNotificationRows_AddsCleanupRecommendation)));
        var report = HealthyReport();
        var stats = new List<TableStatistics> { new() { TableName = "Notifications", RowCount = 50_001 } };

        InvokeGenerateRecommendations(sut, report, stats);

        report.Recommendations.Should().ContainSingle(r => r.Contains("Benachrichtigungen loeschen"));
    }

    [Fact]
    public void GenerateRecommendations_PostgreSQLProvider_AddsVacuumRecommendation()
    {
        var (sut, _) = BuildSutWithSettings(CreateInMemoryFactory(nameof(GenerateRecommendations_PostgreSQLProvider_AddsVacuumRecommendation)), DatabaseProvider.PostgreSQL);
        var report = HealthyReport();
        report.DatabaseSizeBytes = 2L * 1024 * 1024 * 1024;

        InvokeGenerateRecommendations(sut, report, new List<TableStatistics>());

        report.Recommendations.Should().ContainSingle(r => r.Contains("VACUUM ANALYZE"));
    }

    [Fact]
    public void GenerateRecommendations_MySQLProvider_AddsOptimizeTableRecommendation()
    {
        var (sut, _) = BuildSutWithSettings(CreateInMemoryFactory(nameof(GenerateRecommendations_MySQLProvider_AddsOptimizeTableRecommendation)), DatabaseProvider.MySQL);
        var report = HealthyReport();
        report.DatabaseSizeBytes = 6L * 1024 * 1024 * 1024;

        InvokeGenerateRecommendations(sut, report, new List<TableStatistics>());

        report.Recommendations.Should().ContainSingle(r => r.Contains("OPTIMIZE TABLE"));
    }

    [Fact]
    public void GenerateRecommendations_HugeTotalRowCount_AddsPartitioningRecommendation()
    {
        var sut = BuildSut(CreateInMemoryFactory(nameof(GenerateRecommendations_HugeTotalRowCount_AddsPartitioningRecommendation)));
        var report = HealthyReport();
        var stats = new List<TableStatistics> { new() { TableName = "t0", RowCount = 10_000_001 } };

        InvokeGenerateRecommendations(sut, report, stats);

        report.Recommendations.Should().ContainSingle(r => r.Contains("Partitionierung"));
    }

    // ==================== POCO computed properties (DatabaseHealthReport / TableStatistics /
    // IndexStatistics / SlowQueryInfo) ====================
    // These classes live in DatabaseHealthService.cs alongside the service itself.
    // IndexStatistics and SlowQueryInfo are never actually instantiated by reachable production
    // code (GetIndexStatisticsAsync's Postgres/MySQL readers need a live server; GetSlowQueriesAsync
    // is a stub that always returns an empty list), so their properties are exercised here directly.

    [Theory]
    [InlineData(95, "Excellent")]
    [InlineData(85, "Good")]
    [InlineData(60, "Fair")]
    [InlineData(40, "Poor")]
    [InlineData(10, "Critical")]
    public void DatabaseHealthReport_HealthStatus_MapsScoreToExpectedLabel(int score, string expected)
    {
        new DatabaseHealthReport { HealthScore = score }.HealthStatus.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(500, "500 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    public void DatabaseHealthReport_DatabaseSizeFormatted_ScalesToLargestUnit(long bytes, string expected)
    {
        new DatabaseHealthReport { DatabaseSizeBytes = bytes }.DatabaseSizeFormatted.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(2L * 1024 * 1024, "2 MB")]
    [InlineData(4L * 1024 * 1024 * 1024, "4 GB")]
    public void TableStatistics_SizeFormatted_ScalesToLargestUnit(long bytes, string expected)
    {
        new TableStatistics { SizeBytes = bytes }.SizeFormatted.Should().Be(expected);
    }

    [Fact]
    public void TableStatistics_SizeFormatted_FractionalValue_UsesCurrentCultureDecimalSeparator()
    {
        // FormatSize interpolates with "0.##" against CultureInfo.CurrentCulture, so the
        // separator itself is intentionally not hardcoded here (this test run's culture uses ",").
        var formatted = new TableStatistics { SizeBytes = 1536 }.SizeFormatted;

        formatted.Should().Match("1?5 KB");
    }

    [Fact]
    public void IndexStatistics_PropertiesRoundTrip()
    {
        var stat = new IndexStatistics
        {
            IndexName = "IX_Test",
            TableName = "Products",
            Columns = "Id,Name",
            SizeBytes = 1024,
            IsUnique = true,
            ScanCount = 42,
            EfficiencyPercent = 87.5
        };

        stat.IndexName.Should().Be("IX_Test");
        stat.TableName.Should().Be("Products");
        stat.Columns.Should().Be("Id,Name");
        stat.SizeBytes.Should().Be(1024);
        stat.IsUnique.Should().BeTrue();
        stat.ScanCount.Should().Be(42);
        stat.EfficiencyPercent.Should().Be(87.5);
    }

    [Fact]
    public void SlowQueryInfo_PropertiesRoundTrip()
    {
        var now = DateTime.UtcNow;
        var info = new SlowQueryInfo { Query = "SELECT 1", DurationMs = 12.5, ExecutedAt = now, CallCount = 3 };

        info.Query.Should().Be("SELECT 1");
        info.DurationMs.Should().Be(12.5);
        info.ExecutedAt.Should().Be(now);
        info.CallCount.Should().Be(3);
    }
}
