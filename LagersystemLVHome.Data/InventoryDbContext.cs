using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Data;

public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Ensures all DateTime values are stored with Kind=Utc.
    /// PostgreSQL/Npgsql requires DateTimeKind.Utc for 'timestamp with time zone'.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeDateTimeKinds();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NormalizeDateTimeKinds();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void NormalizeDateTimeKinds()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                foreach (var prop in entry.Properties)
                {
                    if (prop.CurrentValue is DateTime dt && dt.Kind == DateTimeKind.Unspecified)
                    {
                        prop.CurrentValue = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                    }
                }
            }
        }
    }

    public DbSet<Product> Products { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<StockMovement> StockMovements { get; set; }
    public DbSet<StorageLocation> StorageLocations { get; set; }
    public DbSet<Warehouse> Warehouses { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Room> Rooms { get; set; }
    public DbSet<ProductStorageLocation> ProductStorageLocations { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<UserNotificationSettings> UserNotificationSettings { get; set; }
    public DbSet<ProductBatch> ProductBatches { get; set; }
    public DbSet<ApiKey> ApiKeys { get; set; }
    public DbSet<PageView> PageViews { get; set; }
    public DbSet<ApiRequest> ApiRequests { get; set; }
    public DbSet<PerformanceMetric> PerformanceMetrics { get; set; }
    public DbSet<UserActivity> UserActivities { get; set; }
    public DbSet<ProductPrice> ProductPrices { get; set; }

    // Backup System
    public DbSet<BackupSettings> BackupSettings { get; set; }
    public DbSet<BackupProvider> BackupProviders { get; set; }
    public DbSet<BackupHistory> BackupHistory { get; set; }

    // Key-Backup System (uses existing local providers)
    public DbSet<KeyBackupHistory> KeyBackupHistory { get; set; }

    // System Settings (Encryption Keys, etc.)
    public DbSet<SystemSetting> SystemSettings { get; set; }

    // Session Management
    public DbSet<UserSession> UserSessions { get; set; }
    public DbSet<SecurityEvent> SecurityEvents { get; set; }
    public DbSet<SessionActivity> SessionActivities { get; set; }
    public DbSet<GdprCleanupHistory> GdprCleanupHistory { get; set; }

    // Passwordless Login & IP Access Rules
    public DbSet<MagicLinkToken> MagicLinkTokens { get; set; }
    public DbSet<UserIpAccessRule> UserIpAccessRules { get; set; }

    // WebAuthn / Passkeys
    public DbSet<UserPasskey> UserPasskeys { get; set; }
    public DbSet<WebAuthnChallenge> WebAuthnChallenges { get; set; }

    // E-Mail OTP for 2FA
    public DbSet<EmailOtpToken> EmailOtpTokens { get; set; }

    // Trusted Devices (skip 2FA)
    public DbSet<TrustedDevice> TrustedDevices { get; set; }
    public DbSet<LinkedDeviceFingerprint> LinkedDeviceFingerprints { get; set; }
    public DbSet<UserGamificationStats> UserGamificationStats { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Warehouse Indexes
        modelBuilder.Entity<Warehouse>()
            .HasIndex(w => w.Code)
            .IsUnique();

        // User Indexes
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => new { u.WarehouseId, u.IsActive })
            .HasDatabaseName("IX_Users_WarehouseId_IsActive");

        modelBuilder.Entity<User>()
            .HasIndex(u => new { u.WarehouseId, u.Role, u.IsActive })
            .HasDatabaseName("IX_Users_WarehouseId_Role_IsActive");

        modelBuilder.Entity<User>()
            .HasIndex(u => u.ApprovalStatus)
            .HasFilter("\"ApprovalStatus\" = 0"); // Pending only

        modelBuilder.Entity<User>()
            .HasIndex(u => new { u.IsActive, u.IsDeleted })
            .HasDatabaseName("IX_Users_IsActive_IsDeleted");

        // User Relationships
        modelBuilder.Entity<User>()
            .HasOne(u => u.Warehouse)
            .WithMany(w => w.Users)
            .HasForeignKey(u => u.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<User>()
            .HasOne(u => u.ApprovedBy)
            .WithMany()
            .HasForeignKey(u => u.ApprovedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Product Indexes
        modelBuilder.Entity<Product>()
            .HasIndex(p => p.Barcode)
            .HasDatabaseName("IX_Products_Barcode");

        modelBuilder.Entity<Product>()
            .HasIndex(p => p.Name)
            .HasDatabaseName("IX_Products_Name");

        modelBuilder.Entity<Product>()
            .HasIndex(p => new { p.WarehouseId, p.Barcode })
            .HasDatabaseName("IX_Products_WarehouseId_Barcode");

        modelBuilder.Entity<Product>()
            .HasIndex(p => new { p.WarehouseId, p.CategoryId })
            .HasDatabaseName("IX_Products_WarehouseId_CategoryId");

        modelBuilder.Entity<Product>()
            .HasIndex(p => new { p.WarehouseId, p.Quantity, p.MinQuantity })
            .HasDatabaseName("IX_Products_WarehouseId_Quantity_MinQuantity");

        modelBuilder.Entity<Product>()
            .HasIndex(p => new { p.CategoryId, p.Quantity })
            .HasDatabaseName("IX_Products_CategoryId_Quantity");

        // Low Stock Query Optimization
        modelBuilder.Entity<Product>()
            .HasIndex(p => new { p.WarehouseId, p.Quantity, p.MinQuantity })
            .HasFilter("\"Quantity\" <= \"MinQuantity\"")
            .HasDatabaseName("IX_Products_LowStock");

        // ExpiryDate for best-before monitoring
        modelBuilder.Entity<Product>()
            .HasIndex(p => new { p.ExpiryDate, p.WarehouseId })
            .HasFilter("\"ExpiryDate\" IS NOT NULL")
            .HasDatabaseName("IX_Products_ExpiryDate_WarehouseId");

        // Product Relationships
        modelBuilder.Entity<Product>()
            .HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Product>()
            .HasOne(p => p.Warehouse)
            .WithMany(w => w.Products)
            .HasForeignKey(p => p.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Category Indexes
        modelBuilder.Entity<Category>()
            .HasIndex(c => new { c.WarehouseId, c.Name })
            .HasDatabaseName("IX_Categories_WarehouseId_Name");

        modelBuilder.Entity<Category>()
            .HasIndex(c => new { c.WarehouseId, c.IsActive })
            .HasDatabaseName("IX_Categories_WarehouseId_IsActive");

        // Category Relationships
        modelBuilder.Entity<Category>()
            .HasOne(c => c.Warehouse)
            .WithMany(w => w.Categories)
            .HasForeignKey(c => c.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // StorageLocation Indexes
        modelBuilder.Entity<StorageLocation>()
            .HasIndex(s => new { s.WarehouseId, s.Code })
            .IsUnique()
            .HasDatabaseName("IX_StorageLocations_WarehouseId_Code");

        modelBuilder.Entity<StorageLocation>()
            .HasIndex(s => new { s.WarehouseId, s.Room })
            .HasDatabaseName("IX_StorageLocations_WarehouseId_Room");

        modelBuilder.Entity<StorageLocation>()
            .HasIndex(s => new { s.WarehouseId, s.IsActive })
            .HasDatabaseName("IX_StorageLocations_WarehouseId_IsActive");

        modelBuilder.Entity<StorageLocation>()
            .HasIndex(s => s.QRCode)
            .HasFilter("\"QRCode\" IS NOT NULL")
            .HasDatabaseName("IX_StorageLocations_QRCode");

        // StorageLocation Relationships
        modelBuilder.Entity<StorageLocation>()
            .HasOne(s => s.Warehouse)
            .WithMany(w => w.StorageLocations)
            .HasForeignKey(s => s.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // ProductStorageLocation Indexes
        modelBuilder.Entity<ProductStorageLocation>()
            .HasIndex(psl => new { psl.ProductId, psl.StorageLocationId })
            .IsUnique()
            .HasDatabaseName("IX_ProductStorageLocations_ProductId_StorageLocationId");

        modelBuilder.Entity<ProductStorageLocation>()
            .HasIndex(psl => new { psl.StorageLocationId, psl.Quantity })
            .HasDatabaseName("IX_ProductStorageLocations_StorageLocationId_Quantity");

        modelBuilder.Entity<ProductStorageLocation>()
            .HasIndex(psl => psl.ProductId)
            .HasDatabaseName("IX_ProductStorageLocations_ProductId");

        // ProductStorageLocation Relationships
        modelBuilder.Entity<ProductStorageLocation>()
            .HasOne(psl => psl.Product)
            .WithMany(p => p.ProductStorageLocations)
            .HasForeignKey(psl => psl.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProductStorageLocation>()
            .HasOne(psl => psl.StorageLocation)
            .WithMany(sl => sl.ProductStorageLocations)
            .HasForeignKey(psl => psl.StorageLocationId)
            .OnDelete(DeleteBehavior.Cascade);

        // StockMovement Indexes
        modelBuilder.Entity<StockMovement>()
            .HasIndex(sm => new { sm.WarehouseId, sm.Timestamp })
            .HasDatabaseName("IX_StockMovements_WarehouseId_Timestamp");

        modelBuilder.Entity<StockMovement>()
            .HasIndex(sm => new { sm.ProductId, sm.Timestamp })
            .HasDatabaseName("IX_StockMovements_ProductId_Timestamp");

        modelBuilder.Entity<StockMovement>()
            .HasIndex(sm => new { sm.Type, sm.Timestamp })
            .HasDatabaseName("IX_StockMovements_Type_Timestamp");

        modelBuilder.Entity<StockMovement>()
            .HasIndex(sm => sm.Timestamp)
            .HasDatabaseName("IX_StockMovements_Timestamp");

        // Today's Movements Query Optimization
        modelBuilder.Entity<StockMovement>()
            .HasIndex(sm => new { sm.WarehouseId, sm.Timestamp, sm.Type })
            .HasDatabaseName("IX_StockMovements_TodayQuery");

        // StockMovement Relationships
        modelBuilder.Entity<StockMovement>()
            .HasOne(sm => sm.Product)
            .WithMany(p => p.StockMovements)
            .HasForeignKey(sm => sm.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StockMovement>()
            .HasOne(sm => sm.Warehouse)
            .WithMany(w => w.StockMovements)
            .HasForeignKey(sm => sm.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // ProductBatch Indexes
        modelBuilder.Entity<ProductBatch>()
            .HasIndex(pb => new { pb.ProductId, pb.ExpiryDate })
            .HasDatabaseName("IX_ProductBatches_ProductId_ExpiryDate");

        modelBuilder.Entity<ProductBatch>()
            .HasIndex(pb => new { pb.WarehouseId, pb.ExpiryDate })
            .HasFilter("\"ExpiryDate\" IS NOT NULL")
            .HasDatabaseName("IX_ProductBatches_WarehouseId_ExpiryDate");

        modelBuilder.Entity<ProductBatch>()
            .HasIndex(pb => pb.BatchNumber)
            .HasDatabaseName("IX_ProductBatches_BatchNumber");

        // Expiring Batches Query Optimization
        modelBuilder.Entity<ProductBatch>()
            .HasIndex(pb => new { pb.ExpiryDate, pb.Quantity })
            .HasFilter("\"ExpiryDate\" IS NOT NULL AND \"Quantity\" > 0")
            .HasDatabaseName("IX_ProductBatches_ExpiringBatches");

        // AuditLog Indexes
        modelBuilder.Entity<AuditLog>()
            .HasIndex(al => new { al.WarehouseId, al.Timestamp })
            .HasDatabaseName("IX_AuditLogs_WarehouseId_Timestamp");

        modelBuilder.Entity<AuditLog>()
            .HasIndex(al => new { al.UserId, al.Timestamp })
            .HasDatabaseName("IX_AuditLogs_UserId_Timestamp");

        modelBuilder.Entity<AuditLog>()
            .HasIndex(al => new { al.Entity, al.EntityId })
            .HasDatabaseName("IX_AuditLogs_Entity_EntityId");

        modelBuilder.Entity<AuditLog>()
            .HasIndex(al => new { al.Action, al.Timestamp })
            .HasDatabaseName("IX_AuditLogs_Action_Timestamp");

        modelBuilder.Entity<AuditLog>()
            .HasIndex(al => new { al.Severity, al.Timestamp })
            .HasDatabaseName("IX_AuditLogs_Severity_Timestamp");

        modelBuilder.Entity<AuditLog>()
            .HasIndex(al => al.Timestamp)
            .HasDatabaseName("IX_AuditLogs_Timestamp");

        // AuditLog Relationships
        modelBuilder.Entity<AuditLog>()
            .HasOne(al => al.User)
            .WithMany()
            .HasForeignKey(al => al.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<AuditLog>()
            .HasOne(al => al.Warehouse)
            .WithMany()
            .HasForeignKey(al => al.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Notification Indexes
        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt })
            .HasDatabaseName("IX_Notifications_UserId_IsRead_CreatedAt");

        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.Type, n.IsRead })
            .HasDatabaseName("IX_Notifications_UserId_Type_IsRead");

        modelBuilder.Entity<Notification>()
            .HasIndex(n => n.CreatedAt)
            .HasDatabaseName("IX_Notifications_CreatedAt");

        // Unread Notifications Query Optimization
        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.IsRead })
            .HasFilter("\"IsRead\" = false")
            .HasDatabaseName("IX_Notifications_UnreadNotifications");

        // Notification Relationships
        modelBuilder.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Notification>()
            .HasOne(n => n.Warehouse)
            .WithMany()
            .HasForeignKey(n => n.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // UserNotificationSettings Indexes
        modelBuilder.Entity<UserNotificationSettings>()
            .HasIndex(uns => uns.UserId)
            .IsUnique()
            .HasDatabaseName("IX_UserNotificationSettings_UserId");

        // UserNotificationSettings Relationships
        modelBuilder.Entity<UserNotificationSettings>()
            .HasOne(uns => uns.User)
            .WithMany()
            .HasForeignKey(uns => uns.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ApiKey Indexes
        modelBuilder.Entity<ApiKey>()
            .HasIndex(ak => ak.KeyHash)
            .IsUnique()
            .HasDatabaseName("IX_ApiKeys_KeyHash");

        modelBuilder.Entity<ApiKey>()
            .HasIndex(ak => new { ak.UserId, ak.IsActive })
            .HasDatabaseName("IX_ApiKeys_UserId_IsActive");

        modelBuilder.Entity<ApiKey>()
            .HasIndex(ak => new { ak.IsActive, ak.ExpiresAt })
            .HasDatabaseName("IX_ApiKeys_IsActive_ExpiresAt");

        // Active Keys Query Optimization
        modelBuilder.Entity<ApiKey>()
            .HasIndex(ak => new { ak.IsActive, ak.ExpiresAt })
            .HasFilter("\"IsActive\" = true")
            .HasDatabaseName("IX_ApiKeys_ActiveKeys");

        // ApiKey Relationships
        modelBuilder.Entity<ApiKey>()
            .HasOne(ak => ak.User)
            .WithMany()
            .HasForeignKey(ak => ak.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // PageView Indexes
        modelBuilder.Entity<PageView>()
            .HasIndex(pv => new { pv.UserId, pv.Timestamp })
            .HasDatabaseName("IX_PageViews_UserId_Timestamp");

        modelBuilder.Entity<PageView>()
            .HasIndex(pv => new { pv.SessionId, pv.Timestamp })
            .HasDatabaseName("IX_PageViews_SessionId_Timestamp");

        modelBuilder.Entity<PageView>()
            .HasIndex(pv => pv.Timestamp)
            .HasDatabaseName("IX_PageViews_Timestamp");

        modelBuilder.Entity<PageView>()
            .HasIndex(pv => new { pv.PageUrl, pv.Timestamp })
            .HasDatabaseName("IX_PageViews_PageUrl_Timestamp");

        // Active Sessions Query Optimization
        modelBuilder.Entity<PageView>()
            .HasIndex(pv => new { pv.Timestamp, pv.SessionId })
            .HasDatabaseName("IX_PageViews_ActiveSessions");

        // Analytics Queries Optimization
        modelBuilder.Entity<PageView>()
            .HasIndex(pv => new { pv.DeviceType, pv.Timestamp })
            .HasDatabaseName("IX_PageViews_DeviceType_Timestamp");

        modelBuilder.Entity<PageView>()
            .HasIndex(pv => new { pv.Browser, pv.Timestamp })
            .HasDatabaseName("IX_PageViews_Browser_Timestamp");

        // PageView Relationships
        modelBuilder.Entity<PageView>()
            .HasOne(pv => pv.User)
            .WithMany()
            .HasForeignKey(pv => pv.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ApiRequest Indexes
        modelBuilder.Entity<ApiRequest>()
            .HasIndex(ar => ar.Timestamp)
            .HasDatabaseName("IX_ApiRequests_Timestamp");

        modelBuilder.Entity<ApiRequest>()
            .HasIndex(ar => new { ar.Endpoint, ar.Timestamp })
            .HasDatabaseName("IX_ApiRequests_Endpoint_Timestamp");

        modelBuilder.Entity<ApiRequest>()
            .HasIndex(ar => new { ar.StatusCode, ar.Timestamp })
            .HasDatabaseName("IX_ApiRequests_StatusCode_Timestamp");

        modelBuilder.Entity<ApiRequest>()
            .HasIndex(ar => new { ar.IsError, ar.Timestamp })
            .HasDatabaseName("IX_ApiRequests_IsError_Timestamp");

        // Performance Analysis Query
        modelBuilder.Entity<ApiRequest>()
            .HasIndex(ar => new { ar.DurationMs, ar.Timestamp })
            .HasDatabaseName("IX_ApiRequests_DurationMs_Timestamp");

        // PerformanceMetric Indexes
        modelBuilder.Entity<PerformanceMetric>()
            .HasIndex(pm => pm.Timestamp)
            .HasDatabaseName("IX_PerformanceMetrics_Timestamp");

        modelBuilder.Entity<PerformanceMetric>()
            .HasIndex(pm => new { pm.Timestamp, pm.CpuUsagePercent })
            .HasDatabaseName("IX_PerformanceMetrics_Timestamp_CpuUsagePercent");

        // UserActivity Indexes
        modelBuilder.Entity<UserActivity>()
            .HasIndex(ua => new { ua.UserId, ua.Timestamp })
            .HasDatabaseName("IX_UserActivities_UserId_Timestamp");

        modelBuilder.Entity<UserActivity>()
            .HasIndex(ua => new { ua.ActivityType, ua.EntityType })
            .HasDatabaseName("IX_UserActivities_ActivityType_EntityType");

        modelBuilder.Entity<UserActivity>()
            .HasIndex(ua => ua.Timestamp)
            .HasDatabaseName("IX_UserActivities_Timestamp");

        // UserActivity Relationships
        modelBuilder.Entity<UserActivity>()
            .HasOne(ua => ua.User)
            .WithMany()
            .HasForeignKey(ua => ua.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ProductPrice Indexes
        modelBuilder.Entity<ProductPrice>()
            .HasIndex(pp => new { pp.ProductId, pp.ValidFrom, pp.ValidTo })
            .HasDatabaseName("IX_ProductPrices_ProductId_ValidFrom_ValidTo");

        modelBuilder.Entity<ProductPrice>()
            .HasIndex(pp => new { pp.ProductId, pp.CreatedAt })
            .HasDatabaseName("IX_ProductPrices_ProductId_CreatedAt");

        modelBuilder.Entity<ProductPrice>()
            .HasIndex(pp => new { pp.WarehouseId, pp.ValidFrom })
            .HasDatabaseName("IX_ProductPrices_WarehouseId_ValidFrom");

        // Current Price Query Optimization
        modelBuilder.Entity<ProductPrice>()
            .HasIndex(pp => new { pp.ProductId, pp.ValidFrom })
            .HasFilter("\"ValidTo\" IS NULL")
            .HasDatabaseName("IX_ProductPrices_CurrentPrice");

        // ProductPrice Relationships
        modelBuilder.Entity<ProductPrice>()
            .HasOne(pp => pp.Product)
            .WithMany(p => p.PriceHistory)
            .HasForeignKey(pp => pp.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProductPrice>()
            .HasOne(pp => pp.Warehouse)
            .WithMany()
            .HasForeignKey(pp => pp.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // BackupProvider Indexes
        modelBuilder.Entity<BackupProvider>()
            .HasIndex(bp => bp.Name)
            .HasDatabaseName("IX_BackupProviders_Name");

        modelBuilder.Entity<BackupProvider>()
            .HasIndex(bp => new { bp.Type, bp.Enabled })
            .HasDatabaseName("IX_BackupProviders_Type_Enabled");

        // BackupHistory Indexes
        modelBuilder.Entity<BackupHistory>()
            .HasIndex(bh => bh.BackupProviderId)
            .HasDatabaseName("IX_BackupHistory_BackupProviderId");

        modelBuilder.Entity<BackupHistory>()
            .HasIndex(bh => bh.BackupDate)
            .HasDatabaseName("IX_BackupHistory_BackupDate");

        modelBuilder.Entity<BackupHistory>()
            .HasIndex(bh => new { bh.Status, bh.BackupDate })
            .HasDatabaseName("IX_BackupHistory_Status_BackupDate");

        modelBuilder.Entity<BackupHistory>()
            .HasIndex(bh => bh.RetentionType)
            .HasDatabaseName("IX_BackupHistory_RetentionType");

        modelBuilder.Entity<BackupHistory>()
            .HasIndex(bh => new { bh.BackupProviderId, bh.RetentionType, bh.BackupDate })
            .HasDatabaseName("IX_BackupHistory_CleanupQuery");

        // BackupHistory Relationships
        modelBuilder.Entity<BackupHistory>()
            .HasOne(bh => bh.BackupProvider)
            .WithMany(bp => bp.BackupHistories)
            .HasForeignKey(bh => bh.BackupProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        // SystemSetting Indexes
        modelBuilder.Entity<SystemSetting>()
            .HasIndex(ss => ss.Key)
            .IsUnique()
            .HasDatabaseName("IX_SystemSettings_Key");

        modelBuilder.Entity<SystemSetting>()
            .HasIndex(ss => ss.CreatedAt)
            .HasDatabaseName("IX_SystemSettings_CreatedAt");

        // KeyBackupHistory Indexes
        modelBuilder.Entity<KeyBackupHistory>()
            .HasIndex(kbh => kbh.BackupDate)
            .HasDatabaseName("IX_KeyBackupHistory_BackupDate");

        modelBuilder.Entity<KeyBackupHistory>()
            .HasIndex(kbh => new { kbh.Status, kbh.BackupDate })
            .HasDatabaseName("IX_KeyBackupHistory_Status_BackupDate");

        modelBuilder.Entity<KeyBackupHistory>()
            .HasIndex(kbh => kbh.BackupProviderId)
            .HasDatabaseName("IX_KeyBackupHistory_BackupProviderId");

        // KeyBackupHistory Relationships
        modelBuilder.Entity<KeyBackupHistory>()
            .HasOne(kbh => kbh.BackupProvider)
            .WithMany()
            .HasForeignKey(kbh => kbh.BackupProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        // UserSession Indexes
        modelBuilder.Entity<UserSession>()
            .HasIndex(us => us.SessionId)
            .IsUnique()
            .HasDatabaseName("IX_UserSessions_SessionId");

        modelBuilder.Entity<UserSession>()
            .HasIndex(us => new { us.UserId, us.IsActive })
            .HasDatabaseName("IX_UserSessions_UserId_IsActive");

        modelBuilder.Entity<UserSession>()
            .HasIndex(us => new { us.WarehouseId, us.IsActive, us.LastActivity })
            .HasDatabaseName("IX_UserSessions_WarehouseId_IsActive_LastActivity");

        modelBuilder.Entity<UserSession>()
            .HasIndex(us => new { us.IsSuspicious, us.RiskLevel })
            .HasDatabaseName("IX_UserSessions_IsSuspicious_RiskLevel");

        modelBuilder.Entity<UserSession>()
            .HasIndex(us => us.IpAddress)
            .HasDatabaseName("IX_UserSessions_IpAddress");

        modelBuilder.Entity<UserSession>()
            .HasIndex(us => new { us.IsVpn, us.IsActive })
            .HasDatabaseName("IX_UserSessions_IsVpn_IsActive");

        // Active Sessions Query Optimization
        modelBuilder.Entity<UserSession>()
            .HasIndex(us => new { us.IsActive, us.LastActivity })
            .HasFilter("\"IsActive\" = true")
            .HasDatabaseName("IX_UserSessions_ActiveSessions");

        // UserSession Relationships
        modelBuilder.Entity<UserSession>()
            .HasOne(us => us.User)
            .WithMany()
            .HasForeignKey(us => us.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserSession>()
            .HasOne(us => us.Warehouse)
            .WithMany()
            .HasForeignKey(us => us.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserSession>()
            .HasOne(us => us.TerminatedBy)
            .WithMany()
            .HasForeignKey(us => us.TerminatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // SessionActivity Indexes
        modelBuilder.Entity<SessionActivity>()
            .HasIndex(sa => new { sa.SessionId, sa.Timestamp })
            .HasDatabaseName("IX_SessionActivities_SessionId_Timestamp");

        modelBuilder.Entity<SessionActivity>()
            .HasIndex(sa => sa.Timestamp)
            .HasDatabaseName("IX_SessionActivities_Timestamp");

        modelBuilder.Entity<SessionActivity>()
            .HasIndex(sa => new { sa.IsAnomaly, sa.Timestamp })
            .HasDatabaseName("IX_SessionActivities_IsAnomaly_Timestamp");

        // SessionActivity Relationships
        modelBuilder.Entity<SessionActivity>()
            .HasOne(sa => sa.Session)
            .WithMany(us => us.Activities)
            .HasForeignKey(sa => sa.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // SecurityEvent Indexes
        modelBuilder.Entity<SecurityEvent>()
            .HasIndex(se => se.Timestamp)
            .HasDatabaseName("IX_SecurityEvents_Timestamp");

        modelBuilder.Entity<SecurityEvent>()
            .HasIndex(se => new { se.EventType, se.Timestamp })
            .HasDatabaseName("IX_SecurityEvents_EventType_Timestamp");

        modelBuilder.Entity<SecurityEvent>()
            .HasIndex(se => new { se.Severity, se.IsResolved })
            .HasDatabaseName("IX_SecurityEvents_Severity_IsResolved");

        modelBuilder.Entity<SecurityEvent>()
            .HasIndex(se => new { se.SessionId, se.Timestamp })
            .HasDatabaseName("IX_SecurityEvents_SessionId_Timestamp");

        modelBuilder.Entity<SecurityEvent>()
            .HasIndex(se => new { se.UserId, se.Timestamp })
            .HasDatabaseName("IX_SecurityEvents_UserId_Timestamp");

        // Unresolved Security Events Query
        modelBuilder.Entity<SecurityEvent>()
            .HasIndex(se => new { se.IsResolved, se.Severity, se.Timestamp })
            .HasFilter("\"IsResolved\" = false")
            .HasDatabaseName("IX_SecurityEvents_UnresolvedEvents");

        // SecurityEvent Relationships
        modelBuilder.Entity<SecurityEvent>()
            .HasOne(se => se.Session)
            .WithMany()
            .HasForeignKey(se => se.SessionId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<SecurityEvent>()
            .HasOne(se => se.User)
            .WithMany()
            .HasForeignKey(se => se.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<SecurityEvent>()
            .HasOne(se => se.ResolvedBy)
            .WithMany()
            .HasForeignKey(se => se.ResolvedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // MagicLinkToken Indexes
        modelBuilder.Entity<MagicLinkToken>()
            .HasIndex(m => m.Token)
            .IsUnique()
            .HasDatabaseName("IX_MagicLinkTokens_Token");

        modelBuilder.Entity<MagicLinkToken>()
            .HasIndex(m => new { m.UserId, m.ExpiresAt })
            .HasDatabaseName("IX_MagicLinkTokens_UserId_ExpiresAt");

        // MagicLinkToken Relationships
        modelBuilder.Entity<MagicLinkToken>()
            .HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // UserIpAccessRule Indexes
        modelBuilder.Entity<UserIpAccessRule>()
            .HasIndex(r => new { r.UserId, r.IsActive })
            .HasDatabaseName("IX_UserIpAccessRules_UserId_IsActive");

        // UserIpAccessRule Relationships
        modelBuilder.Entity<UserIpAccessRule>()
            .HasOne(r => r.User)
            .WithMany(u => u.IpAccessRules)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserIpAccessRule>()
            .HasOne(r => r.CreatedBy)
            .WithMany()
            .HasForeignKey(r => r.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<UserIpAccessRule>()
            .HasOne(r => r.UpdatedBy)
            .WithMany()
            .HasForeignKey(r => r.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // UserPasskey Indexes
        modelBuilder.Entity<UserPasskey>()
            .HasIndex(p => p.CredentialId)
            .HasDatabaseName("IX_UserPasskeys_CredentialId");

        modelBuilder.Entity<UserPasskey>()
            .HasIndex(p => new { p.UserId, p.IsActive })
            .HasDatabaseName("IX_UserPasskeys_UserId_IsActive");

        // Unique constraint for active credentials
        modelBuilder.Entity<UserPasskey>()
            .HasIndex(p => p.CredentialId)
            .IsUnique()
            .HasFilter("\"IsActive\" = true")
            .HasDatabaseName("IX_UserPasskeys_CredentialId_Unique");

        // UserPasskey Relationships
        modelBuilder.Entity<UserPasskey>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // WebAuthnChallenge Indexes
        modelBuilder.Entity<WebAuthnChallenge>()
            .HasIndex(c => c.SessionId)
            .HasDatabaseName("IX_WebAuthnChallenges_SessionId");

        modelBuilder.Entity<WebAuthnChallenge>()
            .HasIndex(c => c.ExpiresAt)
            .HasDatabaseName("IX_WebAuthnChallenges_ExpiresAt");

        // WebAuthnChallenge Relationships
        modelBuilder.Entity<WebAuthnChallenge>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // TrustedDevice Indexes
        modelBuilder.Entity<TrustedDevice>()
            .HasIndex(t => new { t.UserId, t.DeviceFingerprint, t.IsActive })
            .HasDatabaseName("IX_TrustedDevices_UserId_Fingerprint_IsActive");

        modelBuilder.Entity<TrustedDevice>()
            .HasIndex(t => t.ExpiresAt)
            .HasDatabaseName("IX_TrustedDevices_ExpiresAt");

        // TrustedDevice Relationships
        modelBuilder.Entity<TrustedDevice>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // LinkedDeviceFingerprint Configuration
        modelBuilder.Entity<LinkedDeviceFingerprint>()
            .HasOne(l => l.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LinkedDeviceFingerprint>()
            .HasIndex(l => new { l.UserId, l.PrimaryFingerprint })
            .HasDatabaseName("IX_LinkedDeviceFingerprints_UserId_Primary");

        modelBuilder.Entity<LinkedDeviceFingerprint>()
            .HasIndex(l => new { l.UserId, l.LinkedFingerprint })
            .HasDatabaseName("IX_LinkedDeviceFingerprints_UserId_Linked");

        // UserGamificationStats Configuration
        modelBuilder.Entity<UserGamificationStats>()
            .HasOne(g => g.User)
            .WithMany()
            .HasForeignKey(g => g.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserGamificationStats>()
            .HasIndex(g => g.UserId)
            .IsUnique()
            .HasDatabaseName("IX_UserGamificationStats_UserId");
    }
}
