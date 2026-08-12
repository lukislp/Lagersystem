namespace LagersystemLVHome.Application.Configuration;

/// <summary>
/// Configuration for VPN detection based on IP subnets.
/// </summary>
public class VpnDetectionConfig
{
    /// <summary>
    /// List of IP patterns for known VPN subnets.
    /// Example: "192.168.3.*", "10.0.5.*", "172.16.*.*"
    /// * = wildcard for any octet.
    /// </summary>
    public List<string> VpnSubnets { get; set; } = new();

    /// <summary>
    /// Confidence score for subnet-based detection (0-100).
    /// Default: 95 (very reliable since manually configured).
    /// </summary>
    public int SubnetMatchConfidence { get; set; } = 95;
}
