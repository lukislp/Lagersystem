using System;

namespace LagersystemLVHome.Domain.Models
{
    public enum NotificationType
    {
        Info,
        Warning,
        Error,
        Success,
        LowStock,
        CriticalStock,
        NewUser,
        SecurityAlert,
        SystemUpdate
    }

    public enum NotificationPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum NotificationChannel
    {
        InApp,
        Email,
        Push,
        All
    }

    public class Notification
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public NotificationType Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public NotificationPriority Priority { get; set; } = NotificationPriority.Medium;

        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReadAt { get; set; }

        public string? ActionUrl { get; set; }
        public string? Data { get; set; } // JSON for additional data

        public int? WarehouseId { get; set; }
        public Warehouse? Warehouse { get; set; }
    }

    public class UserNotificationSettings
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        // E-Mail Notifications
        public bool EmailLowStock { get; set; } = true;
        public bool EmailCriticalStock { get; set; } = true;
        public bool EmailNewUser { get; set; } = true;
        public bool EmailSecurityAlert { get; set; } = true;
        public bool EmailSystemUpdate { get; set; } = false;

        // Push Notifications
        public bool PushLowStock { get; set; } = true;
        public bool PushCriticalStock { get; set; } = true;
        public bool PushNewUser { get; set; } = false;
        public bool PushSecurityAlert { get; set; } = true;

        // In-App Notifications
        public bool InAppLowStock { get; set; } = true;
        public bool InAppCriticalStock { get; set; } = true;
        public bool InAppNewUser { get; set; } = true;
        public bool InAppSecurityAlert { get; set; } = true;

        // Stock thresholds
        public int LowStockThreshold { get; set; } = 10;
        public int CriticalStockThreshold { get; set; } = 5;

        // Digest settings
        public bool DailyDigest { get; set; } = true;
        public TimeSpan DigestTime { get; set; } = new TimeSpan(9, 0, 0); // 9:00 AM

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
