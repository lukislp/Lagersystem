using Microsoft.AspNetCore.Components.Server.Circuits;
using System.Collections.Concurrent;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Circuit-isolated user store with HttpContext and circuit handler tracking.
/// Each Blazor Server circuit (browser tab/device) has its own isolated session.
/// </summary>
public sealed class CircuitUserStore
{
    private readonly ConcurrentDictionary<string, UserSession> _circuitUsers = new();
    private readonly ConcurrentDictionary<string, string> _circuitSessionIds = new();

    // Mapping: connection ID -> circuit ID (for HttpContext-based lookup)
    private readonly ConcurrentDictionary<string, string> _connectionIdToCircuitId = new();

    // Active circuit handler instance stores the circuit ID
    private static readonly AsyncLocal<string?> _currentCircuitIdFromHandler = new();

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CircuitUserStore> _logger;

    public CircuitUserStore(
        IHttpContextAccessor httpContextAccessor,
        ILogger<CircuitUserStore> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public void SetCurrentCircuitId(string circuitId)
    {
        // Set in AsyncLocal (for circuit handler context)
        _currentCircuitIdFromHandler.Value = circuitId;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            // Store circuit ID in HttpContext.Items (available for the entire request)
            httpContext.Items["CircuitId"] = circuitId;

            // Update connection mapping on every call
            var connectionId = httpContext.Connection.Id;
            if (!string.IsNullOrEmpty(connectionId))
            {
                var existingMapping = _connectionIdToCircuitId.TryGetValue(connectionId, out var existing);
                _connectionIdToCircuitId[connectionId] = circuitId;

                if (existingMapping && existing != circuitId)
                {
                    _logger.LogWarning("Connection mapping updated: Connection {ConnectionId} remapped from Circuit {OldCircuitId} to {NewCircuitId}",
                        connectionId, existing, circuitId);
                }
                else if (!existingMapping)
                {
                    _logger.LogDebug("Connection mapping created: Connection {ConnectionId} -> Circuit {CircuitId}",
                        connectionId, circuitId);
                }
            }
        }

        _logger.LogDebug("Set circuit ID: {CircuitId}", circuitId);
    }

    /// <summary>
    /// Returns the current circuit ID (multi-strategy with safe fallbacks).
    /// </summary>
    private string? GetCurrentCircuitId()
    {
        // Strategy 1: AsyncLocal (circuit handler context)
        var circuitIdFromHandler = _currentCircuitIdFromHandler.Value;
        if (!string.IsNullOrEmpty(circuitIdFromHandler))
        {
            _logger.LogDebug("Circuit ID from AsyncLocal: {CircuitId}", circuitIdFromHandler);
            return circuitIdFromHandler;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            _logger.LogWarning("No HttpContext and no AsyncLocal circuit ID available");
            // No fallback to single circuit when HttpContext is missing to prevent cross-device session sharing
            return null;
        }

        // Strategy 2: HttpContext.Items (request context)
        if (httpContext.Items.TryGetValue("CircuitId", out var circuitIdObj) && circuitIdObj is string circuitId)
        {
            _logger.LogDebug("Circuit ID from HttpContext.Items: {CircuitId}", circuitId);
            return circuitId;
        }

        // Strategy 3: Connection mapping (fallback)
        var connectionId = httpContext.Connection.Id;
        if (!string.IsNullOrEmpty(connectionId) && _connectionIdToCircuitId.TryGetValue(connectionId, out var mappedCircuitId))
        {
            _logger.LogDebug("Circuit ID from connection mapping: {CircuitId} (Connection: {ConnectionId})",
                mappedCircuitId, connectionId);

            // Cache for subsequent calls
            httpContext.Items["CircuitId"] = mappedCircuitId;

            // Also set in AsyncLocal for subsequent handler calls
            _currentCircuitIdFromHandler.Value = mappedCircuitId;

            return mappedCircuitId;
        }

        // No automatic fallback to prevent cross-device session sharing
        _logger.LogWarning("No circuit ID found for connection {ConnectionId}. No fallback applied to prevent cross-device session sharing.",
            connectionId);

        return null;
    }

    /// <summary>
    /// Stores the user session for the current circuit.
    /// </summary>
    public void SetUser(UserSession? session)
    {
        var circuitId = GetCurrentCircuitId();
        if (string.IsNullOrEmpty(circuitId))
        {
            _logger.LogError("Cannot set user: No circuit ID available");
            return;
        }

        if (session == null)
        {
            _circuitUsers.TryRemove(circuitId, out _);
            _logger.LogInformation("Removed user session for circuit {CircuitId}", circuitId);
        }
        else
        {
            _circuitUsers[circuitId] = session;
            _logger.LogInformation("Stored user session for circuit {CircuitId}: User={Username}, UserId={UserId}",
                circuitId, session.Username, session.UserId);
        }
    }

    public UserSession? GetUser()
    {
        var circuitId = GetCurrentCircuitId();
        if (string.IsNullOrEmpty(circuitId))
        {
            _logger.LogWarning("Cannot get user: No circuit ID available");
            return null;
        }

        if (_circuitUsers.TryGetValue(circuitId, out var session))
        {
            _logger.LogDebug("Retrieved user session for circuit {CircuitId}: {Username}",
                circuitId, session.Username);
            return session;
        }

        _logger.LogDebug("No user session found for circuit {CircuitId}", circuitId);
        return null;
    }

    public void ClearUser()
    {
        var circuitId = GetCurrentCircuitId();
        if (string.IsNullOrEmpty(circuitId))
        {
            _logger.LogWarning("Cannot clear user: No circuit ID available");
            return;
        }

        _circuitUsers.TryRemove(circuitId, out _);
        _circuitSessionIds.TryRemove(circuitId, out _);
        _logger.LogInformation("Cleared user session for circuit {CircuitId}", circuitId);
    }

    /// <summary>
    /// Stores the session ID for the current circuit.
    /// </summary>
    public void SetSessionId(string? sessionId)
    {
        var circuitId = GetCurrentCircuitId();
        if (string.IsNullOrEmpty(circuitId))
        {
            _logger.LogError("Cannot set session ID: No circuit ID available");
            return;
        }

        if (string.IsNullOrEmpty(sessionId))
        {
            _circuitSessionIds.TryRemove(circuitId, out _);
            _logger.LogDebug("Removed session ID for circuit {CircuitId}", circuitId);
        }
        else
        {
            _circuitSessionIds[circuitId] = sessionId;
            _logger.LogInformation("Stored session ID for circuit {CircuitId}: {SessionId}", circuitId, sessionId);
        }
    }

    public string? GetSessionId()
    {
        var circuitId = GetCurrentCircuitId();
        if (string.IsNullOrEmpty(circuitId))
        {
            _logger.LogWarning("Cannot get session ID: No circuit ID available");
            return null;
        }

        if (_circuitSessionIds.TryGetValue(circuitId, out var sessionId))
        {
            _logger.LogDebug("Retrieved session ID for circuit {CircuitId}: {SessionId}", circuitId, sessionId);
            return sessionId;
        }

        _logger.LogDebug("No session ID found for circuit {CircuitId}", circuitId);
        return null;
    }

    /// <summary>
    /// Cleanup for terminated circuits (called by CircuitHandler).
    /// </summary>
    public void RemoveCircuit(string circuitId)
    {
        _circuitUsers.TryRemove(circuitId, out var session);
        _circuitSessionIds.TryRemove(circuitId, out var sessionId);

        var connectionIdsToRemove = _connectionIdToCircuitId
            .Where(kvp => kvp.Value == circuitId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var connId in connectionIdsToRemove)
        {
            _connectionIdToCircuitId.TryRemove(connId, out _);
        }

        _logger.LogInformation("Circuit {CircuitId} removed: User={Username}, SessionId={SessionId}",
            circuitId, session?.Username ?? "None", sessionId ?? "None");
    }

    public int GetActiveCircuitCount()
    {
        return _circuitUsers.Count;
    }

    public Dictionary<string, string> GetAllCircuits()
    {
        return _circuitUsers.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Username
        );
    }

    public Dictionary<string, string> GetAllConnectionMappings()
    {
        return _connectionIdToCircuitId.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
        );
    }

    /// <summary>
    /// Finds a circuit ID by session ID (reverse lookup for cookie-based restoration).
    /// </summary>
    public string? FindCircuitBySessionId(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return null;

        // Search all circuits for a matching session ID
        var matchingCircuit = _circuitSessionIds
            .Where(kvp => kvp.Value == sessionId)
            .Select(kvp => kvp.Key)
            .FirstOrDefault();

        if (!string.IsNullOrEmpty(matchingCircuit))
        {
            _logger.LogInformation("Found circuit {CircuitId} for session {SessionId}",
                matchingCircuit, sessionId);
        }
        else
        {
            _logger.LogWarning("No circuit found for session {SessionId}", sessionId);
        }

        return matchingCircuit;
    }

    /// <summary>
    /// Restores a user session from a DB session (for cookie-based restoration).
    /// </summary>
    public void RestoreUserFromDbSession(UserSession session, string circuitId)
    {
        if (session == null || string.IsNullOrEmpty(circuitId))
        {
            _logger.LogWarning("Cannot restore user: Invalid session or circuit ID");
            return;
        }

        SetCurrentCircuitId(circuitId);

        _circuitUsers[circuitId] = session;

        _logger.LogInformation("User session restored from DB for circuit {CircuitId}: User={Username}, UserId={UserId}",
            circuitId, session.Username, session.UserId);
    }
}
