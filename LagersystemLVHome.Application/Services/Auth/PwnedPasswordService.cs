using System.Security.Cryptography;
using System.Text;

namespace LagersystemLVHome.Application.Services;

public sealed class PwnedPasswordResult
{
    public bool IsCompromised { get; set; }
    public int BreachCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class PwnedPasswordService : IPwnedPasswordService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PwnedPasswordService> _logger;

    public PwnedPasswordService(HttpClient httpClient, ILogger<PwnedPasswordService> logger)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "LagerSystem-PasswordChecker");

        _logger = logger;
    }

    public async Task<bool> IsPasswordCompromisedAsync(string password, CancellationToken cancellationToken = default)
    {
        var result = await CheckPasswordAsync(password);
        return result.IsCompromised;
    }

    public async Task<PwnedPasswordResult> CheckPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return new PwnedPasswordResult
                {
                    IsCompromised = false,
                    Message = "Kein Passwort angegeben"
                };
            }

            // Compute SHA1 hash of the password
            using var sha1 = SHA1.Create();
            var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(password));
            var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToUpper();

            // k-Anonymity: only transmit first 5 characters
            var prefix = hash.Substring(0, 5);
            var suffix = hash.Substring(5);

            _logger.LogDebug("Checking password with prefix: {Prefix}", prefix);

            // API call (returns all hashes with this prefix)
            var response = await _httpClient.GetAsync($"range/{prefix}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Pwned Passwords API returned {StatusCode}", response.StatusCode);

                return new PwnedPasswordResult
                {
                    IsCompromised = false,
                    Message = "\u00dcberpr\u00fcfung konnte nicht durchgef\u00fchrt werden"
                };
            }

            var content = await response.Content.ReadAsStringAsync();

            var lines = content.Split('\n');
            foreach (var line in lines)
            {
                var parts = line.Split(':');
                if (parts.Length == 2)
                {
                    var hashSuffix = parts[0].Trim();
                    var count = int.Parse(parts[1].Trim());

                    if (hashSuffix.Equals(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Password found in breach database {Count} times", count);

                        return new PwnedPasswordResult
                        {
                            IsCompromised = true,
                            BreachCount = count,
                            Message = count > 1000
                                ? $"KRITISCH: Dieses Passwort wurde in {count:N0} Datenlecks gefunden!"
                                : $"Dieses Passwort wurde in {count:N0} Datenlecks gefunden!"
                        };
                    }
                }
            }

            _logger.LogInformation("Password is safe (not found in breach database)");

            return new PwnedPasswordResult
            {
                IsCompromised = false,
                Message = "Passwort nicht in Datenleck-Datenbank gefunden"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking password against Pwned Passwords API");

            return new PwnedPasswordResult
            {
                IsCompromised = false,
                Message = "\u00dcberpr\u00fcfung konnte nicht durchgef\u00fchrt werden"
            };
        }
    }
}
