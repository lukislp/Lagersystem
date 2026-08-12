using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using System.Security.Cryptography;

namespace LagersystemLVHome.Application.Services;

public sealed class PasswordlessLoginService : IPasswordlessLoginService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IEmailService _emailService;
    private readonly ILogger<PasswordlessLoginService> _logger;
    private readonly IAuditService? _auditService;
    private readonly IConfiguration _configuration;

    private const int TokenExpirationMinutes = 15;
    private const int TokenLength = 64;

    public PasswordlessLoginService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        IEmailService emailService,
        ILogger<PasswordlessLoginService> logger,
        IConfiguration configuration,
        IAuditService? auditService = null)
    {
        _contextFactory = contextFactory;
        _emailService = emailService;
        _logger = logger;
        _configuration = configuration;
        _auditService = auditService;
    }

    public async Task<bool> SendMagicLinkAsync(string email, string? ipAddress = null, string? userAgent = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Email == email && u.IsActive && !u.IsDeleted, cancellationToken);

            if (user == null)
            {
                _logger.LogWarning("Magic link requested for non-existent email: {Email}", email);
                // Always return true for security (prevents email enumeration)
                return true;
            }

            if (!user.PasswordlessEnabled && user.DefaultLoginMethod != "Both")
            {
                _logger.LogWarning("Magic link requested but passwordless not enabled for user: {UserId}", user.Id);
                return false;
            }

            if (user.ApprovalStatus != UserApprovalStatus.Approved)
            {
                _logger.LogWarning("Magic link requested for non-approved user: {UserId}", user.Id);
                return true; // Don't reveal account status
            }

            // Delete old unused tokens
            var oldTokens = await context.MagicLinkTokens
                .Where(t => t.UserId == user.Id && !t.IsUsed)
                .ToListAsync(cancellationToken);
            context.MagicLinkTokens.RemoveRange(oldTokens);

            // Generate new token
            var token = GenerateSecureToken();
            var magicLinkToken = new MagicLinkToken
            {
                UserId = user.Id,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddMinutes(TokenExpirationMinutes),
                IpAddress = ipAddress,
                UserAgent = userAgent
            };

            context.MagicLinkTokens.Add(magicLinkToken);
            await context.SaveChangesAsync(cancellationToken);

            var applicationUrl = _configuration["EmailSettings:ApplicationUrl"] ?? "https://localhost:5001";
            var magicLink = $"{applicationUrl}/login/magic?token={token}";

            await _emailService.SendEmailAsync(
                user.Email,
                "Ihr Magic Link f\u00fcr LagerSystem",
                GenerateMagicLinkEmailBody(user.DisplayName ?? user.Username, magicLink, TokenExpirationMinutes),
                isHtml: true
            );

            _logger.LogInformation("Magic link sent to user {UserId} ({Email})", user.Id, email);

            if (_auditService != null)
            {
                await _auditService.LogAsync("MAGIC_LINK_SENT", "User", user.Id,
                    new { IpAddress = ipAddress }, AuditSeverity.Info);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending magic link to {Email}", email);
            return false;
        }
    }

    public async Task<User?> ValidateMagicLinkAsync(string token, string? ipAddress = null, string? userAgent = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var magicLinkToken = await context.MagicLinkTokens
                .Include(t => t.User)
                .ThenInclude(u => u!.Warehouse)
                .FirstOrDefaultAsync(t => t.Token == token && !t.IsUsed, cancellationToken);

            if (magicLinkToken == null)
            {
                _logger.LogWarning("Invalid magic link token attempted: {Token}", token[..Math.Min(10, token.Length)] + "...");

                if (_auditService != null)
                {
                    await _auditService.LogAsync("MAGIC_LINK_INVALID", "MagicLinkToken", null,
                        new { IpAddress = ipAddress }, AuditSeverity.Warning);
                }

                return null;
            }

            if (magicLinkToken.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning("Expired magic link token used: UserId={UserId}", magicLinkToken.UserId);

                if (_auditService != null)
                {
                    await _auditService.LogAsync("MAGIC_LINK_EXPIRED", "User", magicLinkToken.UserId,
                        new { IpAddress = ipAddress }, AuditSeverity.Warning);
                }

                return null;
            }

            var user = magicLinkToken.User;
            if (user == null || !user.IsActive || user.IsDeleted || user.ApprovalStatus != UserApprovalStatus.Approved)
            {
                _logger.LogWarning("Magic link used for inactive/deleted user: {UserId}", magicLinkToken.UserId);
                return null;
            }

            magicLinkToken.IsUsed = true;
            magicLinkToken.UsedAt = DateTime.UtcNow;

            user.LastLoginAt = DateTime.UtcNow;
            user.LastLoginIp = ipAddress;
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Magic link login successful for user {UserId} ({Username})", user.Id, user.Username);

            if (_auditService != null)
            {
                await _auditService.LogAsync("MAGIC_LINK_LOGIN", "User", user.Id,
                    new { IpAddress = ipAddress, UserAgent = userAgent }, AuditSeverity.Info);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating magic link token");
            return null;
        }
    }

    public async Task<bool> IsPasswordlessEnabledAsync(string email, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var user = await context.Users
                .Where(u => u.Email == email && u.IsActive && !u.IsDeleted)
                .Select(u => new { u.PasswordlessEnabled, u.DefaultLoginMethod })
                .FirstOrDefaultAsync(cancellationToken);

            return user?.PasswordlessEnabled == true || user?.DefaultLoginMethod == "Both";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking passwordless status for {Email}", email);
            return false;
        }
    }

    public async Task<bool> SetPasswordlessEnabledAsync(int userId, bool enabled, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var user = await context.Users.FindAsync(userId);
            if (user == null) return false;

            user.PasswordlessEnabled = enabled;

            // If passwordless is disabled, reset login method
            if (!enabled && user.DefaultLoginMethod == "Passwordless")
            {
                user.DefaultLoginMethod = "Password";
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Passwordless login {Status} for user {UserId}",
                enabled ? "enabled" : "disabled", userId);

            if (_auditService != null)
            {
                await _auditService.LogAsync(enabled ? "PASSWORDLESS_ENABLED" : "PASSWORDLESS_DISABLED",
                    "User", userId, null, AuditSeverity.Info);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting passwordless status for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> SetDefaultLoginMethodAsync(int userId, string method, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!new[] { "Password", "Passwordless", "Both" }.Contains(method))
            {
                _logger.LogWarning("Invalid login method: {Method}", method);
                return false;
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var user = await context.Users.FindAsync(userId);
            if (user == null) return false;

            // Passwordless must be enabled for "Passwordless" or "Both"
            if ((method == "Passwordless" || method == "Both") && !user.PasswordlessEnabled)
            {
                user.PasswordlessEnabled = true;
            }

            user.DefaultLoginMethod = method;
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Default login method set to {Method} for user {UserId}", method, userId);

            if (_auditService != null)
            {
                await _auditService.LogAsync("LOGIN_METHOD_CHANGED", "User", userId,
                    new { Method = method }, AuditSeverity.Info);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting login method for user {UserId}", userId);
            return false;
        }
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenLength);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private static string GenerateMagicLinkEmailBody(string userName, string magicLink, int expirationMinutes)
    {
        return $@"
<!DOCTYPE html>
<html lang='de'>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Magic Link Login - LagerSystem</title>
    <!--[if mso]>
    <style type='text/css'>
        body, table, td {{font-family: Arial, Helvetica, sans-serif !important;}}
    </style>
    <![endif]-->
</head>
<body style='margin: 0; padding: 0; background-color: #f4f4f7; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif;'>
    <table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%' style='background-color: #f4f4f7;'>
    <tr>
        <td style='padding: 40px 20px;'>
            <table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%' style='max-width: 600px; margin: 0 auto;'>

                <!-- Header -->
                <tr>
                    <td style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 40px 30px; text-align: center; border-radius: 12px 12px 0 0;'>
                        <table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%'>
                            <tr>
                                <td style='text-align: center;'>
                                    <div style='width: 60px; height: 60px; background-color: rgba(255,255,255,0.2); border-radius: 50%; margin: 0 auto 15px; line-height: 60px;'>
                                        <span style='font-size: 28px; color: white;'>&#128274;</span>
                                    </div>
                                    <h1 style='margin: 0; color: #ffffff; font-size: 24px; font-weight: 600;'>Magic Link Login</h1>
                                    <p style='margin: 10px 0 0; color: rgba(255,255,255,0.9); font-size: 14px;'>Sicherer Zugang zu LagerSystem</p>
                                </td>
                            </tr>
                        </table>
                    </td>
                </tr>

                <!-- Content -->
                <tr>
                    <td style='background-color: #ffffff; padding: 40px 30px;'>
                        <h2 style='margin: 0 0 20px; color: #1a1a2e; font-size: 20px;'>Hallo {userName}!</h2>
                        <p style='margin: 0 0 25px; color: #4a4a4a; font-size: 16px; line-height: 1.6;'>
                            Sie haben einen Magic Link f&#252;r den Login bei <strong>LagerSystem</strong> angefordert.
                            Klicken Sie auf den Button unten, um sich sicher anzumelden.
                        </p>

                        <!-- CTA Button -->
                        <table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%'>
                            <tr>
                                <td style='text-align: center; padding: 10px 0 30px;'>
                                    <a href='{magicLink}'
                                        style='display: inline-block;
                                                background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
                                                color: #ffffff !important;
                                                text-decoration: none;
                                                padding: 16px 40px;
                                                border-radius: 8px;
                                                font-size: 16px;
                                                font-weight: 600;
                                                box-shadow: 0 4px 15px rgba(102, 126, 234, 0.4);'>
                                        Jetzt einloggen
                                    </a>
                                </td>
                            </tr>
                        </table>

                        <!-- Warning Box -->
                        <table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%' style='background-color: #fff8e6; border: 1px solid #f0d58c; border-radius: 8px;'>
                            <tr>
                                <td style='padding: 20px;'>
                                    <table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%'>
                                        <tr>
                                            <td style='width: 30px; vertical-align: top;'>
                                                <span style='font-size: 20px; color: #b8860b;'>&#9888;</span>
                                            </td>
                                            <td style='padding-left: 10px;'>
                                                <strong style='color: #8b6914; font-size: 14px;'>Wichtige Hinweise:</strong>
                                                <ul style='margin: 10px 0 0; padding-left: 18px; color: #6b5a1e; font-size: 14px; line-height: 1.8;'>
                                                    <li>Dieser Link ist <strong>{expirationMinutes} Minuten</strong> g&#252;ltig</li>
                                                    <li>Der Link kann nur <strong>einmal</strong> verwendet werden</li>
                                                    <li>Falls Sie diesen Link nicht angefordert haben, ignorieren Sie diese E-Mail</li>
                                                </ul>
                                            </td>
                                        </tr>
                                    </table>
                                </td>
                            </tr>
                        </table>

                        <!-- Fallback Link -->
                        <p style='margin: 25px 0 0; color: #888; font-size: 12px; line-height: 1.6;'>
                            Falls der Button nicht funktioniert, kopieren Sie diesen Link in Ihren Browser:
                        </p>
                        <p style='margin: 8px 0 0; padding: 12px; background-color: #f8f9fa; border-radius: 6px; word-break: break-all;'>
                            <a href='{magicLink}' style='color: #667eea; font-size: 12px; text-decoration: none;'>{magicLink}</a>
                        </p>
                    </td>
                </tr>

                <!-- Footer -->
                <tr>
                    <td style='background-color: #f8f9fa; padding: 25px 30px; text-align: center; border-radius: 0 0 12px 12px; border-top: 1px solid #e9ecef;'>
                        <p style='margin: 0 0 8px; color: #6c757d; font-size: 13px;'>
                            Diese E-Mail wurde automatisch von LagerSystem gesendet.
                        </p>
                        <p style='margin: 0; color: #adb5bd; font-size: 12px;'>
                            &copy; {DateTime.Now.Year} LagerSystem - Inventory Management
                        </p>
                    </td>
                </tr>

            </table>
        </td>
    </tr>
    </table>
</body>
</html>";
    }
}
