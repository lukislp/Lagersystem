using LagersystemLVHome.Application.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.UnitTests.Services.Cache;

public class CacheServiceTests
{
    private sealed class Box { public string Value { get; set; } = ""; }

    private static (CacheService sut, IMemoryCache mem) Build(bool enableMemory = true)
    {
        var settings = new CacheSettings
        {
            EnableMemoryCache = enableMemory,
            EnableDistributedCache = false,
            DefaultExpirationMinutes = 5,
            SlidingExpirationMinutes = 1
        };
        var mem = new MemoryCache(Options.Create(new MemoryCacheOptions { SizeLimit = 100 }));
        var sut = new CacheService(settings, NullLogger<CacheService>.Instance, mem, distributedCache: null);
        return (sut, mem);
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsCachedValue()
    {
        var (sut, _) = Build();

        await sut.SetAsync("k1", new Box { Value = "v" });
        var loaded = await sut.GetAsync<Box>("k1");

        loaded.Should().NotBeNull();
        loaded!.Value.Should().Be("v");
    }

    [Fact]
    public async Task GetAsync_Miss_ReturnsNull()
    {
        var (sut, _) = Build();

        var loaded = await sut.GetAsync<Box>("missing");

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateAsync_CreatesValueOnce()
    {
        var (sut, _) = Build();
        var calls = 0;

        var v1 = await sut.GetOrCreateAsync("k", TimeSpan.FromMinutes(5), () =>
        {
            calls++;
            return Task.FromResult(new Box { Value = "factory" });
        });
        var v2 = await sut.GetOrCreateAsync("k", TimeSpan.FromMinutes(5), () =>
        {
            calls++;
            return Task.FromResult(new Box { Value = "should-not-run" });
        });

        calls.Should().Be(1);
        v1.Value.Should().Be("factory");
        v2.Value.Should().Be("factory");
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntry()
    {
        var (sut, mem) = Build();
        await sut.SetAsync("k", new Box { Value = "v" });

        await sut.RemoveAsync("k");

        mem.TryGetValue("k", out _).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_DoesNotThrow()
    {
        var (sut, _) = Build();
        var act = () => sut.RemoveByPrefixAsync("anything:");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetAsync_MemoryCacheDisabled_DoesNothing()
    {
        var (sut, mem) = Build(enableMemory: false);

        await sut.SetAsync("k", new Box { Value = "v" });

        mem.TryGetValue("k", out _).Should().BeFalse();
    }
}
