# GeoData Directory

## Purpose

This directory contains the MaxMind GeoIP2 database for IP-based geolocation.

## Required File

```
GeoLite2-City.mmdb (~70 MB)
```

**This file is NOT included in the repository.** MaxMind's license prohibits redistribution, and the file is ~70 MB. Each developer/server must download it separately.

## Setup

1. **Create account:** https://www.maxmind.com/en/geolite2/signup (free)
2. **Download:** https://www.maxmind.com/en/accounts/current/geoip/downloads
3. **Select:** GeoLite2 City (MMDB format)
4. **Extract:** `.tar.gz` to get `GeoLite2-City.mmdb`
5. **Copy to:** `LagersystemLVHome/GeoData/GeoLite2-City.mmdb`

## Validation

```powershell
# Check if file exists
Test-Path "LagersystemLVHome\GeoData\GeoLite2-City.mmdb"

# Should be ~70 MB
Get-Item "LagersystemLVHome\GeoData\GeoLite2-City.mmdb" | Select-Object Length
```

## Usage

The file is loaded automatically by `GeoLocationService`:

```csharp
var databasePath = configuration["GeoIP:DatabasePath"] ??
    Path.Combine(AppContext.BaseDirectory, "GeoData", "GeoLite2-City.mmdb");

if (File.Exists(databasePath))
{
    _cityReader = new DatabaseReader(databasePath);
    _isAvailable = true;
}
```

## Updates

MaxMind publishes monthly updates. Recommended: update every 1-2 months.

```powershell
# 1. Download the new file
# 2. Replace the old file
Copy-Item "Downloads\GeoLite2-City.mmdb" "LagersystemLVHome\GeoData\" -Force
# 3. Restart the application
```

## .gitignore

The `.mmdb` file is listed in `.gitignore` and will not be committed:

```gitignore
LagersystemLVHome/GeoData/*.mmdb
```

## Links

- **MaxMind Website:** https://www.maxmind.com
- **Account:** https://www.maxmind.com/en/account/login
- **Downloads:** https://www.maxmind.com/en/accounts/current/geoip/downloads
- **Documentation:** https://dev.maxmind.com/geoip/docs

## License

GeoLite2 is licensed under the **Creative Commons Attribution-ShareAlike 4.0 International License**.

See: https://dev.maxmind.com/geoip/geolite2-free-geolocation-data
