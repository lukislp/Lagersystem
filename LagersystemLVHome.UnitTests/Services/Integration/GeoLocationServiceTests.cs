using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Integration;

public class GeoLocationServiceTests
{
    private static GeoLocationService BuildSut()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GeoIP:DatabasePath"] = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.mmdb")
            })
            .Build();

        return new GeoLocationService(NullLogger<GeoLocationService>.Instance, config);
    }

    [Fact]
    public void IsAvailable_WhenDatabaseMissing_ReturnsFalse()
    {
        using var sut = BuildSut();
        sut.IsAvailable.Should().BeFalse();
    }

    [Theory]
    [InlineData("localhost-ipv6")]
    [InlineData("localhost-ipv4")]
    [InlineData("::1")]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.42")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("Unknown")]
    [InlineData("")]
    public async Task GetLocationFromIpAsync_PrivateOrLocalhost_ReturnsGermany(string ip)
    {
        using var sut = BuildSut();

        var result = await sut.GetLocationFromIpAsync(ip);

        result.IsSuccess.Should().BeTrue();
        result.Country.Should().Be("Germany");
        result.IsoCode.Should().Be("DE");
    }

    [Fact]
    public async Task GetLocationFromIpAsync_PublicIp_WithoutDatabase_ReturnsFailure()
    {
        using var sut = BuildSut();

        var result = await sut.GetLocationFromIpAsync("8.8.8.8");

        // No GeoIP database available: report failure rather than a
        // fabricated location, so callers skip geolocation-dependent
        // checks instead of acting on made-up data.
        result.IsSuccess.Should().BeFalse();
        result.Country.Should().BeNull();
    }

    [Fact]
    public void Dispose_DoesNotThrow_WhenDatabaseUnavailable()
    {
        var sut = BuildSut();
        var act = () => sut.Dispose();
        act.Should().NotThrow();
    }
}
