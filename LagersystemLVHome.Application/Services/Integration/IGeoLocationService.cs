using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// IP-based geolocation service using MaxMind GeoIP2.
/// </summary>
public interface IGeoLocationService
{
    Task<GeoLocationResult> GetLocationFromIpAsync(string ipAddress, CancellationToken cancellationToken = default);
    bool IsAvailable { get; }
}
