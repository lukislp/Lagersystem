using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// Persistent gamification statistics per user.
/// Incremented when writing audit logs so the data
/// survives GDPR cleanup of audit logs.
/// </summary>
public class UserGamificationStats
{
    public int Id { get; set; }

    public int UserId { get; set; }

    // Counters: stock movements
    public int TotalMovements { get; set; }
    public int TotalScans { get; set; }

    // Counters: products
    public int ProductsCreated { get; set; }
    public int ProductsUpdated { get; set; }
    public int ProductsDeleted { get; set; }

    // Counters: categories, storage locations & rooms
    public int CategoriesCreated { get; set; }
    public int StorageLocationsCreated { get; set; }
    public int RoomsCreated { get; set; }

    // Counters: import/export
    public int ImportsCompleted { get; set; }
    public int ExportsCompleted { get; set; }

    // Counters: auth & security
    public int TotalLogins { get; set; }
    public int PasswordChanges { get; set; }
    public int TwoFactorToggles { get; set; }

    // Streak tracking
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateTime? LastActiveDate { get; set; }
    public int TotalActiveDays { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual User? User { get; set; }
}
