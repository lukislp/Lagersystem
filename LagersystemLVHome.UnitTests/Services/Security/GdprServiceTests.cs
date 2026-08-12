using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.UnitTests.Services.Security;

/// <summary>
/// Covers <see cref="GdprService"/>: consent bookkeeping, the personal data export used for
/// GDPR "right of access" requests, account soft/hard deletion, anonymization and the
/// inactive-user lookup used to drive retention policy. <see cref="IAuditService"/> is
/// substituted so tests can assert the compliance-relevant events are actually recorded.
/// </summary>
public class GdprServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static GdprService Build(IDbContextFactory<InventoryDbContext> factory, IAuditService? audit = null)
        => new(factory, audit ?? Substitute.For<IAuditService>());

    private static async Task<User> SeedUserAsync(
        IDbContextFactory<InventoryDbContext> factory, int id = 1, Action<User>? mutate = null)
    {
        await using var db = factory.CreateDbContext();
        if (!await db.Warehouses.AnyAsync(w => w.Id == 1))
            db.Warehouses.Add(new Warehouse { Id = 1, Name = "WH", Code = "T", IsActive = true });
        var user = new User
        {
            Id = id,
            Username = $"u{id}",
            Email = $"u{id}@test.local",
            DisplayName = $"User {id}",
            PasswordHash = "hash",
            IsActive = true,
            WarehouseId = 1,
            LastLoginIp = "1.2.3.4"
        };
        mutate?.Invoke(user);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // ---- GiveConsentAsync ----------------------------------------------------------------

    [Fact]
    public async Task GiveConsentAsync_UnknownUser_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(GiveConsentAsync_UnknownUser_ReturnsFalse));
        var sut = Build(factory);

        (await sut.GiveConsentAsync(999)).Should().BeFalse();
    }

    [Fact]
    public async Task GiveConsentAsync_WithoutMarketing_SetsConsentButNotMarketingDate()
    {
        var factory = CreateFactory(nameof(GiveConsentAsync_WithoutMarketing_SetsConsentButNotMarketingDate));
        var user = await SeedUserAsync(factory, mutate: u => u.GdprConsentGiven = false);
        var audit = Substitute.For<IAuditService>();
        var sut = Build(factory, audit);

        var ok = await sut.GiveConsentAsync(user.Id, marketingConsent: false);

        ok.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        var refreshed = await db.Users.FindAsync(user.Id);
        refreshed!.GdprConsentGiven.Should().BeTrue();
        refreshed.GdprConsentDate.Should().NotBeNull();
        refreshed.GdprConsentVersion.Should().Be("1.0");
        refreshed.MarketingConsent.Should().BeFalse();
        refreshed.MarketingConsentDate.Should().BeNull();
        await audit.Received(1).LogAsync("GDPR_CONSENT", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Info, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GiveConsentAsync_WithMarketing_SetsMarketingConsentDate()
    {
        var factory = CreateFactory(nameof(GiveConsentAsync_WithMarketing_SetsMarketingConsentDate));
        var user = await SeedUserAsync(factory);
        var sut = Build(factory);

        await sut.GiveConsentAsync(user.Id, marketingConsent: true);

        await using var db = factory.CreateDbContext();
        var refreshed = await db.Users.FindAsync(user.Id);
        refreshed!.MarketingConsent.Should().BeTrue();
        refreshed.MarketingConsentDate.Should().NotBeNull();
    }

    // ---- ExportUserDataAsync ---------------------------------------------------------------

    [Fact]
    public async Task ExportUserDataAsync_UnknownUser_Throws()
    {
        var factory = CreateFactory(nameof(ExportUserDataAsync_UnknownUser_Throws));
        var sut = Build(factory);

        var act = async () => await sut.ExportUserDataAsync(999);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*nicht gefunden*");
    }

    [Fact]
    public async Task ExportUserDataAsync_IncludesUserWarehouseAndAuditsExport()
    {
        var factory = CreateFactory(nameof(ExportUserDataAsync_IncludesUserWarehouseAndAuditsExport));
        var user = await SeedUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLogs.Add(new AuditLog { UserId = user.Id, Action = "LOGIN_SUCCESS", Timestamp = DateTime.UtcNow, IpAddress = "1.2.3.4" });
            await db.SaveChangesAsync();
        }
        var audit = Substitute.For<IAuditService>();
        var sut = Build(factory, audit);

        var export = await sut.ExportUserDataAsync(user.Id);

        export.Should().NotBeNull();
        export.ExportDate.Should().BeOnOrBefore(DateTime.UtcNow);
        var json = export.ToJson();
        json.Should().Contain(user.Username).And.Contain("WH").And.Contain("LOGIN_SUCCESS");

        await audit.Received(1).LogAsync("GDPR_EXPORT", "User", user.Id, null, AuditSeverity.Info, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportUserDataAsync_UserWithoutStockMovementsOrLogs_ReturnsEmptyCollections()
    {
        var factory = CreateFactory(nameof(ExportUserDataAsync_UserWithoutStockMovementsOrLogs_ReturnsEmptyCollections));
        var user = await SeedUserAsync(factory);
        var sut = Build(factory);

        var export = await sut.ExportUserDataAsync(user.Id);

        export.ToJson().Should().Contain("\"StockMovements\": []").And.Contain("\"AuditLogs\": []");
    }

    // ---- DeleteUserAccountAsync (soft delete) -----------------------------------------------

    [Fact]
    public async Task DeleteUserAccountAsync_UnknownUser_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(DeleteUserAccountAsync_UnknownUser_ReturnsFalse));
        var sut = Build(factory);

        (await sut.DeleteUserAccountAsync(999, "reason")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteUserAccountAsync_SoftDelete_AnonymizesAndDeactivatesUser()
    {
        var factory = CreateFactory(nameof(DeleteUserAccountAsync_SoftDelete_AnonymizesAndDeactivatesUser));
        var user = await SeedUserAsync(factory);
        var originalUsername = user.Username;
        var audit = Substitute.For<IAuditService>();
        var sut = Build(factory, audit);

        var ok = await sut.DeleteUserAccountAsync(user.Id, "GDPR erasure request", hardDelete: false);

        ok.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        var refreshed = await db.Users.FindAsync(user.Id);
        refreshed.Should().NotBeNull("soft delete must keep the row, not remove it");
        refreshed!.IsDeleted.Should().BeTrue();
        refreshed.IsActive.Should().BeFalse();
        refreshed.DeletedAt.Should().NotBeNull();
        refreshed.DeletionReason.Should().Be("GDPR erasure request");
        refreshed.Email.Should().Be($"deleted_{user.Id}@anonymized.local");
        refreshed.DisplayName.Should().NotBe(originalUsername);
        refreshed.PasswordHash.Should().BeEmpty("password hash must be scrubbed on erasure");
        refreshed.LastLoginIp.Should().BeNull();
        refreshed.Username.Should().Be(originalUsername, "username itself is left intact by the soft-delete path");

        await audit.Received(1).LogAsync("GDPR_SOFT_DELETE", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Warning, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteUserAccountAsync_HardDelete_RemovesRowEntirely()
    {
        var factory = CreateFactory(nameof(DeleteUserAccountAsync_HardDelete_RemovesRowEntirely));
        var user = await SeedUserAsync(factory);
        var audit = Substitute.For<IAuditService>();
        var sut = Build(factory, audit);

        var ok = await sut.DeleteUserAccountAsync(user.Id, "test cleanup", hardDelete: true);

        ok.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        (await db.Users.FindAsync(user.Id)).Should().BeNull("hard delete must remove the row completely");

        await audit.Received(1).LogAsync("GDPR_HARD_DELETE", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Critical, Arg.Any<CancellationToken>());
    }

    // ---- AnonymizeUserDataAsync -------------------------------------------------------------

    [Fact]
    public async Task AnonymizeUserDataAsync_UnknownUser_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(AnonymizeUserDataAsync_UnknownUser_ReturnsFalse));
        var sut = Build(factory);

        (await sut.AnonymizeUserDataAsync(999)).Should().BeFalse();
    }

    [Fact]
    public async Task AnonymizeUserDataAsync_ScrubsUserAndTheirAuditLogsAndAudits()
    {
        var factory = CreateFactory(nameof(AnonymizeUserDataAsync_ScrubsUserAndTheirAuditLogsAndAudits));
        var user = await SeedUserAsync(factory);
        var otherUser = await SeedUserAsync(factory, id: 2);
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLogs.Add(new AuditLog { UserId = user.Id, Action = "LOGIN", Timestamp = DateTime.UtcNow, IpAddress = "1.2.3.4", UserAgent = "RealBrowser/1.0" });
            db.AuditLogs.Add(new AuditLog { UserId = otherUser.Id, Action = "LOGIN", Timestamp = DateTime.UtcNow, IpAddress = "5.6.7.8", UserAgent = "OtherBrowser/1.0" });
            await db.SaveChangesAsync();
        }
        var audit = Substitute.For<IAuditService>();
        var sut = Build(factory, audit);

        var ok = await sut.AnonymizeUserDataAsync(user.Id);

        ok.Should().BeTrue();
        await using var db2 = factory.CreateDbContext();
        var refreshedUser = await db2.Users.FindAsync(user.Id);
        refreshedUser!.Email.Should().Be($"anonymized_{user.Id}@local");
        refreshedUser.DisplayName.Should().Be("Anonymisierter Benutzer");
        refreshedUser.LastLoginIp.Should().BeNull();

        var ownLog = await db2.AuditLogs.SingleAsync(l => l.UserId == user.Id);
        ownLog.IpAddress.Should().Be("xxx.xxx.xxx.xxx");
        ownLog.UserAgent.Should().Be("Anonymized");

        var otherLog = await db2.AuditLogs.SingleAsync(l => l.UserId == otherUser.Id);
        otherLog.IpAddress.Should().Be("5.6.7.8", "another user's audit logs must not be touched");

        await audit.Received(1).LogAsync("GDPR_ANONYMIZE", "User", user.Id, null, AuditSeverity.Info, Arg.Any<CancellationToken>());
    }

    // ---- GetInactiveUsersAsync ---------------------------------------------------------------

    [Fact]
    public async Task GetInactiveUsersAsync_ReturnsOnlyActiveNonDeletedUsersPastCutoff()
    {
        var factory = CreateFactory(nameof(GetInactiveUsersAsync_ReturnsOnlyActiveNonDeletedUsersPastCutoff));
        await SeedUserAsync(factory, id: 1, mutate: u => u.LastLoginAt = DateTime.UtcNow.AddDays(-400)); // inactive-eligible
        await SeedUserAsync(factory, id: 2, mutate: u => u.LastLoginAt = DateTime.UtcNow.AddDays(-10));  // recently active
        await SeedUserAsync(factory, id: 3, mutate: u => { u.LastLoginAt = DateTime.UtcNow.AddDays(-400); u.IsActive = false; }); // already inactive
        await SeedUserAsync(factory, id: 4, mutate: u => { u.LastLoginAt = DateTime.UtcNow.AddDays(-400); u.IsDeleted = true; }); // deleted
        var sut = Build(factory);

        var result = await sut.GetInactiveUsersAsync(daysInactive: 365);

        result.Should().ContainSingle().Which.Id.Should().Be(1);
    }

    [Fact]
    public async Task GetInactiveUsersAsync_UsesProvidedThreshold()
    {
        var factory = CreateFactory(nameof(GetInactiveUsersAsync_UsesProvidedThreshold));
        await SeedUserAsync(factory, id: 1, mutate: u => u.LastLoginAt = DateTime.UtcNow.AddDays(-40));
        var sut = Build(factory);

        (await sut.GetInactiveUsersAsync(daysInactive: 30)).Should().ContainSingle();
        (await sut.GetInactiveUsersAsync(daysInactive: 90)).Should().BeEmpty();
    }

    // ---- UserDataExport.ToJson --------------------------------------------------------------

    [Fact]
    public void UserDataExport_ToJson_ProducesIndentedJson()
    {
        var export = new UserDataExport
        {
            ExportDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            User = new { Name = "Alice" },
            StockMovements = Array.Empty<object>(),
            AuditLogs = Array.Empty<object>(),
            GdprInfo = new { Consent = true }
        };

        var json = export.ToJson();

        json.Should().Contain("Alice").And.Contain("\n", "WriteIndented must produce multi-line output");
    }
}
