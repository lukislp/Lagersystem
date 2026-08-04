using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using LagersystemLVHome.Application.Services;

namespace LagersystemLVHome.Authentication;

/// <summary>
/// Authentication handler for API-key-based authentication.
/// Creates dedicated API sessions (independent of browser sessions).
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IApiKeyService _apiKeyService;
    private readonly ILogger<ApiKeyAuthenticationHandler> _logger;
    private readonly IRateLimitService _rateLimitService;
    private readonly ISessionManagementService _sessionManagementService;
    private const string ApiKeyHeaderName = "X-API-Key";

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyService apiKeyService,
        IRateLimitService rateLimitService,
        ISessionManagementService sessionManagementService)
        : base(options, logger, encoder)
    {
        _apiKeyService = apiKeyService;
        _logger = logger.CreateLogger<ApiKeyAuthenticationHandler>();
        _rateLimitService = rateLimitService;
        _sessionManagementService = sessionManagementService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            if (!Request.Headers.ContainsKey(ApiKeyHeaderName))
            {
                _logger.LogDebug("API request without API-Key header");
                return AuthenticateResult.NoResult();
            }

            var apiKey = Request.Headers[ApiKeyHeaderName].ToString();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("Empty API-Key provided");
                LogFailedAuthAttemptAsync("/api/auth/apikey");
                return AuthenticateResult.Fail("Invalid API-Key");
            }

            var user = await _apiKeyService.ValidateApiKeyAsync(apiKey);
            var apiKeyDetails = await _apiKeyService.GetApiKeyByKeyAsync(apiKey);

            if (user == null || apiKeyDetails == null)
            {
                _logger.LogWarning("Invalid API-Key attempt from {IP}",
                    Request.HttpContext.Connection.RemoteIpAddress);
                LogFailedAuthAttemptAsync("/api/auth/apikey");
                return AuthenticateResult.Fail("Invalid API-Key");
            }

            if (!user.IsActive || user.IsDeleted)
            {
                _logger.LogWarning("API-Key used by inactive/deleted user {UserId}", user.Id);
                LogFailedAuthAttemptAsync("/api/auth/apikey");
                return AuthenticateResult.Fail("User account is not active");
            }

            var clientIp = GetClientIpAddress();
            var apiKeyName = apiKeyDetails.Name ?? "Unknown";
            var requestPath = Request.Path.ToString();

            var apiSession = await _sessionManagementService.GetOrCreateApiSessionAsync(
                user.Id,
                user.WarehouseId,
                clientIp,
                apiKeyName,
                requestPath);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("WarehouseId", user.WarehouseId.ToString()),
                new Claim("AuthenticationType", "ApiKey"),
                new Claim("ApiKeyName", apiKeyName),
                new Claim("ApiSessionId", apiSession?.SessionId ?? "")
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            _logger.LogInformation(
                "API authentication successful for user {UserId} ({Username}) with key '{ApiKeyName}'. Request: {RequestPath}, Session: {SessionId}",
                user.Id, user.Username, apiKeyName, requestPath, apiSession?.SessionId ?? "N/A");

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during API authentication");
            LogFailedAuthAttemptAsync("/api/auth/apikey");
            return AuthenticateResult.Fail("Authentication error");
        }
    }

    private string GetClientIpAddress()
    {
        // X-Forwarded-For (standard for proxies/load balancers)
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        // X-Real-IP (nginx)
        var realIp = Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // CF-Connecting-IP (Cloudflare)
        var cfIp = Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(cfIp))
        {
            return cfIp;
        }

        // Fallback: RemoteIpAddress
        return Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    /// <summary>
    /// Logs failed auth attempts for brute-force detection.
    /// </summary>
    private void LogFailedAuthAttemptAsync(string endpoint)
    {
        try
        {
            var identifier = GetUserIdentifier();
            _rateLimitService.LogFailedAuthAttempt(identifier, endpoint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log auth attempt for brute-force detection");
        }
    }

    private string GetUserIdentifier()
    {
        var ipAddress = GetClientIpAddress();

        var apiKey = Request.Headers[ApiKeyHeaderName].FirstOrDefault();
        if (!string.IsNullOrEmpty(apiKey))
        {
            return $"apikey:{apiKey[..Math.Min(apiKey.Length, 32)]}";
        }

        return $"ip:{ipAddress}";
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.ContentType = "application/json";

        var response = new
        {
            error = "Unauthorized",
            message = "Valid API-Key required. Include 'X-API-Key' header with your request.",
            timestamp = DateTime.UtcNow
        };

        return Response.WriteAsJsonAsync(response);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        Response.ContentType = "application/json";

        var response = new
        {
            error = "Forbidden",
            message = "Your API-Key does not have permission to access this resource.",
            timestamp = DateTime.UtcNow
        };

        return Response.WriteAsJsonAsync(response);
    }
}
