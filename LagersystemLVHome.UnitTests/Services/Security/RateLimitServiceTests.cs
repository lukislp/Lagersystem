using LagersystemLVHome.Application.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.UnitTests.Services.Security;

public class RateLimitServiceTests
{
    private static RateLimitService Build(RateLimitSettings settings)
        => new(Options.Create(settings), NullLogger<RateLimitService>.Instance);

    [Fact]
    public async Task CheckRateLimitAsync_Disabled_AlwaysSucceeds()
    {
        using var sut = Build(new RateLimitSettings { Enabled = false });

        var result = await sut.CheckRateLimitAsync("1.2.3.4", "/api/x");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRateLimitAsync_Whitelisted_AlwaysSucceeds()
    {
        using var sut = Build(new RateLimitSettings { WhitelistedIPs = { "9.9.9.9" } });

        var result = await sut.CheckRateLimitAsync("9.9.9.9", "/api/x");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRateLimitAsync_Blacklisted_IsBlocked()
    {
        using var sut = Build(new RateLimitSettings { BlacklistedIPs = { "6.6.6.6" } });

        var result = await sut.CheckRateLimitAsync("6.6.6.6", "/api/x");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("blacklist", because: "blacklisted message should mention reason")
            .And.NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckRateLimitAsync_AllowsBelowLimit()
    {
        using var sut = Build(new RateLimitSettings
        {
            Anonymous = new RateLimitPolicy { PermitLimit = 3, Window = TimeSpan.FromMinutes(1) }
        });

        var first = await sut.CheckRateLimitAsync("1.1.1.1", "/api/x");
        var second = await sut.CheckRateLimitAsync("1.1.1.1", "/api/x");

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ResetLimitAsync_RemovesBucketsForIdentifier()
    {
        using var sut = Build(new RateLimitSettings());
        await sut.CheckRateLimitAsync("2.2.2.2", "/api/a");
        await sut.CheckRateLimitAsync("2.2.2.2", "/api/b");

        sut.GetActiveBucketsCount().Should().BeGreaterOrEqualTo(2);

        await sut.ResetLimitAsync("2.2.2.2");

        sut.GetActiveBucketsCount().Should().Be(0);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsBucketsForIdentifier()
    {
        using var sut = Build(new RateLimitSettings());
        await sut.CheckRateLimitAsync("3.3.3.3", "/api/foo");

        var stats = await sut.GetStatsAsync("3.3.3.3");

        stats.Identifier.Should().Be("3.3.3.3");
        stats.Buckets.Should().ContainSingle().Which.Endpoint.Should().Be("/api/foo");
    }

    [Fact]
    public async Task GetAllBuckets_IncludesActiveBuckets()
    {
        using var sut = Build(new RateLimitSettings());
        await sut.CheckRateLimitAsync("4.4.4.4", "/api/bar");

        var all = sut.GetAllBuckets();

        all.Should().Contain(b => b.Identifier == "4.4.4.4" && b.Endpoint == "/api/bar");
    }

    [Fact]
    public void RateLimitResult_FactoryMethods_ProduceExpectedShape()
    {
        RateLimitResult.CreateSuccess(5).IsSuccess.Should().BeTrue();
        RateLimitResult.CreateExceeded(TimeSpan.FromSeconds(1)).IsSuccess.Should().BeFalse();
        RateLimitResult.CreateBlocked("nope").Message.Should().Be("nope");
    }
}
