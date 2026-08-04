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
}
