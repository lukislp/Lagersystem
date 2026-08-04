using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Custom authentication handler for Blazor Server.
/// Used when [Authorize] attributes are applied to components.
/// </summary>
public sealed class BlazorAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly CircuitUserStore _userStore;

    public BlazorAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        CircuitUserStore userStore)
        : base(options, logger, encoder)
    {
        _userStore = userStore;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            var user = _userStore.GetUser();

            if (user == null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim("DisplayName", user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("WarehouseId", user.WarehouseId.ToString())
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during authentication");
            return Task.FromResult(AuthenticateResult.Fail(ex));
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Redirect("/login");
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.Redirect("/");
        return Task.CompletedTask;
    }
}
