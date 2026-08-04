using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

public sealed class CustomAuthStateProvider : AuthenticationStateProvider
{
    private readonly CircuitUserStore _userStore;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CustomAuthStateProvider> _logger;
    private ClaimsPrincipal _currentUser = new(new ClaimsIdentity());

    public CustomAuthStateProvider(
        CircuitUserStore userStore,
        IHttpContextAccessor httpContextAccessor,
        ILogger<CustomAuthStateProvider> logger)
    {
        _userStore = userStore;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        _logger.LogDebug("GetAuthenticationStateAsync called");

        var userSession = _userStore.GetUser();

        if (userSession != null)
        {
            _logger.LogDebug("Session found for user: {Username}", userSession.Username);

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userSession.UserId.ToString()),
                new Claim(ClaimTypes.Name, userSession.Username),
                new Claim(ClaimTypes.Email, userSession.Email),
                new Claim("DisplayName", userSession.DisplayName),
                new Claim("WarehouseId", userSession.WarehouseId.ToString()),
                new Claim(ClaimTypes.Role, userSession.Role.ToString())
            }, "CustomAuth");

            _currentUser = new ClaimsPrincipal(identity);

            _logger.LogDebug("User authenticated from store");
        }
        else
        {
            _logger.LogDebug("No session found in store");
            _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
        }

        _logger.LogDebug("User authenticated: {IsAuthenticated}", _currentUser.Identity?.IsAuthenticated ?? false);
        _logger.LogDebug("User name: {Name}", _currentUser.Identity?.Name ?? "None");

        return Task.FromResult(new AuthenticationState(_currentUser));
    }

    public Task MarkUserAsAuthenticated(User user, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MarkUserAsAuthenticated called for: {Username}", user.Username);

        var userSession = new UserSession
        {
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            WarehouseId = user.WarehouseId,
            Role = user.Role
        };

        _userStore.SetUser(userSession);
        _logger.LogDebug("Session saved to store");

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("DisplayName", user.DisplayName),
            new Claim("WarehouseId", user.WarehouseId.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        }, "CustomAuth");

        _currentUser = new ClaimsPrincipal(identity);

        _logger.LogDebug("Notifying authentication state changed");
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
        _logger.LogDebug("Authentication state changed notification sent");

        return Task.CompletedTask;
    }

    public Task MarkUserAsLoggedOut(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MarkUserAsLoggedOut called");

        _userStore.ClearUser();

        _currentUser = new ClaimsPrincipal(new ClaimsIdentity());

        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));

        _logger.LogDebug("Logged out successfully");
        return Task.CompletedTask;
    }
}

public sealed class UserSession
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public UserRole Role { get; set; }
}
