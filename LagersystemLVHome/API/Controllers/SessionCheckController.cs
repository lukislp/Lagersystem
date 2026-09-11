using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using LagersystemLVHome.Application.Services;
using LagersystemLVHome.Application.Utilities;

namespace LagersystemLVHome.Controllers;

/// <summary>
/// API controller for session status checks (used by JavaScript SessionBlockingOverlay).
/// </summary>
[ApiController]
[Route("api/session")]
[AllowAnonymous]
[EnableRateLimiting("SessionCheck")]
public class SessionCheckController : ControllerBase
{
    private readonly ISessionManagementService _sessionService;
    private readonly ILogger<SessionCheckController> _logger;

    public SessionCheckController(
        ISessionManagementService sessionService,
        ILogger<SessionCheckController> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    /// <param name="sessionId">The session ID to check.</param>
    /// <returns>JSON with isActive and reason.</returns>
    [HttpGet("check/{sessionId}")]
    public async Task<IActionResult> CheckSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 100)
        {
            return BadRequest(new { isActive = false, reason = "InvalidSessionId" });
        }

        try
        {
            var session = await _sessionService.GetSessionAsync(sessionId);

            if (session == null)
            {
                _logger.LogDebug("Session check: {SessionId} not found", LogRedaction.MaskSecret(sessionId));
                return Ok(new { isActive = false, reason = "NotFound" });
            }

            if (!session.IsActive)
            {
                _logger.LogDebug("Session check: {SessionId} inactive, reason: {Reason}",
                    LogRedaction.MaskSecret(sessionId),
                    session.EndReason?.ToString() ?? "Unknown");

                return Ok(new
                {
                    isActive = false,
                    reason = session.EndReason?.ToString() ?? "Unknown"
                });
            }

            return Ok(new { isActive = true, reason = (string?)null });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking session {SessionId}", LogRedaction.MaskSecret(sessionId));
            // On error assume session is still active
            return Ok(new { isActive = true, reason = (string?)null });
        }
    }
}
