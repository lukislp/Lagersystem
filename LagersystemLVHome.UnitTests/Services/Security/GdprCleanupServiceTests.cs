using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.UnitTests.Services.Security;

public class GdprCleanupServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);

        public Task<InventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static GdprCleanupService BuildSut(IDbContextFactory<InventoryDbContext> factory, GdprSettings settings)
        => new(factory, Options.Create(settings), NullLogger<GdprCleanupService>.Instance);

    private static GdprSettings DefaultSettings(bool dryRun = false, int batchSize = 1000) => new()
    {
        DryRun = dryRun,
        BatchSize = batchSize,
        PageViewsRetentionDays = 30,
        ApiRequestsRetentionDays = 30,
        SessionActivitiesRetentionDays = 30,
        UserActivitiesRetentionDays = 30,
        AuditLogsRetentionDays = 90,
        SecurityEventsRetentionDays = 90,
        PerformanceMetricsRetentionDays = 7,
        KeyBackupHistoryRetentionDays = 90
    };

    // ---------- CleanupPageViewsAsync ----------

    [Fact]
    public async Task CleanupPageViewsAsync_DryRun_ReturnsCountWithoutDeleting()
    {
        var factory = CreateFactory(nameof(CleanupPageViewsAsync_DryRun_ReturnsCountWithoutDeleting));
        await using (var db = factory.CreateDbContext())
        {
            db.PageViews.Add(new PageView { Timestamp = DateTime.UtcNow.AddDays(-40) });
            db.PageViews.Add(new PageView { Timestamp = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        var sut = BuildSut(factory, DefaultSettings(dryRun: true));

        var deleted = await sut.CleanupPageViewsAsync();

        deleted.Should().Be(1);
        await using var check = factory.CreateDbContext();
        (await check.PageViews.CountAsync()).Should().Be(2, "dry run must not delete anything");
    }

    [Fact]
    public async Task CleanupPageViewsAsync_DeletesOldRecordsInBatchesAndKeepsRecent()
    {
        var factory = CreateFactory(nameof(CleanupPageViewsAsync_DeletesOldRecordsInBatchesAndKeepsRecent));
        await using (var db = factory.CreateDbContext())
        {
            for (var i = 0; i < 5; i++)
            {
                db.PageViews.Add(new PageView { Timestamp = DateTime.UtcNow.AddDays(-40).AddMinutes(i) });
            }
            db.PageViews.Add(new PageView { Timestamp = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        var sut = BuildSut(factory, DefaultSettings(batchSize: 2));

        var deleted = await sut.CleanupPageViewsAsync();

        deleted.Should().Be(5);
        await using var check = factory.CreateDbContext();
        (await check.PageViews.CountAsync()).Should().Be(1);
    }

    // ---------- CleanupApiRequestsAsync ----------

    [Fact]
    public async Task CleanupApiRequestsAsync_DryRun_ReturnsCountWithoutDeleting()
    {
        var factory = CreateFactory(nameof(CleanupApiRequestsAsync_DryRun_ReturnsCountWithoutDeleting));
        await using (var db = factory.CreateDbContext())
        {
            db.ApiRequests.Add(new ApiRequest { Timestamp = DateTime.UtcNow.AddDays(-31) });
            db.ApiRequests.Add(new ApiRequest { Timestamp = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        var sut = BuildSut(factory, DefaultSettings(dryRun: true));

        (await sut.CleanupApiRequestsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CleanupApiRequestsAsync_DeletesOnlyOldRecords()
    {
        var factory = CreateFactory(nameof(CleanupApiRequestsAsync_DeletesOnlyOldRecords));
        await using (var db = factory.CreateDbContext())
        {
            db.ApiRequests.Add(new ApiRequest { Timestamp = DateTime.UtcNow.AddDays(-31) });
            db.ApiRequests.Add(new ApiRequest { Timestamp = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        var sut = BuildSut(factory, DefaultSettings());

        var deleted = await sut.CleanupApiRequestsAsync();

        deleted.Should().Be(1);
        await using var check = factory.CreateDbContext();
        (await check.ApiRequests.CountAsync()).Should().Be(1);
    }

    // ---------- CleanupSessionActivitiesAsync ----------

    [Fact]
    public async Task CleanupSessionActivitiesAsync_DeletesOnlyOldRecords()
    {
        var factory = CreateFactory(nameof(CleanupSessionActivitiesAsync_DeletesOnlyOldRecords));
        await using (var db = factory.CreateDbContext())
        {
            db.SessionActivities.Add(new SessionActivity { SessionId = 1, ActivityType = "PageView", Timestamp = DateTime.UtcNow.AddDays(-31) });
            db.SessionActivities.Add(new SessionActivity { SessionId = 1, ActivityType = "PageView", Timestamp = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        var sut = BuildSut(factory, DefaultSettings());

        (await sut.CleanupSessionActivitiesAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CleanupSessionActivitiesAsync_DryRun_DoesNotDelete()
    {
        var factory = CreateFactory(nameof(CleanupSessionActivitiesAsync_DryRun_DoesNotDelete));
        await using (var db = factory.CreateDbContext())
        {
            db.SessionActivities.Add(new SessionActivity { SessionId = 1, ActivityType = "PageView", Timestamp = DateTime.UtcNow.AddDays(-31) });
            await db.SaveChangesAsync();
        }

        var sut = BuildSut(factory, DefaultSettings(dryRun: true));

        (await sut.CleanupSessionActivitiesAsync()).Should().Be(1);
        await using var check = factory.CreateDbContext();
        (await check.SessionActivities.CountAsync()).Should().Be(1);
    }

    // ---------- CleanupUserActivitiesAsync ----------

    [Fact]
    public async Task CleanupUserActivitiesAsync_DeletesOnlyOldRecords()
    {
        var factory = CreateFactory(nameof(CleanupUserActivitiesAsync_DeletesOnlyOldRecords));
        await using (var db = factory.CreateDbContext())
        {
            db.UserActivities.Add(new UserActivity { UserId = 1, ActivityType = "Login", EntityType = "User", Timestamp = DateTime.UtcNow.AddDays(-31) });
            db.UserActivities.Add(new UserActivity { UserId = 1, ActivityType = "Login", EntityType = "User", Timestamp = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        var sut = BuildSut(factory, DefaultSettings());

        (await sut.CleanupUserActivitiesAsync()).Should().Be(1);
    }

    // ---------- CleanupAuditLogsAsync ----------

    [Fact]
    public async Task CleanupAuditLogsAsync_DeletesOnlyOldRecords()
    {
        var factory = CreateFactory(nameof(CleanupAuditLogsAsync_DeletesOnlyOldRecords));
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLogs.Add(new AuditLog { Action = "LOGIN", Timestamp = DateTime.UtcNow.AddDays(-91) });
            db.AuditLogs.Add(new AuditLog { Action = "LOGIN", Timestamp = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        var sut = BuildSut(factory, DefaultSettings());

        (await sut.CleanupAuditLogsAsync()).Should().Be(1);
    }

    // ---------- CleanupSecurityEventsAsync ----------

    [Fact]
    public async Task CleanupSecurityEventsAsync_DeletesOnlyOldRecords()
    {
        var factory = CreateFactory(nameof(CleanupSecurityEventsAsync_DeletesOnlyOldRecords));
        await using (var db = factory.CreateDbContext())
        {
            db.SecurityEvents.Add(new SecurityEvent { EventType = "LoginFailed", Timestamp = DateTime.UtcNow.AddDays(-91) });
            db.SecurityEvents.Add(new SecurityEvent { EventType = "LoginFailed", Timestamp = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        var sut = BuildSut(factory, DefaultSettings());

        (await sut.CleanupSecurityEventsAsync()).Should().Be(1);
    }

    // ---------- CleanupPerformanceMetricsAsync ----------

    [Fact]
    public async Task CleanupPerformanceMetricsAsync_DeletesOnlyOldRecords()
    {
        var factory = CreateFactory(nameof(CleanupPerformanceMetricsAsync_DeletesOnlyOldRecords));
        await using (var db = factory.CreateDbContext())
        {
            db.PerformanceMetrics.Add(new PerformanceMetric { Timestamp = DateTime.UtcNow.AddDays(-8) });
            db.PerformanceMetrics.Add(new PerformanceMetric { Timestamp = DateTime.UtcNow.AddHours(-1) });
            await db.SaveChangesAsync();
        }

        var sut = BuildSut(factory, DefaultSettings());

        (await sut.CleanupPerformanceMetricsAsync()).Should().Be(1);
    }

    // ---------- CleanupKeyBackupHistoryAsync ----------

    [Fact]
    public async Task CleanupKeyBackupHistoryAsync_DeletesOnlyOldRecords()
    {
        var factory = CreateFactory(nameof(CleanupKeyBackupHistoryAsync_DeletesOnlyOldRecords));
        await using (var db = factory.CreateDbContext())
        {
            db.KeyBackupHistory.Add(new KeyBackupHistory { BackupProviderId = 1, BackupDate = DateTime.UtcNow.AddDays(-91) });
            db.KeyBackupHistory.Add(new KeyBackupHistory { BackupProviderId = 1, BackupDate = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        var sut = BuildSut(factory, DefaultSettings());

        (await sut.CleanupKeyBackupHistoryAsync()).Should().Be(1);
    }

    // ---------- GetCleanupPreviewAsync ----------

    [Fact]
    public async Task GetCleanupPreviewAsync_CountsAcrossAllCategoriesAndAlwaysFlaggedDryRun()
    {
        var factory = CreateFactory(nameof(GetCleanupPreviewAsync_CountsAcrossAllCategoriesAndAlwaysFlaggedDryRun));
        await using (var db = factory.CreateDbContext())
        {
            db.PageViews.Add(new PageView { Timestamp = DateTime.UtcNow.AddDays(-40) });
            db.ApiRequests.Add(new ApiRequest { Timestamp = DateTime.UtcNow.AddDays(-40) });
            db.SessionActivities.Add(new SessionActivity { SessionId = 1, ActivityType = "x", Timestamp = DateTime.UtcNow.AddDays(-40) });
            db.UserActivities.Add(new UserActivity { UserId = 1, ActivityType = "x", EntityType = "x", Timestamp = DateTime.UtcNow.AddDays(-40) });
            db.AuditLogs.Add(new AuditLog { Action = "x", Timestamp = DateTime.UtcNow.AddDays(-91) });
            db.SecurityEvents.Add(new SecurityEvent { EventType = "x", Timestamp = DateTime.UtcNow.AddDays(-91) });
            db.PerformanceMetrics.Add(new PerformanceMetric { Timestamp = DateTime.UtcNow.AddDays(-8) });
            db.KeyBackupHistory.Add(new KeyBackupHistory { BackupProviderId = 1, BackupDate = DateTime.UtcNow.AddDays(-91) });
            await db.SaveChangesAsync();
        }

        // Even when the settings say DryRun = false, this method never deletes and always reports DryRun = true.
        var sut = BuildSut(factory, DefaultSettings(dryRun: false));

        var preview = await sut.GetCleanupPreviewAsync();

        preview.PageViewsDeleted.Should().Be(1);
        preview.ApiRequestsDeleted.Should().Be(1);
        preview.SessionActivitiesDeleted.Should().Be(1);
        preview.UserActivitiesDeleted.Should().Be(1);
        preview.AuditLogsDeleted.Should().Be(1);
        preview.SecurityEventsDeleted.Should().Be(1);
        preview.PerformanceMetricsDeleted.Should().Be(1);
        preview.KeyBackupHistoryDeleted.Should().Be(1);
        preview.DryRun.Should().BeTrue();

        await using var check = factory.CreateDbContext();
        (await check.PageViews.CountAsync()).Should().Be(1, "preview must never delete data");
    }

    // ---------- GetLastCleanupStatsAsync ----------

    [Fact]
    public async Task GetLastCleanupStatsAsync_NoHistory_ReturnsNull()
    {
        var factory = CreateFactory(nameof(GetLastCleanupStatsAsync_NoHistory_ReturnsNull));
        var sut = BuildSut(factory, DefaultSettings());

        (await sut.GetLastCleanupStatsAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetLastCleanupStatsAsync_ReturnsMostRecentEntry()
    {
        var factory = CreateFactory(nameof(GetLastCleanupStatsAsync_ReturnsMostRecentEntry));
        await using (var db = factory.CreateDbContext())
        {
            db.GdprCleanupHistory.Add(new GdprCleanupHistory
            {
                StartTime = DateTime.UtcNow.AddDays(-2),
                EndTime = DateTime.UtcNow.AddDays(-2).AddMinutes(1),
                Duration = TimeSpan.FromMinutes(1),
                Success = true,
                PageViewsDeleted = 3
            });
            db.GdprCleanupHistory.Add(new GdprCleanupHistory
            {
                StartTime = DateTime.UtcNow.AddDays(-1),
                EndTime = DateTime.UtcNow.AddDays(-1).AddMinutes(2),
                Duration = TimeSpan.FromMinutes(2),
                Success = true,
                PageViewsDeleted = 9,
                ApiRequestsDeleted = 4,
                SessionActivitiesDeleted = 1,
                UserActivitiesDeleted = 2,
                AuditLogsDeleted = 5,
                SecurityEventsDeleted = 6,
                PerformanceMetricsDeleted = 7,
                KeyBackupHistoryDeleted = 8,
                DryRun = false
            });
            await db.SaveChangesAsync();
        }

        var sut = BuildSut(factory, DefaultSettings());

        var last = await sut.GetLastCleanupStatsAsync();

        last.Should().NotBeNull();
        last!.PageViewsDeleted.Should().Be(9);
        last.ApiRequestsDeleted.Should().Be(4);
        last.SessionActivitiesDeleted.Should().Be(1);
        last.UserActivitiesDeleted.Should().Be(2);
        last.AuditLogsDeleted.Should().Be(5);
        last.SecurityEventsDeleted.Should().Be(6);
        last.PerformanceMetricsDeleted.Should().Be(7);
        last.KeyBackupHistoryDeleted.Should().Be(8);
        last.TotalDeleted.Should().Be(9 + 4 + 1 + 2 + 5 + 6 + 7 + 8);
    }

    // ---------- CleanupPersonalDataAsync (full orchestration) ----------

    [Fact]
    public async Task CleanupPersonalDataAsync_HappyPath_AggregatesStatsAndPersistsHistory()
    {
        var factory = CreateFactory(nameof(CleanupPersonalDataAsync_HappyPath_AggregatesStatsAndPersistsHistory));
        await using (var db = factory.CreateDbContext())
        {
            db.PageViews.Add(new PageView { Timestamp = DateTime.UtcNow.AddDays(-40) });
            db.ApiRequests.Add(new ApiRequest { Timestamp = DateTime.UtcNow.AddDays(-40) });
            db.SessionActivities.Add(new SessionActivity { SessionId = 1, ActivityType = "x", Timestamp = DateTime.UtcNow.AddDays(-40) });
            db.UserActivities.Add(new UserActivity { UserId = 1, ActivityType = "x", EntityType = "x", Timestamp = DateTime.UtcNow.AddDays(-40) });
            db.AuditLogs.Add(new AuditLog { Action = "x", Timestamp = DateTime.UtcNow.AddDays(-91) });
            db.SecurityEvents.Add(new SecurityEvent { EventType = "x", Timestamp = DateTime.UtcNow.AddDays(-91) });
            db.PerformanceMetrics.Add(new PerformanceMetric { Timestamp = DateTime.UtcNow.AddDays(-8) });
            db.KeyBackupHistory.Add(new KeyBackupHistory { BackupProviderId = 1, BackupDate = DateTime.UtcNow.AddDays(-91) });
            await db.SaveChangesAsync();
        }

        var sut = BuildSut(factory, DefaultSettings());

        var stats = await sut.CleanupPersonalDataAsync();

        stats.Success.Should().BeTrue();
        stats.PageViewsDeleted.Should().Be(1);
        stats.ApiRequestsDeleted.Should().Be(1);
        stats.SessionActivitiesDeleted.Should().Be(1);
        stats.UserActivitiesDeleted.Should().Be(1);
        stats.AuditLogsDeleted.Should().Be(1);
        stats.SecurityEventsDeleted.Should().Be(1);
        stats.PerformanceMetricsDeleted.Should().Be(1);
        stats.KeyBackupHistoryDeleted.Should().Be(1);
        stats.TotalDeleted.Should().Be(8);
        stats.EndTime.Should().NotBeNull();
        stats.Duration.Should().NotBeNull();
        stats.DryRun.Should().BeFalse();

        var lastSaved = await sut.GetLastCleanupStatsAsync();
        lastSaved.Should().NotBeNull();
        lastSaved!.TotalDeleted.Should().Be(8);
    }

    [Fact]
    public async Task CleanupPersonalDataAsync_DryRun_DoesNotPersistHistory()
    {
        var factory = CreateFactory(nameof(CleanupPersonalDataAsync_DryRun_DoesNotPersistHistory));
        await using (var db = factory.CreateDbContext())
        {
            db.PageViews.Add(new PageView { Timestamp = DateTime.UtcNow.AddDays(-40) });
            await db.SaveChangesAsync();
        }

        var sut = BuildSut(factory, DefaultSettings(dryRun: true));

        var stats = await sut.CleanupPersonalDataAsync();

        stats.Success.Should().BeTrue();
        stats.DryRun.Should().BeTrue();
        (await sut.GetLastCleanupStatsAsync()).Should().BeNull("dry-run cleanups must not be persisted to history");
    }

    [Fact]
    public async Task CleanupPersonalDataAsync_ContextFactoryThrows_RethrowsAndDoesNotSwallow()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db unavailable")));

        var sut = BuildSut(throwingFactory, DefaultSettings());

        var act = async () => await sut.CleanupPersonalDataAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("db unavailable");
    }

    [Fact]
    public async Task CleanupPersonalDataAsync_SaveStatsFails_StillReturnsSuccessfulStats()
    {
        // The 8 cleanup calls each open one context; SaveCleanupStatsAsync opens a 9th.
        // Make only that 9th call fail - SaveCleanupStatsAsync swallows the exception internally,
        // so CleanupPersonalDataAsync should still report success.
        var name = nameof(CleanupPersonalDataAsync_SaveStatsFails_StillReturnsSuccessfulStats);
        var options = new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options;

        var callCount = 0;
        var factory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount > 8)
                {
                    return Task.FromException<InventoryDbContext>(new InvalidOperationException("save failed"));
                }

                return Task.FromResult(new InventoryDbContext(options));
            });

        var sut = BuildSut(factory, DefaultSettings());

        var stats = await sut.CleanupPersonalDataAsync();

        stats.Success.Should().BeTrue("SaveCleanupStatsAsync catches and logs its own failures instead of propagating them");
        callCount.Should().Be(9);
    }
}
