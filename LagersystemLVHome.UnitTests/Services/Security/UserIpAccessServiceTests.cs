using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Security;

public class UserIpAccessServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task SeedUserAsync(IDbContextFactory<InventoryDbContext> factory, int userId, bool ipRestrictions)
    {
        await using var db = factory.CreateDbContext();
        db.Users.Add(new User
        {
            Id = userId,
            Username = $"u{userId}",
            Email = $"u{userId}@x",
            PasswordHash = "h",
            IsActive = true,
            IpRestrictionsEnabled = ipRestrictions
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedRuleAsync(IDbContextFactory<InventoryDbContext> factory, int userId, string pattern, bool isAllowed, int priority = 10)
    {
        await using var db = factory.CreateDbContext();
        db.UserIpAccessRules.Add(new UserIpAccessRule
        {
            UserId = userId,
            IpPattern = pattern,
            IsAllowed = isAllowed,
            Priority = priority,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private static UserIpAccessService Build(IDbContextFactory<InventoryDbContext> factory)
        => new(factory, NullLogger<UserIpAccessService>.Instance);

    [Fact]
    public async Task CheckAccessAsync_RestrictionsDisabled_NotRestricted()
    {
        var factory = CreateFactory(nameof(CheckAccessAsync_RestrictionsDisabled_NotRestricted));
        await SeedUserAsync(factory, 1, ipRestrictions: false);
        var sut = Build(factory);

        var result = await sut.CheckAccessAsync(1, "10.0.0.1");

        result.IsAllowed.Should().BeTrue();
        result.RestrictionsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAccessAsync_NoRules_AllowsAccess()
    {
        var factory = CreateFactory(nameof(CheckAccessAsync_NoRules_AllowsAccess));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        var sut = Build(factory);

        var result = await sut.CheckAccessAsync(1, "10.0.0.1");

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAccessAsync_BlacklistMatch_Denies()
    {
        var factory = CreateFactory(nameof(CheckAccessAsync_BlacklistMatch_Denies));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        await SeedRuleAsync(factory, 1, "10.0.0.5", isAllowed: false, priority: 20);
        var sut = Build(factory);

        var result = await sut.CheckAccessAsync(1, "10.0.0.5");

        result.IsAllowed.Should().BeFalse();
        result.Message.Should().Contain("10.0.0.5");
    }

    [Fact]
    public async Task CheckAccessAsync_WhitelistMatch_Allows()
    {
        var factory = CreateFactory(nameof(CheckAccessAsync_WhitelistMatch_Allows));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        await SeedRuleAsync(factory, 1, "192.168.1.*", isAllowed: true);
        var sut = Build(factory);

        var result = await sut.CheckAccessAsync(1, "192.168.1.42");

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAccessAsync_WhitelistRulesPresent_NoMatch_Denies()
    {
        var factory = CreateFactory(nameof(CheckAccessAsync_WhitelistRulesPresent_NoMatch_Denies));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        await SeedRuleAsync(factory, 1, "192.168.1.*", isAllowed: true);
        var sut = Build(factory);

        var result = await sut.CheckAccessAsync(1, "10.0.0.99");

        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task GetRulesAsync_ReturnsRulesOrderedByPriorityDesc()
    {
        var factory = CreateFactory(nameof(GetRulesAsync_ReturnsRulesOrderedByPriorityDesc));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        await SeedRuleAsync(factory, 1, "10.0.0.1", isAllowed: true, priority: 1);
        await SeedRuleAsync(factory, 1, "10.0.0.2", isAllowed: false, priority: 50);
        var sut = Build(factory);

        var rules = await sut.GetRulesAsync(1);

        rules.Should().HaveCount(2);
        rules[0].Priority.Should().Be(50);
    }

    [Fact]
    public async Task AddRuleAsync_ValidPattern_PersistsRule()
    {
        var factory = CreateFactory(nameof(AddRuleAsync_ValidPattern_PersistsRule));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        var sut = Build(factory);

        var rule = await sut.AddRuleAsync(1, "10.0.0.0/24", "Office", isAllowed: true);

        rule.Should().NotBeNull();
        rule!.IpPattern.Should().Be("10.0.0.0/24");
        rule.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task AddRuleAsync_InvalidPattern_ReturnsNull()
    {
        var factory = CreateFactory(nameof(AddRuleAsync_InvalidPattern_ReturnsNull));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        var sut = Build(factory);

        var rule = await sut.AddRuleAsync(1, "not-an-ip", null, isAllowed: true);

        rule.Should().BeNull();
    }

    [Fact]
    public void IpAccessCheckResult_FactoryMethods_HaveExpectedDefaults()
    {
        IpAccessCheckResult.Allowed("r").IsAllowed.Should().BeTrue();
        IpAccessCheckResult.Denied("nope").IsAllowed.Should().BeFalse();
        IpAccessCheckResult.NotRestricted().RestrictionsEnabled.Should().BeFalse();
    }

    // ---- CheckAccessAsync: CIDR matching ----------------------------------------------------

    [Theory]
    [InlineData("192.168.1.0/24", "192.168.1.42", true)]
    [InlineData("192.168.1.0/24", "192.168.2.1", false)]
    [InlineData("10.0.0.0/8", "10.255.255.255", true)]
    [InlineData("10.0.0.0/8", "11.0.0.1", false)]
    [InlineData("192.168.1.128/25", "192.168.1.200", true)]
    [InlineData("192.168.1.128/25", "192.168.1.100", false)]
    public async Task CheckAccessAsync_CidrPattern_MatchesExpectedRange(string cidr, string testIp, bool expectAllowed)
    {
        var factory = CreateFactory($"{nameof(CheckAccessAsync_CidrPattern_MatchesExpectedRange)}_{cidr}_{testIp}".Replace('/', '_').Replace('.', '_'));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        await SeedRuleAsync(factory, 1, cidr, isAllowed: true);
        var sut = Build(factory);

        var result = await sut.CheckAccessAsync(1, testIp);

        result.IsAllowed.Should().Be(expectAllowed);
    }

    // ---- CheckAccessAsync: audit logging on denial --------------------------------------------

    [Fact]
    public async Task CheckAccessAsync_BlacklistMatch_LogsAuditWarningWithMatchedRule()
    {
        var factory = CreateFactory(nameof(CheckAccessAsync_BlacklistMatch_LogsAuditWarningWithMatchedRule));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        await SeedRuleAsync(factory, 1, "10.0.0.5", isAllowed: false, priority: 20);
        var audit = Substitute.For<IAuditService>();
        var sut = new UserIpAccessService(factory, NullLogger<UserIpAccessService>.Instance, audit);

        await sut.CheckAccessAsync(1, "10.0.0.5");

        await audit.Received(1).LogAsync("IP_ACCESS_DENIED", "User", 1, Arg.Any<object?>(), AuditSeverity.Warning, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAccessAsync_NotInWhitelist_LogsAuditWarning()
    {
        var factory = CreateFactory(nameof(CheckAccessAsync_NotInWhitelist_LogsAuditWarning));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        await SeedRuleAsync(factory, 1, "192.168.1.*", isAllowed: true);
        var audit = Substitute.For<IAuditService>();
        var sut = new UserIpAccessService(factory, NullLogger<UserIpAccessService>.Instance, audit);

        await sut.CheckAccessAsync(1, "10.0.0.99");

        await audit.Received(1).LogAsync("IP_ACCESS_DENIED_NOT_WHITELISTED", "User", 1, Arg.Any<object?>(), AuditSeverity.Warning, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAccessAsync_WithoutAuditService_StillDeniesCorrectly()
    {
        var factory = CreateFactory(nameof(CheckAccessAsync_WithoutAuditService_StillDeniesCorrectly));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        await SeedRuleAsync(factory, 1, "10.0.0.5", isAllowed: false, priority: 20);
        var sut = Build(factory); // no audit service

        var result = await sut.CheckAccessAsync(1, "10.0.0.5");

        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAccessAsync_OnlyBlacklistRulesPresentAndNoneMatch_Allows()
    {
        var factory = CreateFactory(nameof(CheckAccessAsync_OnlyBlacklistRulesPresentAndNoneMatch_Allows));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        await SeedRuleAsync(factory, 1, "10.0.0.5", isAllowed: false, priority: 20);
        var sut = Build(factory);

        var result = await sut.CheckAccessAsync(1, "192.168.1.1");

        result.IsAllowed.Should().BeTrue("only blacklist rules exist and this IP isn't blocked by any of them");
    }

    [Fact]
    public async Task CheckAccessAsync_UnknownUser_TreatsAsNotRestricted()
    {
        var factory = CreateFactory(nameof(CheckAccessAsync_UnknownUser_TreatsAsNotRestricted));
        var sut = Build(factory);

        var result = await sut.CheckAccessAsync(999, "1.2.3.4");

        result.IsAllowed.Should().BeTrue();
        result.RestrictionsEnabled.Should().BeFalse();
    }

    /// <summary>
    /// SECURITY: on an internal error (e.g. DB unavailable), CheckAccessAsync explicitly
    /// fails OPEN ("On error: allow access (fail-open for better UX)" per the source comment)
    /// rather than fail-closed. This test documents that this is real, intentional behavior -
    /// not a gap - so it's visible to anyone auditing this code path. Fail-open on an access
    /// control check is a meaningful risk trade-off worth having a directly-visible test for.
    /// </summary>
    [Fact]
    public async Task CheckAccessAsync_ContextFactoryThrows_FailsOpenAndAllowsAccess()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db down")));
        var sut = Build(throwingFactory);

        var result = await sut.CheckAccessAsync(1, "1.2.3.4");

        result.IsAllowed.Should().BeTrue(
            "documented fail-open behavior: CheckAccessAsync explicitly allows access when the underlying check errors out");
    }

    // ---- IsValidIpPattern -----------------------------------------------------------------

    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.1.*", true)]
    [InlineData("10.0.0.0/8", true)]
    [InlineData("10.0.0.0/33", false)] // prefix out of range
    [InlineData("10.0.0.0/-1", false)]
    [InlineData("not-an-ip-or-pattern", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsValidIpPattern_ValidatesVariousFormats(string pattern, bool expected)
    {
        var sut = Build(CreateFactory($"{nameof(IsValidIpPattern_ValidatesVariousFormats)}_{Guid.NewGuid()}"));

        sut.IsValidIpPattern(pattern).Should().Be(expected);
    }

    // ---- AddRuleAsync: audit + priority assignment -----------------------------------------

    [Fact]
    public async Task AddRuleAsync_AllowRule_AssignsLowerPriorityThanDenyRule()
    {
        var factory = CreateFactory(nameof(AddRuleAsync_AllowRule_AssignsLowerPriorityThanDenyRule));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        var sut = Build(factory);

        var allowRule = await sut.AddRuleAsync(1, "10.0.0.1", null, isAllowed: true);
        var denyRule = await sut.AddRuleAsync(1, "10.0.0.2", null, isAllowed: false);

        allowRule!.Priority.Should().Be(10);
        denyRule!.Priority.Should().Be(20, "deny/blacklist rules must take priority over allow rules when evaluated in priority order");
    }

    [Fact]
    public async Task AddRuleAsync_LogsAuditInfo()
    {
        var factory = CreateFactory(nameof(AddRuleAsync_LogsAuditInfo));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        var audit = Substitute.For<IAuditService>();
        var sut = new UserIpAccessService(factory, NullLogger<UserIpAccessService>.Instance, audit);

        await sut.AddRuleAsync(1, "10.0.0.1", "office", isAllowed: true);

        await audit.Received(1).LogAsync("IP_RULE_ADDED", "UserIpAccessRule", Arg.Any<int?>(), Arg.Any<object?>(), AuditSeverity.Info, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddRuleAsync_ContextFactoryThrows_ReturnsNull()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db down")));
        var sut = Build(throwingFactory);

        (await sut.AddRuleAsync(1, "10.0.0.1", null, isAllowed: true)).Should().BeNull();
    }

    // ---- UpdateRuleAsync --------------------------------------------------------------------

    [Fact]
    public async Task UpdateRuleAsync_ExistingRule_UpdatesFieldsAndAudits()
    {
        var factory = CreateFactory(nameof(UpdateRuleAsync_ExistingRule_UpdatesFieldsAndAudits));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        var audit = Substitute.For<IAuditService>();
        var sut = new UserIpAccessService(factory, NullLogger<UserIpAccessService>.Instance, audit);
        var rule = await sut.AddRuleAsync(1, "10.0.0.1", "old desc", isAllowed: true);

        var ok = await sut.UpdateRuleAsync(rule!.Id, "10.0.0.2", "new desc", isAllowed: false, isActive: false, updatedByUserId: 42);

        ok.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        var updated = await db.UserIpAccessRules.FindAsync(rule.Id);
        updated!.IpPattern.Should().Be("10.0.0.2");
        updated.Description.Should().Be("new desc");
        updated.IsAllowed.Should().BeFalse();
        updated.IsActive.Should().BeFalse();
        updated.UpdatedByUserId.Should().Be(42);
        updated.UpdatedAt.Should().NotBeNull();

        await audit.Received(1).LogAsync("IP_RULE_UPDATED", "UserIpAccessRule", rule.Id, Arg.Any<object?>(), AuditSeverity.Info, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateRuleAsync_InvalidPattern_ReturnsFalseWithoutChangingRule()
    {
        var factory = CreateFactory(nameof(UpdateRuleAsync_InvalidPattern_ReturnsFalseWithoutChangingRule));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        var sut = Build(factory);
        var rule = await sut.AddRuleAsync(1, "10.0.0.1", null, isAllowed: true);

        var ok = await sut.UpdateRuleAsync(rule!.Id, "not-valid", null, isAllowed: true, isActive: true);

        ok.Should().BeFalse();
        await using var db = factory.CreateDbContext();
        (await db.UserIpAccessRules.FindAsync(rule.Id))!.IpPattern.Should().Be("10.0.0.1", "an invalid update must not modify the existing rule");
    }

    [Fact]
    public async Task UpdateRuleAsync_UnknownRuleId_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(UpdateRuleAsync_UnknownRuleId_ReturnsFalse));
        var sut = Build(factory);

        (await sut.UpdateRuleAsync(999, "10.0.0.1", null, isAllowed: true, isActive: true)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateRuleAsync_ContextFactoryThrows_ReturnsFalse()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db down")));
        var sut = Build(throwingFactory);

        (await sut.UpdateRuleAsync(1, "10.0.0.1", null, isAllowed: true, isActive: true)).Should().BeFalse();
    }

    // ---- DeleteRuleAsync --------------------------------------------------------------------

    [Fact]
    public async Task DeleteRuleAsync_ExistingRule_RemovesItAndAudits()
    {
        var factory = CreateFactory(nameof(DeleteRuleAsync_ExistingRule_RemovesItAndAudits));
        await SeedUserAsync(factory, 1, ipRestrictions: true);
        var audit = Substitute.For<IAuditService>();
        var sut = new UserIpAccessService(factory, NullLogger<UserIpAccessService>.Instance, audit);
        var rule = await sut.AddRuleAsync(1, "10.0.0.1", null, isAllowed: true);

        var ok = await sut.DeleteRuleAsync(rule!.Id);

        ok.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        (await db.UserIpAccessRules.FindAsync(rule.Id)).Should().BeNull();
        await audit.Received(1).LogAsync("IP_RULE_DELETED", "UserIpAccessRule", rule.Id, Arg.Any<object?>(), AuditSeverity.Info, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteRuleAsync_UnknownRuleId_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(DeleteRuleAsync_UnknownRuleId_ReturnsFalse));
        var sut = Build(factory);

        (await sut.DeleteRuleAsync(999)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteRuleAsync_ContextFactoryThrows_ReturnsFalse()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db down")));
        var sut = Build(throwingFactory);

        (await sut.DeleteRuleAsync(1)).Should().BeFalse();
    }

    // ---- SetIpRestrictionsEnabledAsync ------------------------------------------------------

    [Theory]
    [InlineData(true, "IP_RESTRICTIONS_ENABLED")]
    [InlineData(false, "IP_RESTRICTIONS_DISABLED")]
    public async Task SetIpRestrictionsEnabledAsync_UpdatesFlagAndAuditsCorrectAction(bool enabled, string expectedAction)
    {
        var factory = CreateFactory($"{nameof(SetIpRestrictionsEnabledAsync_UpdatesFlagAndAuditsCorrectAction)}_{enabled}");
        await SeedUserAsync(factory, 1, ipRestrictions: !enabled);
        var audit = Substitute.For<IAuditService>();
        var sut = new UserIpAccessService(factory, NullLogger<UserIpAccessService>.Instance, audit);

        var ok = await sut.SetIpRestrictionsEnabledAsync(1, enabled);

        ok.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        (await db.Users.FindAsync(1))!.IpRestrictionsEnabled.Should().Be(enabled);
        await audit.Received(1).LogAsync(expectedAction, "User", 1, null, AuditSeverity.Info, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetIpRestrictionsEnabledAsync_UnknownUser_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(SetIpRestrictionsEnabledAsync_UnknownUser_ReturnsFalse));
        var sut = Build(factory);

        (await sut.SetIpRestrictionsEnabledAsync(999, true)).Should().BeFalse();
    }

    [Fact]
    public async Task SetIpRestrictionsEnabledAsync_ContextFactoryThrows_ReturnsFalse()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db down")));
        var sut = Build(throwingFactory);

        (await sut.SetIpRestrictionsEnabledAsync(1, true)).Should().BeFalse();
    }
}
