using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.UnitTests.Services.Admin;

public class AuditQueryServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static User MakeUser(int id, bool isActive = true, bool isDeleted = false) => new()
    {
        Id = id,
        Username = $"u{id}",
        Email = $"u{id}@x.local",
        DisplayName = $"U{id}",
        PasswordHash = "x",
        WarehouseId = 1,
        IsActive = isActive,
        IsDeleted = isDeleted
    };

    private static AuditLog MakeLog(
        int id,
        string action = "LOGIN",
        AuditSeverity severity = AuditSeverity.Info,
        int? userId = null,
        DateTime? timestamp = null) => new()
        {
            Id = id,
            Action = action,
            Severity = severity,
            UserId = userId,
            Timestamp = timestamp ?? DateTime.UtcNow
        };

    [Fact]
    public async Task GetActiveUsersForFilterAsync_ExcludesInactiveAndDeleted()
    {
        var factory = CreateFactory(nameof(GetActiveUsersForFilterAsync_ExcludesInactiveAndDeleted));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(
                MakeUser(1),
                MakeUser(2, isActive: false),
                MakeUser(3, isDeleted: true));
            await db.SaveChangesAsync();
        }

        var sut = new AuditQueryService(factory);

        var users = await sut.GetActiveUsersForFilterAsync();

        users.Should().ContainSingle().Which.Id.Should().Be(1);
    }

    [Fact]
    public async Task GetAuditLogsAsync_AppliesUserFilterAndTakeAndOrdersDescending()
    {
        var factory = CreateFactory(nameof(GetAuditLogsAsync_AppliesUserFilterAndTakeAndOrdersDescending));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLogs.AddRange(
                MakeLog(1, userId: 10, timestamp: now.AddMinutes(-3)),
                MakeLog(2, userId: 10, timestamp: now.AddMinutes(-1)),
                MakeLog(3, userId: 10, timestamp: now.AddMinutes(-2)),
                MakeLog(4, userId: 20, timestamp: now));
            await db.SaveChangesAsync();
        }

        var sut = new AuditQueryService(factory);

        var result = await sut.GetAuditLogsAsync(new AuditLogFilter(UserId: 10, Action: null, Severity: null, TakeCount: 2));

        result.Should().HaveCount(2);
        result.Select(l => l.Id).Should().ContainInOrder(2, 3);
    }

    [Fact]
    public async Task GetAuditLogsAsync_UserIdZeroIsTreatedAsNoFilter()
    {
        var factory = CreateFactory(nameof(GetAuditLogsAsync_UserIdZeroIsTreatedAsNoFilter));
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLogs.AddRange(MakeLog(1, userId: 5), MakeLog(2, userId: 7));
            await db.SaveChangesAsync();
        }

        var sut = new AuditQueryService(factory);

        var result = await sut.GetAuditLogsAsync(new AuditLogFilter(UserId: 0, Action: null, Severity: null, TakeCount: 50));

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAuditLogsAsync_FiltersByActionWhenProvided()
    {
        var factory = CreateFactory(nameof(GetAuditLogsAsync_FiltersByActionWhenProvided));
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLogs.AddRange(MakeLog(1, action: "LOGIN"), MakeLog(2, action: "LOGOUT"));
            await db.SaveChangesAsync();
        }

        var sut = new AuditQueryService(factory);

        var result = await sut.GetAuditLogsAsync(new AuditLogFilter(UserId: null, Action: "LOGIN", Severity: null, TakeCount: 50));

        result.Should().ContainSingle().Which.Action.Should().Be("LOGIN");
    }

    [Fact]
    public async Task GetAuditLogsAsync_WhitespaceActionIsIgnored()
    {
        var factory = CreateFactory(nameof(GetAuditLogsAsync_WhitespaceActionIsIgnored));
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLogs.AddRange(MakeLog(1, action: "LOGIN"), MakeLog(2, action: "LOGOUT"));
            await db.SaveChangesAsync();
        }

        var sut = new AuditQueryService(factory);

        var result = await sut.GetAuditLogsAsync(new AuditLogFilter(UserId: null, Action: "  ", Severity: null, TakeCount: 50));

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAuditLogsAsync_FiltersBySeverityWhenProvided()
    {
        var factory = CreateFactory(nameof(GetAuditLogsAsync_FiltersBySeverityWhenProvided));
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLogs.AddRange(
                MakeLog(1, severity: AuditSeverity.Info),
                MakeLog(2, severity: AuditSeverity.Warning),
                MakeLog(3, severity: AuditSeverity.Error));
            await db.SaveChangesAsync();
        }

        var sut = new AuditQueryService(factory);

        var result = await sut.GetAuditLogsAsync(new AuditLogFilter(null, null, AuditSeverity.Warning, 50));

        result.Should().ContainSingle().Which.Id.Should().Be(2);
    }

    [Fact]
    public async Task GetAuditLogStatsAsync_AggregatesSeverityCounts()
    {
        var factory = CreateFactory(nameof(GetAuditLogStatsAsync_AggregatesSeverityCounts));
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLogs.AddRange(
                MakeLog(1, severity: AuditSeverity.Info),
                MakeLog(2, severity: AuditSeverity.Info),
                MakeLog(3, severity: AuditSeverity.Warning),
                MakeLog(4, severity: AuditSeverity.Error),
                MakeLog(5, severity: AuditSeverity.Critical));
            await db.SaveChangesAsync();
        }

        var sut = new AuditQueryService(factory);

        var stats = await sut.GetAuditLogStatsAsync();

        stats.Total.Should().Be(5);
        stats.InfoCount.Should().Be(2);
        stats.WarningCount.Should().Be(1);
        stats.ErrorAndCriticalCount.Should().Be(2);
    }
}
