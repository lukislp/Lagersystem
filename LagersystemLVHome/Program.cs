using LagersystemLVHome;
using LagersystemLVHome.Components;
using Toolbelt.Blazor.Extensions.DependencyInjection;
using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

// Forwarded headers for reverse proxy support
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.ForwardLimit = null;
});

// IIS integration. AllowSynchronousIO is intentionally NOT enabled – the
// codebase no longer performs synchronous Stream.Read/Write on Request/Response
// bodies. Re-enable only if a specific endpoint truly needs it.
builder.Services.Configure<IISServerOptions>(options =>
{
    options.AutomaticAuthentication = false;
});

// Bootstrap logger factory – avoids the ASP0000 anti-pattern of calling
// builder.Services.BuildServiceProvider() before the host is built. Disposed
// once startup configuration is finished.
using var bootstrapLoggerFactory = LoggerFactory.Create(b => b
    .AddConfiguration(builder.Configuration.GetSection("Logging"))
    .AddConsole());
var bootstrapLogger = bootstrapLoggerFactory.CreateLogger<SecureConnectionStringProvider>();

// Encrypted database password provider
var secureConnectionProvider = new SecureConnectionStringProvider(
    builder.Environment,
    bootstrapLogger);

// Database settings
var databaseSettings = builder.Configuration.GetSection("DatabaseSettings").Get<DatabaseSettings>()
    ?? new DatabaseSettings();

// Fallback to SQLite if no settings are configured
if (string.IsNullOrEmpty(databaseSettings.ConnectionString))
{
    databaseSettings.Provider = DatabaseProvider.SQLite;
    databaseSettings.ConnectionString = "Data Source=inventory.db";
}

var databaseProvider = databaseSettings.Provider;

// Load encrypted password if available (SQLite doesn't use a password)
var connectionString = secureConnectionProvider.HasSecureSecrets() && databaseProvider != DatabaseProvider.SQLite
    ? secureConnectionProvider.GetSecureConnectionString(databaseSettings.ConnectionString)
    : databaseSettings.ConnectionString;

// Application settings
var cacheSettings = builder.Configuration.GetSection("CacheSettings").Get<CacheSettings>() ?? new CacheSettings();
var backupSettings = builder.Configuration.GetSection("BackupSettings").Get<LagersystemLVHome.Application.Configuration.BackupSettings>() ?? new LagersystemLVHome.Application.Configuration.BackupSettings();
var performanceSettings = builder.Configuration.GetSection("PerformanceSettings").Get<PerformanceSettings>() ?? new PerformanceSettings();
var uiSettings = builder.Configuration.GetSection("UISettings").Get<UISettings>() ?? new UISettings();
var dashboardSettings = builder.Configuration.GetSection("DashboardSettings").Get<DashboardSettings>() ?? new DashboardSettings();
var emailSettings = builder.Configuration.GetSection("EmailSettings").Get<EmailSettings>() ?? new EmailSettings();

// Rate limit flag (needed for middleware pipeline setup)
var enableRateLimiting = builder.Configuration.GetSection("RateLimitSettings").GetValue<bool>("Enabled", true);

// Register configuration objects
builder.Services.AddSingleton(databaseSettings);
builder.Services.AddSingleton(cacheSettings);
builder.Services.AddSingleton(backupSettings);
builder.Services.AddSingleton(performanceSettings);
builder.Services.AddSingleton(uiSettings);
builder.Services.AddSingleton(dashboardSettings);
builder.Services.AddSingleton(emailSettings);

// Secure connection string provider
builder.Services.AddSingleton<ISecureConnectionStringProvider, SecureConnectionStringProvider>();

// Data protection (key persistence and encryption)
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, "keys")))
    .SetApplicationName("LagerSystemBackup")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

// ASP.NET Core rate limiter (fallback)
if (enableRateLimiting)
{
    builder.Services.AddRateLimitingLayer();
}

// Configure Kestrel for production
if (builder.Environment.IsProduction())
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(5000); // HTTP only
    });
}



// Response compression, memory cache, session and distributed cache layer
builder.Services.AddCachingLayer(performanceSettings, cacheSettings);

// I18n Text localization (de/en)
builder.Services.AddI18nText(options =>
    options.PersistenceLevel = Toolbelt.Blazor.I18nText.PersistanceLevel.Cookie);

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { "de", "en" };
    options.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("de");
    options.AddSupportedCultures(supportedCultures);
    options.AddSupportedUICultures(supportedCultures);
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// API controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// OpenAPI document generation (Development only)
builder.Services.AddOpenApiDocumentation(builder.Environment);

// SignalR configuration for Blazor Server with increased limits for barcode scanner
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 32 * 1024 * 1024;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

// Blazor Server circuit options
builder.Services.AddServerSideBlazor(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(1);
    options.MaxBufferedUnacknowledgedRenderBatches = 20;
});

// Custom authentication for Blazor Server interactive mode
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "BlazorAuth";
    options.DefaultChallengeScheme = "BlazorAuth";
})
.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, BlazorAuthenticationHandler>("BlazorAuth", null)
.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, LagersystemLVHome.Authentication.ApiKeyAuthenticationHandler>("ApiKey", null);

builder.Services.AddSingleton<CircuitUserStore>();
builder.Services.AddScoped<CustomAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<CustomAuthStateProvider>());
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

// Circuit handlers (must be registered in order)
builder.Services.AddScoped<CircuitHandler, LagersystemLVHome.Infrastructure.CircuitIdTrackerHandler>();
builder.Services.AddScoped<CircuitHandler, LagersystemLVHome.Infrastructure.SessionMonitorCircuitHandler>();

// Database provider service
builder.Services.AddSingleton<IDatabaseProviderService>(serviceProvider =>
{
    var settings = serviceProvider.GetRequiredService<DatabaseSettings>();
    var logger = serviceProvider.GetRequiredService<ILogger<DatabaseProviderService>>();
    var environment = serviceProvider.GetRequiredService<IWebHostEnvironment>();

    return new DatabaseProviderService(settings, logger, environment, connectionString);
});

// DbContext factory for thread-safe access in Blazor Server
builder.Services.AddDbContextFactory<InventoryDbContext>((serviceProvider, options) =>
{
    var dbProvider = serviceProvider.GetRequiredService<IDatabaseProviderService>();
    dbProvider.ConfigureDbContext(options, connectionString);
});

// Log factory pattern usage
var programLogger = bootstrapLoggerFactory.CreateLogger<Program>();
programLogger.LogInformation("{Provider}: Using factory pattern (thread-safe for Blazor Server)", databaseProvider);

// Application services (repositories, business logic, security, ML, etc.)
builder.Services.AddApplicationServices(builder.Configuration);

// Background services (backup, cleanup, monitoring, reports)
builder.Services.AddBackgroundServices(builder.Configuration);

// Global exception handling: catches unhandled exceptions from the request
// pipeline, logs them with full context and returns RFC 7807 ProblemDetails
// to API callers.
builder.Services.AddExceptionHandler<LagersystemLVHome.Infrastructure.GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Health checks: liveness (process up) + readiness (DB reachable).
builder.Services.AddHealthChecks()
    .AddDbContextCheck<InventoryDbContext>("database");

var app = builder.Build();

// Register the exception handler as the outermost middleware so it wraps
// every subsequent component in the pipeline.
app.UseExceptionHandler();

// Response compression middleware
if (performanceSettings.EnableResponseCompression)
{
    app.UseResponseCompression();
}

// Database initialization with seed data
await app.InitializeDatabaseAsync(databaseProvider, connectionString);

// Middleware pipeline
app.UseRequestLocalization();
app.ConfigureMiddleware(enableRateLimiting);

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();

// Liveness probe – does not check downstream dependencies.
app.MapHealthChecks("/healthz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
});

// Readiness probe – includes the database check registered above.
app.MapHealthChecks("/readyz");

app.Run();
