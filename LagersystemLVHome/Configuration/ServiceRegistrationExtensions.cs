using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data.Repositories;
using LagersystemLVHome.Infrastructure.HostedServices;
using LagersystemLVHome.Application.Services;

namespace LagersystemLVHome;

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Repository pattern
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IStorageLocationRepository, StorageLocationRepository>();

        // Business logic
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IQRCodeService, QRCodeService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<IUserRegistrationService, UserRegistrationService>();
        services.AddScoped<ITwoFactorManagementService, TwoFactorManagementService>();
        services.AddScoped<StorageLocationService>();
        services.AddScoped<IStorageDistributionService, StorageDistributionService>();
        services.AddScoped<ILocationQueryService, LocationQueryService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<ISetupService, SetupService>();
        services.AddScoped<IAdminQueryService, AdminQueryService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();

        // Security & audit
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IGdprService, GdprService>();
        services.AddScoped<IDeviceFingerprintService, DeviceFingerprintService>();
        services.AddScoped<InputSanitizationService>();
        services.AddScoped<TamperProofAuditService>();
        // Typed HttpClient (registers IPwnedPasswordService -> PwnedPasswordService as transient with pooled HttpClient).
        // Do NOT also call AddScoped for the same interface – that would override the typed-client registration
        // and bypass HttpClientFactory pooling, risking socket exhaustion.
        services.AddHttpClient<IPwnedPasswordService, PwnedPasswordService>();
        services.AddScoped<ISecureConfigurationService, SecureConfigurationService>();
        services.AddScoped<IEncryptionService, EncryptionService>();

        // Thumbmarkjs browser fingerprinting
        Soenneker.Blazor.Thumbmarkjs.Registrars.ThumbmarkjsInteropRegistrar.AddThumbmarkjsInteropAsScoped(services);

        // Authentication & authorization
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddScoped<IEmailOtpService, EmailOtpService>();
        services.AddScoped<ITrustedDeviceService, TrustedDeviceService>();
        services.AddScoped<IPasswordValidationService, PasswordValidationService>();
        services.AddScoped<IPasswordlessLoginService, PasswordlessLoginService>();
        services.AddScoped<IUserIpAccessService, UserIpAccessService>();
        services.AddScoped<IWebAuthnService, WebAuthnService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();

        // Communication
        services.AddScoped<IEmailService, EmailService>();
        services.AddHttpClient();
        services.AddScoped<ITeamsService, TeamsService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddSingleton<INotificationEventService, NotificationEventService>();

        // Session management
        services.AddScoped<ISessionManagementService, SessionManagementService>();
        services.AddScoped<ITeamPresenceService, TeamPresenceService>();
        services.AddSingleton<ISessionMonitorService, SessionMonitorService>();

        // Database health
        services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();

        // Dashboard & reporting
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IApplicationInsightsService, ApplicationInsightsService>();
        services.AddSingleton<IApplicationUptimeService, ApplicationUptimeService>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IPdfReportService, PdfReportService>();
        services.AddScoped<IPriceHistoryService, PriceHistoryService>();
        services.AddScoped<IExpiryService, ExpiryService>();

        // Caching
        services.AddScoped<ICacheService, CacheService>();

        // UI services
        services.AddScoped<IToastService, ToastService>();
        services.AddScoped<IKeyboardShortcutService, KeyboardShortcutService>();
        services.AddScoped<ICameraService, CameraService>();
        services.AddScoped<IGamificationService, GamificationService>();
        services.AddScoped<IImageService, ImageService>();

        // Backup
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IBackupManagementService, BackupManagementService>();
        services.AddScoped<IDatabaseRestoreService, DatabaseRestoreService>();
        services.AddScoped<IKeyBackupService, KeyBackupService>();
        services.AddScoped<JsonBackupHelper>();
        services.AddBackupProviders();

        // ML services
        services.AddScoped<Infrastructure.ML.Services.IAnomalyDetectionService, Infrastructure.ML.Services.AnomalyDetectionService>();
        services.AddScoped<Infrastructure.ML.Services.ISecurityRiskService, Infrastructure.ML.Services.SecurityRiskService>();
        services.AddScoped<Infrastructure.ML.Services.ICategoryPredictionService, Infrastructure.ML.Services.CategoryPredictionService>();
        services.AddSingleton<Infrastructure.ML.Keywords.CategoryKeywordService>();

        // Misc
        services.AddScoped<CategorySeederService>();
        services.AddSingleton<IGeoLocationService, GeoLocationService>();
        services.AddScoped<ICloudflareService, CloudflareService>();
        services.AddHttpClient<IBarcodeApiService, BarcodeApiService>();

        // GDPR
        services.Configure<GdprSettings>(configuration.GetSection("GdprSettings"));
        services.AddScoped<IGdprCleanupService, GdprCleanupService>();

        // Rate limiting & security alerts
        services.Configure<RateLimitSettings>(configuration.GetSection("RateLimitSettings"));
        services.AddSingleton<IRateLimitService, RateLimitService>();
        services.Configure<SecurityAlertsSettings>(configuration.GetSection("SecurityAlerts"));
        services.AddScoped<ISecurityAlertService, SecurityAlertService>();
        services.Configure<VpnDetectionConfig>(configuration.GetSection("VpnDetection"));
        services.Configure<CloudflareSettings>(configuration.GetSection("CloudflareSettings"));

        // External configuration
        services.Configure<TeamsSettings>(configuration.GetSection("TeamsSettings"));
        services.Configure<NotificationChannels>(configuration.GetSection("NotificationChannels"));
        services.Configure<PrivacyPolicySettings>(configuration.GetSection("PrivacyPolicySettings"));

        // Ollama AI
        services.AddHttpClient("Ollama", client =>
        {
            client.BaseAddress = new Uri(configuration["OllamaSettings:BaseUrl"] ?? "http://localhost:11434");
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddScoped<IOllamaService, OllamaService>();

        return services;
    }

    public static IServiceCollection AddBackgroundServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHostedService<Infrastructure.HostedServices.BackupHostedService>();
        services.AddHostedService<Infrastructure.HostedServices.BackupCleanupHostedService>();
        services.AddHostedService<Infrastructure.HostedServices.KeyBackupHostedService>();
        services.AddHostedService<NotificationHostedService>();
        services.AddHostedService<Infrastructure.HostedServices.SessionCleanupHostedService>();
        services.AddHostedService<GdprCleanupHostedService>();
        services.AddHostedService<WeeklyReportService>();
        services.AddHostedService<Infrastructure.HostedServices.SecurityMonitoringHostedService>();

        if (configuration.GetValue<bool>("CloudflareSettings:AutoEscalation:Enabled", false))
        {
            services.AddHostedService<Infrastructure.HostedServices.CloudflareAutoEscalationService>();
        }

        return services;
    }

    private static void AddBackupProviders(this IServiceCollection services)
    {
        services.AddScoped<Application.Services.BackupProviders.IBackupProviderUploader,
            Application.Services.BackupProviders.LocalBackupProviderUploader>();
        services.AddScoped<Application.Services.BackupProviders.IBackupProviderUploader,
            Application.Services.BackupProviders.NetworkShareProviderUploader>();
        services.AddScoped<Application.Services.BackupProviders.IBackupProviderUploader,
            Application.Services.BackupProviders.AzureBlobProviderUploader>();
        services.AddScoped<Application.Services.BackupProviders.IBackupProviderUploader,
            Application.Services.BackupProviders.AwsS3ProviderUploader>();
        services.AddScoped<Application.Services.BackupProviders.IBackupProviderUploader,
            Application.Services.BackupProviders.GoogleDriveProviderUploader>();
        services.AddScoped<Application.Services.BackupProviders.IBackupProviderUploader,
            Application.Services.BackupProviders.OneDriveProviderUploader>();
        services.AddScoped<Application.Services.BackupProviders.IBackupProviderUploader,
            Application.Services.BackupProviders.CloudflareR2ProviderUploader>();
        services.AddScoped<Application.Services.BackupProviders.BackupProviderFactory>();
    }
}
