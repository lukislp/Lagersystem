using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using System.Net;
using System.Text.RegularExpressions;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Service for per-user IP-based access rules.
/// </summary>
public interface IUserIpAccessService
{
    /// <summary>
    /// Checks whether the given IP address is allowed for the specified user.
    /// </summary>
    Task<IpAccessCheckResult> CheckAccessAsync(int userId, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all IP rules for a user.
    /// </summary>
    Task<List<UserIpAccessRule>> GetRulesAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new IP rule.
    /// </summary>
    Task<UserIpAccessRule?> AddRuleAsync(int userId, string ipPattern, string? description, bool isAllowed, int? createdByUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an IP rule.
    /// </summary>
    Task<bool> UpdateRuleAsync(int ruleId, string ipPattern, string? description, bool isAllowed, bool isActive, int? updatedByUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an IP rule.
    /// </summary>
    Task<bool> DeleteRuleAsync(int ruleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables IP restrictions for a user.
    /// </summary>
    Task<bool> SetIpRestrictionsEnabledAsync(int userId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the given IP pattern is valid.
    /// </summary>
    bool IsValidIpPattern(string pattern);
}
