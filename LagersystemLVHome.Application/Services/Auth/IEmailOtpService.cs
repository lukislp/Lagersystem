using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Service for email-based OTP codes as a 2FA method.
/// </summary>
public interface IEmailOtpService
{
    Task<bool> SendOtpAsync(int userId, string? ipAddress = null, CancellationToken cancellationToken = default);

    Task<bool> ValidateOtpAsync(int userId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired OTP tokens.
    /// </summary>
    Task CleanupExpiredTokensAsync(CancellationToken cancellationToken = default);
}
