using System.Security.Cryptography;
using System.Text;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Service for checking passwords against the Have I Been Pwned API.
/// Uses k-Anonymity model (only first 5 characters of hash are transmitted).
/// </summary>
public interface IPwnedPasswordService
{
    Task<bool> IsPasswordCompromisedAsync(string password, CancellationToken cancellationToken = default);
    Task<PwnedPasswordResult> CheckPasswordAsync(string password, CancellationToken cancellationToken = default);
}
