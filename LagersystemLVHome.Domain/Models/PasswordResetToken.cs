namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// Token for password reset.
/// </summary>
public class PasswordResetToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Token { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    public bool IsUsed { get; set; } = false;
    public string? IpAddress { get; set; }

    // Navigation
    public virtual User User { get; set; } = null!;
}
