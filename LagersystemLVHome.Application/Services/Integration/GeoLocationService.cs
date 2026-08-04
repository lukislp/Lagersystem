using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;

namespace LagersystemLVHome.Application.Services;

public sealed class GeoLocationResult
{
    public string? Country { get; set; }
    public string? City { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? IsoCode { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class GeoLocationService : IGeoLocationService, IDisposable
{
    private readonly ILogger<GeoLocationService> _logger;
    private readonly DatabaseReader? _cityReader;
    private readonly bool _isAvailable;

    public bool IsAvailable => _isAvailable;

    public GeoLocationService(ILogger<GeoLocationService> logger, IConfiguration configuration)
    {
        _logger = logger;

        try
        {
            // Path to GeoLite2-City.mmdb file from appsettings.json
            var databasePath = configuration["GeoIP:DatabasePath"] ??
                Path.Combine(AppContext.BaseDirectory, "GeoData", "GeoLite2-City.mmdb");

            if (File.Exists(databasePath))
            {
                _cityReader = new DatabaseReader(databasePath);
                _isAvailable = true;
                _logger.LogInformation("GeoIP2 Database loaded: {Path}", databasePath);
            }
            else
            {
                _logger.LogWarning("GeoIP2 Database not found: {Path}", databasePath);
                _logger.LogWarning("  IP anonymization will use default values");
                _isAvailable = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading GeoIP2 Database");
            _isAvailable = false;
        }
    }

    public async Task<GeoLocationResult> GetLocationFromIpAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        // Handle special localhost strings from RateLimitMiddleware before any processing
        if (ipAddress == "localhost-ipv6") ipAddress = "::1";
        if (ipAddress == "localhost-ipv4") ipAddress = "127.0.0.1";

        // Localhost and private IPs default to Germany
        if (IsPrivateOrLocalhost(ipAddress))
        {
            return new GeoLocationResult
            {
                Country = "Germany",
                City = null,
                IsoCode = "DE",
                IsSuccess = true
            };
        }

        // If GeoIP2 is unavailable, fall back to hash-based simulation
        if (!_isAvailable || _cityReader == null)
        {
            return GetFallbackLocation(ipAddress);
        }

        // MaxMind City() with exception handling (no TryCity() available)
        try
        {
            var response = _cityReader.City(ipAddress);

            return new GeoLocationResult
            {
                Country = response.Country.Name ?? "Unknown",
                City = response.City.Name ?? "Unknown",
                IsoCode = response.Country.IsoCode ?? "XX",
                Latitude = response.Location.Latitude,
                Longitude = response.Location.Longitude,
                IsSuccess = true
            };
        }
        catch (AddressNotFoundException)
        {
            // IP not in database - silent fallback without logging
            return GetFallbackLocation(ipAddress);
        }
        catch (Exception ex)
        {
            // Only log actual errors
            _logger.LogWarning("GeoIP2 lookup failed for IP {IP}: {Message}", ipAddress, ex.Message);
            return GetFallbackLocation(ipAddress);
        }
    }

    private bool IsPrivateOrLocalhost(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || ipAddress == "Unknown")
            return true;

        // Localhost IPv4 and IPv6
        if (ipAddress == "::1" || ipAddress == "127.0.0.1")
            return true;

        // Private IP ranges (RFC 1918)
        if (ipAddress.StartsWith("192.168.") ||
            ipAddress.StartsWith("10.") ||
            ipAddress.StartsWith("172."))
            return true;

        return false;
    }

    private GeoLocationResult GetFallbackLocation(string ipAddress)
    {
        // No GeoIP database available, or the IP could not be resolved.
        // Report failure rather than fabricating a location so callers
        // (e.g. impossible-travel / VPN-by-location checks) skip the
        // check instead of acting on made-up data.
        _logger.LogDebug("No geolocation available for IP {IP}", ipAddress);

        return new GeoLocationResult
        {
            IsSuccess = false,
            ErrorMessage = "Geolocation unavailable"
        };
    }

    public void Dispose()
    {
        _cityReader?.Dispose();
    }
}
