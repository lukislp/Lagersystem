using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using System.Net;
using System.Text.RegularExpressions;

namespace LagersystemLVHome.Application.Services;

public sealed class IpAccessCheckResult
{
    public bool IsAllowed { get; set; }
    public string? MatchedRule { get; set; }
    public string? Message { get; set; }
    public bool RestrictionsEnabled { get; set; }

    public static IpAccessCheckResult Allowed(string? matchedRule = null) => new()
    {
        IsAllowed = true,
        MatchedRule = matchedRule,
        RestrictionsEnabled = true
    };

    public static IpAccessCheckResult Denied(string message, string? matchedRule = null) => new()
    {
        IsAllowed = false,
        Message = message,
        MatchedRule = matchedRule,
        RestrictionsEnabled = true
    };

    public static IpAccessCheckResult NotRestricted() => new()
    {
        IsAllowed = true,
        RestrictionsEnabled = false,
        Message = "IP restrictions not enabled for this user"
    };
}

public sealed class UserIpAccessService : IUserIpAccessService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<UserIpAccessService> _logger;
    private readonly IAuditService? _auditService;

    public UserIpAccessService(
    IDbContextFactory<InventoryDbContext> contextFactory,
    ILogger<UserIpAccessService> logger,
    IAuditService? auditService = null)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _auditService = auditService;
    }

    public async Task<IpAccessCheckResult> CheckAccessAsync(int userId, string ipAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Check whether IP restrictions are enabled for the user
            var user = await context.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.IpRestrictionsEnabled })
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null || !user.IpRestrictionsEnabled)
            {
                return IpAccessCheckResult.NotRestricted();
            }

            // Fetch all active rules, ordered by priority (higher first)
            var rules = await context.UserIpAccessRules
        .Where(r => r.UserId == userId && r.IsActive)
            .OrderByDescending(r => r.Priority)
        .ToListAsync(cancellationToken);

            // If no rules are defined, allow access (whitelist mode is not enforced)
            if (!rules.Any())
            {
                _logger.LogDebug("No IP rules defined for user {UserId}, allowing access", userId);
                return IpAccessCheckResult.Allowed("No rules defined - access allowed");
            }

            // Evaluate all rules
            foreach (var rule in rules)
            {
                if (MatchesIpPattern(ipAddress, rule.IpPattern))
                {
                    if (rule.IsAllowed)
                    {
                        _logger.LogDebug("IP {IP} allowed for user {UserId} by rule: {Rule}",
                    ipAddress, userId, rule.Description ?? rule.IpPattern);
                        return IpAccessCheckResult.Allowed(rule.Description ?? rule.IpPattern);
                    }
                    else
                    {
                        _logger.LogWarning("IP {IP} denied for user {UserId} by rule: {Rule}",
                                ipAddress, userId, rule.Description ?? rule.IpPattern);

                        if (_auditService != null)
                        {
                            await _auditService.LogAsync("IP_ACCESS_DENIED", "User", userId,
                            new { IpAddress = ipAddress, Rule = rule.IpPattern, Description = rule.Description },
                        AuditSeverity.Warning);
                        }

                        return IpAccessCheckResult.Denied(
                            $"Zugriff von IP {ipAddress} nicht erlaubt",
                        rule.Description ?? rule.IpPattern);
                    }
                }
            }

            // No rule matched - default behavior: allow (block only if whitelist rules exist)
            var hasWhitelistRules = rules.Any(r => r.IsAllowed);
            if (hasWhitelistRules)
            {
                // Whitelist rules exist and none matched = block
                _logger.LogWarning("IP {IP} denied for user {UserId} - not in whitelist", ipAddress, userId);

                if (_auditService != null)
                {
                    await _auditService.LogAsync("IP_ACCESS_DENIED_NOT_WHITELISTED", "User", userId,
                            new { IpAddress = ipAddress }, AuditSeverity.Warning);
                }

                return IpAccessCheckResult.Denied("IP-Adresse nicht in der Whitelist", "No matching whitelist rule");
            }

            // Only blacklist rules present and IP not blocked = allow
            return IpAccessCheckResult.Allowed("Not in blacklist");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking IP access for user {UserId}", userId);
            // On error: allow access (fail-open for better UX)
            return IpAccessCheckResult.Allowed("Error during check - access allowed");
        }
    }

    public async Task<List<UserIpAccessRule>> GetRulesAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.UserIpAccessRules
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.IpPattern)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserIpAccessRule?> AddRuleAsync(int userId, string ipPattern, string? description, bool isAllowed, int? createdByUserId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!IsValidIpPattern(ipPattern))
            {
                _logger.LogWarning("Invalid IP pattern: {Pattern}", ipPattern);
                return null;
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var rule = new UserIpAccessRule
            {
                UserId = userId,
                IpPattern = ipPattern,
                Description = description,
                IsAllowed = isAllowed,
                Priority = isAllowed ? 10 : 20, // Blacklist has higher priority
                CreatedByUserId = createdByUserId
            };

            context.UserIpAccessRules.Add(rule);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("IP rule added for user {UserId}: {Pattern} ({Type})",
        userId, ipPattern, isAllowed ? "Allow" : "Deny");

            if (_auditService != null)
            {
                await _auditService.LogAsync("IP_RULE_ADDED", "UserIpAccessRule", rule.Id,
                new { UserId = userId, IpPattern = ipPattern, IsAllowed = isAllowed, Description = description },
            AuditSeverity.Info);
            }

            return rule;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding IP rule for user {UserId}", userId);
            return null;
        }
    }

    public async Task<bool> UpdateRuleAsync(int ruleId, string ipPattern, string? description, bool isAllowed, bool isActive, int? updatedByUserId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!IsValidIpPattern(ipPattern))
            {
                _logger.LogWarning("Invalid IP pattern: {Pattern}", ipPattern);
                return false;
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var rule = await context.UserIpAccessRules.FindAsync(ruleId);
            if (rule == null) return false;

            rule.IpPattern = ipPattern;
            rule.Description = description;
            rule.IsAllowed = isAllowed;
            rule.IsActive = isActive;
            rule.UpdatedAt = DateTime.UtcNow;
            rule.UpdatedByUserId = updatedByUserId;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("IP rule {RuleId} updated: {Pattern} ({Type})",
            ruleId, ipPattern, isAllowed ? "Allow" : "Deny");

            if (_auditService != null)
            {
                await _auditService.LogAsync("IP_RULE_UPDATED", "UserIpAccessRule", ruleId,
                new { IpPattern = ipPattern, IsAllowed = isAllowed, IsActive = isActive },
                AuditSeverity.Info);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating IP rule {RuleId}", ruleId);
            return false;
        }
    }

    public async Task<bool> DeleteRuleAsync(int ruleId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var rule = await context.UserIpAccessRules.FindAsync(ruleId);
            if (rule == null) return false;

            context.UserIpAccessRules.Remove(rule);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("IP rule {RuleId} deleted", ruleId);

            if (_auditService != null)
            {
                await _auditService.LogAsync("IP_RULE_DELETED", "UserIpAccessRule", ruleId,
                new { UserId = rule.UserId, IpPattern = rule.IpPattern }, AuditSeverity.Info);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting IP rule {RuleId}", ruleId);
            return false;
        }
    }

    public async Task<bool> SetIpRestrictionsEnabledAsync(int userId, bool enabled, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var user = await context.Users.FindAsync(userId);
            if (user == null) return false;

            user.IpRestrictionsEnabled = enabled;
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("IP restrictions {Status} for user {UserId}",
            enabled ? "enabled" : "disabled", userId);

            if (_auditService != null)
            {
                await _auditService.LogAsync(enabled ? "IP_RESTRICTIONS_ENABLED" : "IP_RESTRICTIONS_DISABLED",
                "User", userId, null, AuditSeverity.Info);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting IP restrictions for user {UserId}", userId);
            return false;
        }
    }

    public bool IsValidIpPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        // Exact IP (IPv4)
        if (IPAddress.TryParse(pattern, out _))
            return true;

        // Wildcard pattern (e.g. "192.168.1.*")
        if (pattern.Contains('*'))
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", "\\d{1,3}") + "$";
            try
            {
                _ = new Regex(regexPattern);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // CIDR notation (e.g. "192.168.1.0/24")
        if (pattern.Contains('/'))
        {
            var parts = pattern.Split('/');
            if (parts.Length == 2 &&
        IPAddress.TryParse(parts[0], out _) &&
        int.TryParse(parts[1], out var prefix) &&
        prefix >= 0 && prefix <= 32)
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesIpPattern(string ipAddress, string pattern)
    {
        try
        {
            // Exact match
            if (ipAddress == pattern)
                return true;

            // Wildcard pattern
            if (pattern.Contains('*'))
            {
                var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", "\\d{1,3}") + "$";
                return Regex.IsMatch(ipAddress, regexPattern);
            }

            // CIDR notation
            if (pattern.Contains('/'))
            {
                return IsInCidrRange(ipAddress, pattern);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error matching IP {IP} against pattern {Pattern}", ipAddress, pattern);
            return false;
        }
    }

    private bool IsInCidrRange(string ipAddress, string cidr)
    {
        try
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2) return false;

            if (!IPAddress.TryParse(parts[0], out var networkAddress)) return false;
            if (!int.TryParse(parts[1], out var prefixLength)) return false;
            if (!IPAddress.TryParse(ipAddress, out var checkAddress)) return false;

            var networkBytes = networkAddress.GetAddressBytes();
            var checkBytes = checkAddress.GetAddressBytes();

            if (networkBytes.Length != checkBytes.Length) return false;

            var mask = CreateMask(prefixLength, networkBytes.Length);

            for (int i = 0; i < networkBytes.Length; i++)
            {
                if ((networkBytes[i] & mask[i]) != (checkBytes[i] & mask[i]))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private byte[] CreateMask(int prefixLength, int length)
    {
        var mask = new byte[length];
        for (int i = 0; i < length; i++)
        {
            if (prefixLength >= 8)
            {
                mask[i] = 0xFF;
                prefixLength -= 8;
            }
            else if (prefixLength > 0)
            {
                mask[i] = (byte)(0xFF << (8 - prefixLength));
                prefixLength = 0;
            }
            else
            {
                mask[i] = 0;
            }
        }
        return mask;
    }
}
