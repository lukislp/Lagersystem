namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Service for secure encryption of backup provider configurations.
/// Uses the ASP.NET Core Data Protection API.
/// </summary>
public interface ISecureConfigurationService
{
    /// <summary>
    /// Encrypts a configuration (e.g. JSON with API keys).
    /// </summary>
    /// <param name="plaintext">Plain-text configuration.</param>
    /// <returns>Encrypted string.</returns>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a configuration.
    /// </summary>
    /// <param name="ciphertext">Encrypted string.</param>
    /// <returns>Plain-text configuration.</returns>
    string Decrypt(string ciphertext);

    /// <summary>
    /// Checks whether a string is encrypted (Data Protection format).
    /// </summary>
    /// <param name="value">String to check.</param>
    /// <returns>True if encrypted.</returns>
    bool IsEncrypted(string value);
}
