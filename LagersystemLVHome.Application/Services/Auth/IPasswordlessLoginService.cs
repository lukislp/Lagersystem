using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using System.Security.Cryptography;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Service for passwordless login via magic link.
/// </summary>
public interface IPasswordlessLoginService
{
    /// <summary>
    /// Sends a magic link to the user's email address.
    /// </summary>
    Task<bool> SendMagicLinkAsync(string email, string? ipAddress = null, string? userAgent = null, CancellationToken cancellationToken = default);

    Task<User?> ValidateMagicLinkAsync(string token, string? ipAddress = null, string? userAgent = null, CancellationToken cancellationToken = default);

    Task<bool> IsPasswordlessEnabledAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables passwordless login for a user.
    /// </summary>
    Task<bool> SetPasswordlessEnabledAsync(int userId, bool enabled, CancellationToken cancellationToken = default);

    Task<bool> SetDefaultLoginMethodAsync(int userId, string method, CancellationToken cancellationToken = default);
}
