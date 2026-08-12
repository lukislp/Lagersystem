using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

public sealed class GamificationService : IGamificationService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<GamificationService> _logger;

    public GamificationService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<GamificationService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task RecordActionAsync(int userId, string action, string? details = null, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return;

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var stats = await GetOrCreateStatsAsync(context, userId);

            switch (action)
            {
                case "STOCK_MOVEMENT":
                    stats.TotalMovements++;
                    if (details != null && details.Contains("Scan"))
                        stats.TotalScans++;
                    break;
                case "PRODUCT_CREATED":
                    stats.ProductsCreated++;
                    break;
                case "PRODUCT_UPDATED":
                    stats.ProductsUpdated++;
                    break;
                case "PRODUCT_DELETED":
                    stats.ProductsDeleted++;
                    break;
                case "LOGIN_SUCCESS":
                case "PASSKEY_LOGIN_SUCCESS":
                case "MAGIC_LINK_LOGIN":
                    stats.TotalLogins++;
                    break;
                case "CATEGORY_CREATED":
                    stats.CategoriesCreated++;
                    break;
                case "STORAGE_LOCATION_CREATED":
                    stats.StorageLocationsCreated++;
                    break;
                case "ROOM_CREATED":
                    stats.RoomsCreated++;
                    break;
                case "IMPORT_SUCCESS":
                case "DATA_IMPORT":
                    stats.ImportsCompleted++;
                    break;
                case "EXPORT":
                case "DATA_EXPORT":
                    stats.ExportsCompleted++;
                    break;
                case "PASSWORD_CHANGED":
                case "PASSWORD_RESET_SUCCESS":
                    stats.PasswordChanges++;
                    break;
                case "2FA_ENABLED":
                case "2FA_DISABLED":
                case "EMAIL_OTP_ENABLED":
                case "EMAIL_OTP_DISABLED":
                    stats.TwoFactorToggles++;
                    break;
                default:
                    break;
            }

            // Update streak
            var today = DateTime.UtcNow.Date;
            if (stats.LastActiveDate != today)
            {
                var yesterday = today.AddDays(-1);
                if (stats.LastActiveDate == yesterday)
                {
                    stats.CurrentStreak++;
                }
                else if (stats.LastActiveDate != today)
                {
                    stats.CurrentStreak = 1;
                }

                stats.TotalActiveDays++;
                stats.LastActiveDate = today;

                if (stats.CurrentStreak > stats.LongestStreak)
                    stats.LongestStreak = stats.CurrentStreak;
            }

            stats.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording gamification action {Action} for user {UserId}", action, userId);
        }
    }

    public async Task MigrateFromAuditLogsAsync(int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var existing = await context.UserGamificationStats.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
            if (existing != null) return;

            var audits = await context.AuditLogs
                .Where(a => a.UserId == userId)
                .ToListAsync(cancellationToken);

            if (audits.Count == 0) return;

            var activeDates = audits.Select(a => a.Timestamp.Date).Distinct().OrderBy(d => d).ToList();

            // Calculate streak
            int currentStreak = 0, longestStreak = 0, tempStreak = 1;
            var today = DateTime.UtcNow.Date;
            for (int i = 1; i < activeDates.Count; i++)
            {
                if (activeDates[i] == activeDates[i - 1].AddDays(1))
                    tempStreak++;
                else
                {
                    longestStreak = Math.Max(longestStreak, tempStreak);
                    tempStreak = 1;
                }
            }
            longestStreak = Math.Max(longestStreak, tempStreak);

            // Calculate current streak
            var checkDate = activeDates.Contains(today) ? today : today.AddDays(-1);
            for (int i = activeDates.Count - 1; i >= 0; i--)
            {
                if (activeDates[i] == checkDate)
                {
                    currentStreak++;
                    checkDate = checkDate.AddDays(-1);
                }
                else if (activeDates[i] < checkDate)
                    break;
            }

            var stats = new UserGamificationStats
            {
                UserId = userId,
                TotalMovements = audits.Count(a => a.Action == "STOCK_MOVEMENT"),
                TotalScans = audits.Count(a => a.Action == "STOCK_MOVEMENT" && a.Details != null && a.Details.Contains("Scan")),
                ProductsCreated = audits.Count(a => a.Action == "PRODUCT_CREATED"),
                ProductsUpdated = audits.Count(a => a.Action == "PRODUCT_UPDATED"),
                ProductsDeleted = audits.Count(a => a.Action == "PRODUCT_DELETED"),
                TotalLogins = audits.Count(a => a.Action is "LOGIN_SUCCESS" or "PASSKEY_LOGIN_SUCCESS" or "MAGIC_LINK_LOGIN"),
                CategoriesCreated = audits.Count(a => a.Action == "CATEGORY_CREATED"),
                StorageLocationsCreated = audits.Count(a => a.Action == "STORAGE_LOCATION_CREATED"),
                RoomsCreated = audits.Count(a => a.Action == "ROOM_CREATED"),
                ImportsCompleted = audits.Count(a => a.Action is "IMPORT_SUCCESS" or "DATA_IMPORT"),
                ExportsCompleted = audits.Count(a => a.Action is "EXPORT" or "DATA_EXPORT"),
                PasswordChanges = audits.Count(a => a.Action is "PASSWORD_CHANGED" or "PASSWORD_RESET_SUCCESS"),
                TwoFactorToggles = audits.Count(a => a.Action is "2FA_ENABLED" or "2FA_DISABLED" or "EMAIL_OTP_ENABLED" or "EMAIL_OTP_DISABLED"),
                CurrentStreak = currentStreak,
                LongestStreak = longestStreak,
                TotalActiveDays = activeDates.Count,
                LastActiveDate = activeDates.LastOrDefault(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.UserGamificationStats.Add(stats);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Migrated gamification stats for user {UserId}: {XP} XP from {AuditCount} audit logs",
                userId, CalculateXP(stats), audits.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error migrating gamification stats for user {UserId}", userId);
        }
    }

    public async Task<UserGamificationProfile> GetUserProfileAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var stats = await GetOrCreateStatsAsync(context, userId);

        var xp = CalculateXP(stats);
        var (level, xpForNext, xpInLevel) = CalculateLevel(xp);

        // Monthly/weekly values from remaining audit logs (best effort)
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var startOfWeek = now.AddDays(-(int)now.DayOfWeek + (int)DayOfWeek.Monday);
        if (now.DayOfWeek == DayOfWeek.Sunday) startOfWeek = startOfWeek.AddDays(-7);
        startOfWeek = new DateTime(startOfWeek.Year, startOfWeek.Month, startOfWeek.Day, 0, 0, 0, DateTimeKind.Utc);

        var monthlyMovements = await context.AuditLogs
            .CountAsync(a => a.UserId == userId && a.Action == "STOCK_MOVEMENT" && a.Timestamp >= startOfMonth, cancellationToken);
        var weeklyMovements = await context.AuditLogs
            .CountAsync(a => a.UserId == userId && a.Action == "STOCK_MOVEMENT" && a.Timestamp >= startOfWeek, cancellationToken);

        return new UserGamificationProfile
        {
            UserId = userId,
            TotalXP = xp,
            Level = level,
            XPForNextLevel = xpForNext,
            XPInCurrentLevel = xpInLevel,
            TotalMovements = stats.TotalMovements,
            MonthlyMovements = monthlyMovements,
            WeeklyMovements = weeklyMovements,
            TotalScans = stats.TotalScans,
            ProductsCreated = stats.ProductsCreated,
            ProductsUpdated = stats.ProductsUpdated,
            CategoriesCreated = stats.CategoriesCreated,
            StorageLocationsCreated = stats.StorageLocationsCreated,
            RoomsCreated = stats.RoomsCreated,
            ImportsCompleted = stats.ImportsCompleted,
            ExportsCompleted = stats.ExportsCompleted,
            TotalLogins = stats.TotalLogins,
            TotalActiveDays = stats.TotalActiveDays,
            CurrentStreak = stats.CurrentStreak,
            LongestStreak = stats.LongestStreak,
            MemberSince = await context.Users.Where(u => u.Id == userId).Select(u => u.CreatedAt).FirstOrDefaultAsync(cancellationToken)
        };
    }

    public async Task<List<UserLeaderboardEntry>> GetLeaderboardAsync(int? warehouseId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.UserGamificationStats
            .Include(s => s.User)
            .Where(s => s.User != null && s.User.IsActive && !s.User.IsDeleted);

        if (warehouseId.HasValue)
            query = query.Where(s => s.User!.WarehouseId == warehouseId.Value);

        var allStats = await query.ToListAsync(cancellationToken);

        return allStats
            .Select(s =>
            {
                var xp = CalculateXP(s);
                var (level, _, _) = CalculateLevel(xp);
                return new UserLeaderboardEntry
                {
                    UserId = s.UserId,
                    DisplayName = s.User!.DisplayName,
                    Username = s.User.Username,
                    ProfileImagePath = s.User.ProfileImagePath,
                    XP = xp,
                    Level = level,
                    Movements = s.TotalMovements,
                    Scans = s.TotalScans,
                    ProductsCreated = s.ProductsCreated,
                    LastActivity = s.UpdatedAt
                };
            })
            .Where(e => e.XP > 0)
            .OrderByDescending(e => e.XP)
            .ToList();
    }

    public async Task<List<Achievement>> GetAchievementsAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var stats = await GetOrCreateStatsAsync(context, userId);

        return
        [
            // Stock movements
            new("Erste Schritte", "Erste Lagerbewegung durchgef\u00fchrt", "bi-box-arrow-in-right", 1, stats.TotalMovements, "success"),
            new("Flei\u00dfig", "50 Lagerbewegungen", "bi-lightning-charge", 50, stats.TotalMovements, "primary"),
            new("Poweruser", "200 Lagerbewegungen", "bi-lightning-charge-fill", 200, stats.TotalMovements, "warning"),
            new("Lagermeister", "1.000 Lagerbewegungen", "bi-trophy", 1000, stats.TotalMovements, "danger"),
            new("Logistik-Legende", "5.000 Lagerbewegungen", "bi-gem", 5000, stats.TotalMovements, "danger"),

            // Scanner
            new("Scanner-Neuling", "Ersten Barcode gescannt", "bi-qr-code-scan", 1, stats.TotalScans, "info"),
            new("Scan-Profi", "100 Barcodes gescannt", "bi-upc-scan", 100, stats.TotalScans, "primary"),
            new("Scan-Maschine", "500 Barcodes gescannt", "bi-cpu", 500, stats.TotalScans, "warning"),
            new("Barcode-Terminator", "2.000 Barcodes gescannt", "bi-robot", 2000, stats.TotalScans, "danger"),

            // Products
            new("Anleger", "Erstes Produkt erstellt", "bi-plus-circle", 1, stats.ProductsCreated, "success"),
            new("Katalog-Builder", "25 Produkte erstellt", "bi-collection", 25, stats.ProductsCreated, "primary"),
            new("Datenbank-Architekt", "100 Produkte erstellt", "bi-database", 100, stats.ProductsCreated, "warning"),
            new("Produkt-Fabrik", "500 Produkte erstellt", "bi-building-fill-gear", 500, stats.ProductsCreated, "danger"),

            // Product maintenance
            new("Datenpfleger", "Erstes Produkt aktualisiert", "bi-pencil", 1, stats.ProductsUpdated, "success"),
            new("Qualit\u00e4tssicherer", "50 Produkte aktualisiert", "bi-check2-circle", 50, stats.ProductsUpdated, "primary"),
            new("Perfektionist", "200 Produkte aktualisiert", "bi-patch-check-fill", 200, stats.ProductsUpdated, "warning"),

            // Categories
            new("Ordnungsh\u00fcter", "Erste Kategorie erstellt", "bi-tags", 1, stats.CategoriesCreated, "success"),
            new("Strukturgenie", "10 Kategorien erstellt", "bi-diagram-3", 10, stats.CategoriesCreated, "primary"),

            // Storage locations
            new("Raumplaner", "Ersten Lagerort erstellt", "bi-grid-3x3-gap", 1, stats.StorageLocationsCreated, "success"),
            new("Logistik-Architekt", "20 Lagerorte erstellt", "bi-building", 20, stats.StorageLocationsCreated, "primary"),
            new("Lagerhallen-K\u00f6nig", "50 Lagerorte erstellt", "bi-house-gear-fill", 50, stats.StorageLocationsCreated, "warning"),

            // Rooms
            new("Zimmermann", "Ersten Raum erstellt", "bi-door-open", 1, stats.RoomsCreated, "success"),
            new("Raumgestalter", "5 R\u00e4ume erstellt", "bi-layout-wtf", 5, stats.RoomsCreated, "primary"),
            new("Geb\u00e4udemanager", "10 R\u00e4ume erstellt", "bi-houses", 10, stats.RoomsCreated, "warning"),

            // Import / Export
            new("Importeur", "Erster Import durchgef\u00fchrt", "bi-cloud-upload", 1, stats.ImportsCompleted, "success"),
            new("Daten-Jongleur", "10 Imports durchgef\u00fchrt", "bi-arrow-repeat", 10, stats.ImportsCompleted, "primary"),
            new("Exporteur", "Erster Export durchgef\u00fchrt", "bi-cloud-download", 1, stats.ExportsCompleted, "success"),
            new("Berichterstatter", "10 Exports durchgef\u00fchrt", "bi-file-earmark-spreadsheet", 10, stats.ExportsCompleted, "primary"),

            // Logins
            new("Willkommen", "Erster Login", "bi-door-open", 1, stats.TotalLogins, "success"),
            new("Regelm\u00e4\u00dfig", "50 Logins", "bi-key", 50, stats.TotalLogins, "info"),
            new("Dauergast", "200 Logins", "bi-key-fill", 200, stats.TotalLogins, "primary"),
            new("Unentbehrlich", "500 Logins", "bi-person-check-fill", 500, stats.TotalLogins, "warning"),

            // Streaks
            new("Warm-up", "3 Tage Streak", "bi-fire", 3, stats.CurrentStreak, "success"),
            new("Auf Kurs", "7 Tage Streak", "bi-graph-up-arrow", 7, stats.CurrentStreak, "primary"),
            new("Eiserne Disziplin", "14 Tage Streak", "bi-shield-fill-check", 14, stats.CurrentStreak, "warning"),
            new("Unaufhaltsam", "30 Tage Streak", "bi-rocket-takeoff-fill", 30, stats.CurrentStreak, "danger"),

            // Longest streak
            new("Bestleistung 7", "L\u00e4ngster Streak: 7 Tage", "bi-bookmark-star", 7, stats.LongestStreak, "info"),
            new("Bestleistung 30", "L\u00e4ngster Streak: 30 Tage", "bi-bookmark-star-fill", 30, stats.LongestStreak, "primary"),
            new("Marathon-Rekord", "L\u00e4ngster Streak: 90 Tage", "bi-trophy-fill", 90, stats.LongestStreak, "warning"),

            // Activity
            new("Stammgast", "An 7 Tagen aktiv", "bi-calendar-check", 7, stats.TotalActiveDays, "info"),
            new("Dauerbrenner", "An 30 Tagen aktiv", "bi-calendar-heart", 30, stats.TotalActiveDays, "primary"),
            new("Veteran", "An 100 Tagen aktiv", "bi-award", 100, stats.TotalActiveDays, "warning"),
            new("Legende", "An 365 Tagen aktiv", "bi-star-fill", 365, stats.TotalActiveDays, "danger"),

            // Security
            new("Sicherheitsbewusst", "Passwort ge\u00e4ndert", "bi-shield-lock", 1, stats.PasswordChanges, "success"),
            new("Festung", "2FA aktiviert/konfiguriert", "bi-shield-fill-check", 1, stats.TwoFactorToggles, "primary"),

            // Milestones (total XP)
            new("Bronze", "100 XP erreicht", "bi-circle-fill text-warning", 100,
                CalculateXP(stats), "warning"),
            new("Silber", "500 XP erreicht", "bi-circle-fill", 500,
                CalculateXP(stats), "secondary"),
            new("Gold", "2.000 XP erreicht", "bi-circle-fill text-warning", 2000,
                CalculateXP(stats), "warning"),
            new("Diamant", "10.000 XP erreicht", "bi-diamond-fill", 10000,
                CalculateXP(stats), "info"),
        ];
    }

    public async Task<UserStreakInfo> GetStreakInfoAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var stats = await GetOrCreateStatsAsync(context, userId);

        return new UserStreakInfo
        {
            CurrentStreak = stats.CurrentStreak,
            LongestStreak = stats.LongestStreak,
            TotalActiveDays = stats.TotalActiveDays,
            IsActiveToday = stats.LastActiveDate == DateTime.UtcNow.Date
        };
    }

    private static async Task<UserGamificationStats> GetOrCreateStatsAsync(InventoryDbContext context, int userId, CancellationToken cancellationToken = default)
    {
        var stats = await context.UserGamificationStats.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (stats == null)
        {
            stats = new UserGamificationStats { UserId = userId };
            context.UserGamificationStats.Add(stats);
            await context.SaveChangesAsync(cancellationToken);
        }
        return stats;
    }

    internal static int CalculateXP(UserGamificationStats s)
    {
        return (s.TotalMovements * 10)
            + (s.TotalScans * 15)
            + (s.ProductsCreated * 25)
            + (s.ProductsUpdated * 5)
            + (s.ProductsDeleted * 5)
            + (s.CategoriesCreated * 20)
            + (s.StorageLocationsCreated * 15)
            + (s.RoomsCreated * 30)
            + (s.ImportsCompleted * 50)
            + (s.ExportsCompleted * 10)
            + (s.TotalLogins * 5)
            + (s.TotalActiveDays * 20)
            + (s.LongestStreak * 10)
            + (s.PasswordChanges * 15)
            + (s.TwoFactorToggles * 25);
    }

    internal static (int level, int xpForNext, int xpInLevel) CalculateLevel(int totalXP)
    {
        var level = 1;
        var xpUsed = 0;

        while (true)
        {
            var xpNeeded = level * 100;
            if (xpUsed + xpNeeded > totalXP)
            {
                var xpInLevel = totalXP - xpUsed;
                return (level, xpNeeded, xpInLevel);
            }
            xpUsed += xpNeeded;
            level++;
        }
    }
}

// DTOs

public sealed class UserGamificationProfile
{
    public int UserId { get; set; }
    public int TotalXP { get; set; }
    public int Level { get; set; }
    public int XPForNextLevel { get; set; }
    public int XPInCurrentLevel { get; set; }
    public int TotalMovements { get; set; }
    public int MonthlyMovements { get; set; }
    public int WeeklyMovements { get; set; }
    public int TotalScans { get; set; }
    public int ProductsCreated { get; set; }
    public int ProductsUpdated { get; set; }
    public int CategoriesCreated { get; set; }
    public int StorageLocationsCreated { get; set; }
    public int RoomsCreated { get; set; }
    public int ImportsCompleted { get; set; }
    public int ExportsCompleted { get; set; }
    public int TotalLogins { get; set; }
    public int TotalActiveDays { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateTime MemberSince { get; set; }
}

public sealed class UserLeaderboardEntry
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? ProfileImagePath { get; set; }
    public int XP { get; set; }
    public int Level { get; set; }
    public int Movements { get; set; }
    public int Scans { get; set; }
    public int ProductsCreated { get; set; }
    public DateTime LastActivity { get; set; }
}

public sealed class Achievement
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Icon { get; set; }
    public int RequiredCount { get; set; }
    public int CurrentCount { get; set; }
    public string Color { get; set; }
    public bool IsUnlocked => CurrentCount >= RequiredCount;
    public double Progress => RequiredCount > 0 ? Math.Min(1.0, (double)CurrentCount / RequiredCount) : 0;

    public Achievement(string name, string description, string icon, int required, int current, string color)
    {
        Name = name;
        Description = description;
        Icon = icon;
        RequiredCount = required;
        CurrentCount = current;
        Color = color;
    }
}

public sealed class UserStreakInfo
{
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public int TotalActiveDays { get; set; }
    public bool IsActiveToday { get; set; }
}
