using FsCheck.Xunit;
using LagersystemLVHome.Application.Utilities;

namespace LagersystemLVHome.UnitTests.Utilities;

/// <summary>
/// Property-based tests (FsCheck) for the IP pattern matcher behind the per-user IP allow-lists.
/// The example table next door checks a handful of hand-picked addresses; these properties run
/// every rule against hundreds of generated addresses, octets and prefix lengths - including the
/// combinations nobody writes into an example table.
/// </summary>
public class IpPatternMatcherPropertyTests
{
    [Property(MaxTest = 500)]
    public bool Matching_never_throws_whatever_the_input(string? ipAddress, string? pattern)
    {
        // An exception here would turn one malformed allow-list entry into a 500 on every request.
        IpPatternMatcher.Matches(ipAddress!, pattern!);
        IpPatternMatcher.MatchesAny(ipAddress!, pattern is null ? [] : [pattern]);
        IpPatternMatcher.IsPrivateIP(ipAddress!);
        return true;
    }

    [Property(MaxTest = 300)]
    public bool Every_address_matches_itself_and_every_wildcard_form_that_contains_it(byte a, byte b, byte c, byte d)
    {
        var ip = $"{a}.{b}.{c}.{d}";
        return IpPatternMatcher.Matches(ip, ip)
            && IpPatternMatcher.Matches(ip, $"{a}.{b}.{c}.*")
            && IpPatternMatcher.Matches(ip, $"{a}.{b}.*.*")
            && IpPatternMatcher.Matches(ip, "*.*.*.*")
            && IpPatternMatcher.Matches(ip, $"{ip}/32");
    }

    [Property(MaxTest = 300)]
    public bool A_two_octet_prefix_pattern_matches_exactly_the_addresses_sharing_the_prefix(byte a, byte b, byte c, byte d, byte x, byte y)
    {
        var matches = IpPatternMatcher.Matches($"{x}.{y}.{c}.{d}", $"{a}.{b}.*.*");
        return matches == (x == a && y == b);
    }

    [Property(MaxTest = 300)]
    public bool A_range_octet_accepts_exactly_the_values_inside_the_range(byte low, byte high, byte value)
    {
        if (low > high) (low, high) = (high, low);
        var matches = IpPatternMatcher.Matches($"10.0.0.{value}", $"10.0.0.{low}-{high}");
        return matches == (value >= low && value <= high);
    }

    [Property(MaxTest = 500)]
    public bool A_cidr_pattern_agrees_with_the_bit_mask_it_denotes(byte a, byte b, byte c, byte d, byte x, byte y, byte z, byte w, byte prefixSeed)
    {
        var prefix = prefixSeed % 33;
        var network = (uint)((a << 24) | (b << 16) | (c << 8) | d);
        var address = (uint)((x << 24) | (y << 16) | (z << 8) | w);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        var expected = (address & mask) == (network & mask);
        return IpPatternMatcher.Matches($"{x}.{y}.{z}.{w}", $"{a}.{b}.{c}.{d}/{prefix}") == expected;
    }

    [Property(MaxTest = 500)]
    public bool IsPrivateIP_agrees_with_the_private_loopback_and_link_local_ranges(byte a, byte b, byte c, byte d)
    {
        var expected = a == 10
            || a == 127
            || (a == 172 && b >= 16 && b <= 31)
            || (a == 192 && b == 168)
            || (a == 169 && b == 254);
        return IpPatternMatcher.IsPrivateIP($"{a}.{b}.{c}.{d}") == expected;
    }
}
