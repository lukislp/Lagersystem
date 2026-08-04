using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

public sealed class EmailOtpService : IEmailOtpService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailOtpService> _logger;

    private const int OtpLength = 6;
    private const int OtpExpirationMinutes = 5;
    private const int MaxActiveTokens = 3;
    private const int MaxFailedAttempts = 5;

    public EmailOtpService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        IEmailService emailService,
        ILogger<EmailOtpService> logger)
    {
        _contextFactory = contextFactory;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<bool> SendOtpAsync(int userId, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FindAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("Email OTP: User {UserId} not found", userId);
            return false;
        }

        if (string.IsNullOrEmpty(user.Email))
        {
            _logger.LogWarning("Email OTP: No email for user {UserId}", userId);
            return false;
        }

        // Rate limiting: max active tokens per user
        var activeTokenCount = await context.EmailOtpTokens
            .CountAsync(t => t.UserId == userId && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow, cancellationToken);

        if (activeTokenCount >= MaxActiveTokens)
        {
            _logger.LogWarning("Email OTP: Too many active tokens for user {UserId} ({Count})",
                userId, activeTokenCount);
            return false;
        }

        // Generate secure 6-digit code
        var code = GenerateSecureCode();

        var token = new EmailOtpToken
        {
            UserId = userId,
            Code = code,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(OtpExpirationMinutes),
            IpAddress = ipAddress
        };

        context.EmailOtpTokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            await _emailService.SendTwoFactorCodeEmailAsync(user.Email, code);
            _logger.LogInformation("Email OTP sent for user {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email OTP for user {UserId}", userId);
            // Remove token if email failed
            context.EmailOtpTokens.Remove(token);
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }
    }

    public async Task<bool> ValidateOtpAsync(int userId, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        code = code.Replace(" ", "").Trim();

        if (code.Length != OtpLength)
            return false;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var token = await context.EmailOtpTokens
            .Where(t => t.UserId == userId && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (token == null)
        {
            _logger.LogWarning("Email OTP: No active token for user {UserId}", userId);
            return false;
        }

        // Brute-force protection
        if (token.FailedAttempts >= MaxFailedAttempts)
        {
            _logger.LogWarning("Email OTP: Too many failed attempts for user {UserId}", userId);
            token.IsUsed = true;
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }

        // Timing-safe comparison
        if (!CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(code),
            System.Text.Encoding.UTF8.GetBytes(token.Code)))
        {
            token.FailedAttempts++;
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Email OTP: Invalid code for user {UserId} (attempt {Attempt}/{Max})",
                userId, token.FailedAttempts, MaxFailedAttempts);
            return false;
        }

        // Code is correct - invalidate all active tokens for this user
        var activeTokens = await context.EmailOtpTokens
            .Where(t => t.UserId == userId && !t.IsUsed)
            .ToListAsync(cancellationToken);

        foreach (var t in activeTokens)
        {
            t.IsUsed = true;
        }

        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Email OTP successfully validated for user {UserId}", userId);
        return true;
    }

    public async Task CleanupExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var expiredTokens = await context.EmailOtpTokens
            .Where(t => t.ExpiresAt < DateTime.UtcNow || t.IsUsed)
            .Where(t => t.CreatedAt < DateTime.UtcNow.AddHours(-1))
            .ToListAsync(cancellationToken);

        if (expiredTokens.Count > 0)
        {
            context.EmailOtpTokens.RemoveRange(expiredTokens);
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Email OTP: {Count} expired tokens cleaned up", expiredTokens.Count);
        }
    }

    private static string GenerateSecureCode()
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return code.ToString("D6");
    }
}
