using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// WebAuthn/Passkey credential for a user.
/// Enables passwordless and phishing-resistant authentication.
/// </summary>
public class UserPasskey
{
    public int Id { get; set; }

    /// <summary>
    /// Associated user.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Unique credential ID (Base64-encoded).
    /// </summary>
    [Required]
    [MaxLength(1024)]
    public string CredentialId { get; set; } = string.Empty;

    /// <summary>
    /// Public key of the credential (Base64-encoded COSE key).
    /// </summary>
    [Required]
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Signature counter for replay protection.
    /// </summary>
    public uint SignatureCounter { get; set; } = 0;

    /// <summary>
    /// Authenticator AAGUID (device type identification).
    /// </summary>
    [MaxLength(36)]
    public string? AaGuid { get; set; }

    /// <summary>
    /// User-friendly name for this passkey.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string DeviceName { get; set; } = "Unbekanntes Gerät";

    /// <summary>
    /// Credential type (e.g. "public-key").
    /// </summary>
    [MaxLength(50)]
    public string CredentialType { get; set; } = "public-key";

    /// <summary>
    /// Supported transports (usb, ble, nfc, internal).
    /// </summary>
    [MaxLength(200)]
    public string? Transports { get; set; }

    /// <summary>
    /// Whether this is a resident key / discoverable credential.
    /// </summary>
    public bool IsDiscoverable { get; set; } = true;

    /// <summary>
    /// Whether user verification was performed during registration.
    /// </summary>
    public bool UserVerified { get; set; } = false;

    /// <summary>
    /// Whether this passkey is still active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Registration timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last usage.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// IP address at registration.
    /// </summary>
    [MaxLength(45)]
    public string? RegisteredFromIp { get; set; }

    /// <summary>
    /// User-Agent at registration.
    /// </summary>
    [MaxLength(500)]
    public string? RegisteredUserAgent { get; set; }

    /// <summary>
    /// Number of successful authentications.
    /// </summary>
    public int UseCount { get; set; } = 0;

    // Navigation
    public virtual User? User { get; set; }
}

/// <summary>
/// Temporary challenge for WebAuthn operations.
/// </summary>
public class WebAuthnChallenge
{
    public int Id { get; set; }

    /// <summary>
    /// User ID (null for new user registration).
    /// </summary>
    public int? UserId { get; set; }

    [Required]
    [MaxLength(128)]
    public string Challenge { get; set; } = string.Empty;

    /// <summary>
    /// Operation type (register, authenticate).
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string OperationType { get; set; } = "register";

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Expiration timestamp (default: 5 minutes).
    /// </summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(5);

    /// <summary>
    /// Whether the challenge has already been used.
    /// </summary>
    public bool IsUsed { get; set; } = false;

    /// <summary>
    /// Session ID for association.
    /// </summary>
    [MaxLength(100)]
    public string? SessionId { get; set; }
}
