using LagersystemLVHome.Application.Utilities;

namespace LagersystemLVHome.UnitTests.Utilities;

public class IpPatternMatcherTests
{
    [Theory]
    [InlineData("192.168.3.45", "192.168.3.45", true)]
    [InlineData("192.168.3.45", "192.168.3.*", true)]
    [InlineData("192.168.3.45", "192.168.*.*", true)]
    [InlineData("192.168.3.45", "*.*.*.*", true)]
    [InlineData("10.0.0.1", "192.168.*.*", false)]
    [InlineData("192.168.3.45", "192.168.4.*", false)]
    public void Matches_IPv4Wildcard(string ip, string pattern, bool expected)
        => IpPatternMatcher.Matches(ip, pattern).Should().Be(expected);

    [Theory]
    [InlineData("192.168.3.45", "192.168.3.40-50", true)]
    [InlineData("192.168.3.39", "192.168.3.40-50", false)]
    [InlineData("192.168.3.51", "192.168.3.40-50", false)]
    public void Matches_IPv4Range(string ip, string pattern, bool expected)
        => IpPatternMatcher.Matches(ip, pattern).Should().Be(expected);

    [Theory]
    [InlineData("192.168.3.45", "192.168.3.0/24", true)]
    [InlineData("192.168.4.1", "192.168.3.0/24", false)]
    [InlineData("10.0.0.5", "10.0.0.0/8", true)]
    [InlineData("11.0.0.5", "10.0.0.0/8", false)]
    [InlineData("192.168.3.130", "192.168.3.128/25", true)]
    [InlineData("192.168.3.127", "192.168.3.128/25", false)]
    public void Matches_IPv4Cidr(string ip, string pattern, bool expected)
        => IpPatternMatcher.Matches(ip, pattern).Should().Be(expected);

    [Theory]
    [InlineData("", "192.168.*.*", false)]
    [InlineData("192.168.3.45", "", false)]
    [InlineData("notanip", "192.168.*.*", false)]
    [InlineData("192.168.3.45", "garbage", false)]
    public void Matches_InvalidInput_ReturnsFalse(string ip, string pattern, bool expected)
        => IpPatternMatcher.Matches(ip, pattern).Should().Be(expected);

    [Fact]
    public void MatchesAny_ReturnsTrue_WhenAtLeastOnePatternMatches()
    {
        var patterns = new[] { "10.0.0.0/8", "192.168.3.*" };
        IpPatternMatcher.MatchesAny("192.168.3.45", patterns).Should().BeTrue();
        IpPatternMatcher.MatchesAny("172.16.0.1", patterns).Should().BeFalse();
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("localhost", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.16.0.5", true)]
    [InlineData("172.32.0.5", false)]
    [InlineData("192.168.3.45", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("", false)]
    public void IsPrivateIP_DetectsCommonRanges(string ip, bool expected)
        => IpPatternMatcher.IsPrivateIP(ip).Should().Be(expected);

    [Fact]
    public void Matches_IPv6_ExactMatchIsCaseInsensitive()
    {
        IpPatternMatcher.Matches("FE80::1", "fe80::1").Should().BeTrue();
    }

    [Fact]
    public void Matches_IPv6_WildcardPrefix()
    {
        IpPatternMatcher.Matches("fe80::abcd", "fe80:*").Should().BeTrue();
        IpPatternMatcher.Matches("2001::1", "fe80:*").Should().BeFalse();
    }
}
