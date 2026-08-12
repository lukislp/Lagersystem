namespace LagersystemLVHome.Application.Utilities;

/// <summary>
/// Helper class for IP pattern matching with wildcard support.
/// Supports patterns like "192.168.3.*" or "10.0.*.*".
/// </summary>
public static class IpPatternMatcher
{
    /// <param name="ipAddress">The IP address to check (e.g. "192.168.3.45").</param>
    /// <param name="pattern">The pattern with wildcards (e.g. "192.168.3.*").</param>
    /// <returns>True if the IP matches the pattern.</returns>
    public static bool Matches(string ipAddress, string pattern)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || string.IsNullOrWhiteSpace(pattern))
            return false;

        ipAddress = ipAddress.Trim();
        pattern = pattern.Trim();

        if (ipAddress.Contains(':') || pattern.Contains(':'))
        {
            return MatchesIPv6(ipAddress, pattern);
        }

        return MatchesIPv4(ipAddress, pattern);
    }

    /// <summary>
    /// Checks whether an IP address matches any of the given patterns.
    /// </summary>
    public static bool MatchesAny(string ipAddress, IEnumerable<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || patterns == null)
            return false;

        return patterns.Any(pattern => Matches(ipAddress, pattern));
    }

    private static bool MatchesIPv4(string ipAddress, string pattern)
    {
        var ipParts = ipAddress.Split('.');
        var patternParts = pattern.Split('.');

        if (ipParts.Length != 4 || patternParts.Length != 4)
            return false;

        for (int i = 0; i < 4; i++)
        {
            if (patternParts[i] == "*")
                continue;

            if (patternParts[i].Contains('/'))
            {
                return MatchesCIDR(ipAddress, pattern);
            }

            if (patternParts[i].Contains('-'))
            {
                if (!MatchesRange(ipParts[i], patternParts[i]))
                    return false;
                continue;
            }

            if (ipParts[i] != patternParts[i])
                return false;
        }

        return true;
    }

    private static bool MatchesIPv6(string ipAddress, string pattern)
    {
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern.TrimEnd('*', ':');
            return ipAddress.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return ipAddress.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesRange(string ipPart, string rangePart)
    {
        var rangeParts = rangePart.Split('-');
        if (rangeParts.Length != 2)
            return false;

        if (!int.TryParse(ipPart, out var value))
            return false;

        if (!int.TryParse(rangeParts[0], out var min))
            return false;

        if (!int.TryParse(rangeParts[1], out var max))
            return false;

        return value >= min && value <= max;
    }

    private static bool MatchesCIDR(string ipAddress, string cidrPattern)
    {
        try
        {
            var parts = cidrPattern.Split('/');
            if (parts.Length != 2)
                return false;

            var networkAddress = parts[0];
            if (!int.TryParse(parts[1], out var prefixLength))
                return false;

            var ipParts = ipAddress.Split('.');
            var networkParts = networkAddress.Split('.');

            if (ipParts.Length != 4 || networkParts.Length != 4)
                return false;

            int fullOctets = prefixLength / 8;
            int remainingBits = prefixLength % 8;

            for (int i = 0; i < fullOctets && i < 4; i++)
            {
                if (ipParts[i] != networkParts[i])
                    return false;
            }

            if (remainingBits > 0 && fullOctets < 4)
            {
                if (!byte.TryParse(ipParts[fullOctets], out var ipByte))
                    return false;
                if (!byte.TryParse(networkParts[fullOctets], out var networkByte))
                    return false;

                int mask = (0xFF << (8 - remainingBits)) & 0xFF;
                if ((ipByte & mask) != (networkByte & mask))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsPrivateIP(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return false;

        // Localhost
        if (ipAddress == "127.0.0.1" || ipAddress == "::1" || ipAddress == "localhost")
            return true;

        // Private IPv4 ranges
        var privateRanges = new[]
        {
            "10.*.*.*",       // 10.0.0.0/8
            "172.16.*.*",     // 172.16.0.0/12
            "172.17.*.*",
            "172.18.*.*",
            "172.19.*.*",
            "172.20.*.*",
            "172.21.*.*",
            "172.22.*.*",
            "172.23.*.*",
            "172.24.*.*",
            "172.25.*.*",
            "172.26.*.*",
            "172.27.*.*",
            "172.28.*.*",
            "172.29.*.*",
            "172.30.*.*",
            "172.31.*.*",
            "192.168.*.*",    // 192.168.0.0/16
            "169.254.*.*"     // Link-local
        };

        return MatchesAny(ipAddress, privateRanges);
    }
}
