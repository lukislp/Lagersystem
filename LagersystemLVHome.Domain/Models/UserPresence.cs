namespace LagersystemLVHome.Domain.Models;

public class UserPresence
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Role { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string? WarehouseName { get; set; }

    // Current Activity
    public string CurrentPage { get; set; } = string.Empty;
    public string DeviceType { get; set; } = "Desktop";
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    // Status
    public PresenceStatus Status { get; set; } = PresenceStatus.Online;
    public string? CustomStatus { get; set; }

    // Session Info
    public string SessionId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;

    // Profile Image
    public string? ProfileImagePath { get; set; }

    // Calculated Properties
    public bool IsOnline => (DateTime.UtcNow - LastSeen).TotalMinutes < 5;
    public bool IsIdle => (DateTime.UtcNow - LastSeen).TotalMinutes >= 5 && (DateTime.UtcNow - LastSeen).TotalMinutes < 15;
    public string StatusBadgeColor => Status switch
    {
        PresenceStatus.Online => "success",
        PresenceStatus.Idle => "warning",
        PresenceStatus.DoNotDisturb => "danger",
        PresenceStatus.Away => "secondary",
        _ => "secondary"
    };
}

public enum PresenceStatus
{
    Online,
    Idle,
    Away,
    DoNotDisturb,
    Offline
}
