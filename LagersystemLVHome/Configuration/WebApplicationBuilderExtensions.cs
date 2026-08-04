using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using LagersystemLVHome.Application.Services;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.OpenApi;
using System.IO.Compression;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace LagersystemLVHome;

/// <summary>
/// Extension methods that extract large groups of service registrations and
/// startup-time work out of <c>Program.cs</c>. Keeps <c>Program.cs</c> as a
/// short orchestrator instead of a ~400-line god-file.
/// </summary>
public static class WebApplicationBuilderExtensions
{
    /// <summary>
    /// Adds response compression (Brotli, Gzip), the in-memory cache, ASP.NET
    /// Core session state and a distributed cache (Redis when enabled,
    /// in-memory fallback otherwise).
    /// </summary>
    public static IServiceCollection AddCachingLayer(
        this IServiceCollection services,
        PerformanceSettings performanceSettings,
        CacheSettings cacheSettings)
    {
        if (performanceSettings.EnableResponseCompression)
        {
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
            });

            services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Fastest;
            });

            services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.SmallestSize;
            });
        }

        if (cacheSettings.EnableMemoryCache)
        {
            services.AddMemoryCache(options =>
            {
                options.SizeLimit = 1024;
            });
        }

        services.AddDistributedMemoryCache();
        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });

        if (cacheSettings.EnableDistributedCache)
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = cacheSettings.RedisConnection;
                options.InstanceName = "LagerSystem_";
            });
        }

        return services;
    }

    /// <summary>
    /// Registers the ASP.NET Core rate limiter with a global fixed-window
    /// fallback limiter (100 req/min per user or host) plus a dedicated
    /// <c>SessionCheck</c> policy for the session-overlay polling.
    /// </summary>
    public static IServiceCollection AddRateLimitingLayer(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User.Identity?.Name ?? httpContext.Request.Headers.Host.ToString(),
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 100,
                        QueueLimit = 5,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                await context.HttpContext.Response.WriteAsync(
                    "Too many requests. Please try again later.", token);
            };

            // sessionBlockingOverlay.js polls every 10s (~6 req/min per tab).
            // Partition by session id (taken from the route value) so each tab
            // gets its own bucket and a user with multiple tabs is never throttled
            // by another tab's traffic. Falls back to the host header for
            // unauthenticated requests.
            options.AddPolicy("SessionCheck", context =>
            {
                var sessionId = context.Request.RouteValues["sessionId"]?.ToString()
                    ?? context.Request.Headers.Host.ToString();

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"session:{sessionId}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 60,
                        QueueLimit = 5,
                        Window = TimeSpan.FromMinutes(1)
                    });
            });
        });

        return services;
    }

    /// <summary>
    /// Adds the OpenAPI document generator together with the API-Key security
    /// scheme. Only enabled in the Development environment.
    /// </summary>
    public static IServiceCollection AddOpenApiDocumentation(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            return services;
        }

        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "LagerSystem REST API",
                    Version = "v1.0",
                    Description = "REST API for the LagerSystem inventory platform with API-Key authentication.",
                    Contact = new OpenApiContact
                    {
                        Name = "LagerSystem Team",
                        Url = new Uri("https://github.com/lukislp/Lagersystem")
                    }
                };

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

                document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-API-Key",
                    Description = "API key for authentication. Create one under /profile -> API keys."
                };

                document.Security ??= [];
                document.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("ApiKey", document)] = new List<string>()
                });

                return Task.CompletedTask;
            });
        });

        return services;
    }

    /// <summary>
    /// Ensures the database exists, creates the schema, seeds default
    /// categories and applies missing-column migrations. Any failure is
    /// logged but the app is allowed to start so administrators can fix the
    /// configuration through the Setup page.
    /// </summary>
    public static async Task InitializeDatabaseAsync(
        this WebApplication app,
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var dbProvider = scope.ServiceProvider.GetRequiredService<IDatabaseProviderService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<InventoryDbContext>>();
        var categorySeeder = scope.ServiceProvider.GetRequiredService<CategorySeederService>();

        try
        {
            logger.LogInformation("Initializing database with provider: {Provider}", databaseProvider);

            // Ensure the database itself exists (PostgreSQL/MySQL only - SQLite
            // is file-based and will be created by EnsureCreatedAsync below).
            if (databaseProvider != DatabaseProvider.SQLite)
            {
                logger.LogInformation("Ensuring database exists for {Provider}...", databaseProvider);
                var dbExists = await dbProvider.EnsureDatabaseExistsAsync();

                if (!dbExists)
                {
                    logger.LogError("Failed to create/verify database for {Provider}", databaseProvider);
                    throw new InvalidOperationException($"Database creation failed for provider {databaseProvider}");
                }
            }

            var canConnect = await dbProvider.TestConnectionAsync();
            if (!canConnect)
            {
                logger.LogWarning("Initial database connection failed. Will retry after table creation...");
            }

            logger.LogInformation("Creating database tables for {Provider}...", databaseProvider);
            var created = await db.Database.EnsureCreatedAsync();

            logger.LogInformation(
                created
                    ? "Database and tables created successfully for {Provider}"
                    : "Database already exists for {Provider}",
                databaseProvider);

            if (!await dbProvider.TestConnectionAsync())
            {
                logger.LogError("Database connection failed after creation with provider: {Provider}", databaseProvider);
                return;
            }

            logger.LogInformation("Database connection successful with provider: {Provider}", databaseProvider);

            logger.LogInformation("Seeding standard categories...");
            await categorySeeder.SeedCategoriesAsync();

            logger.LogInformation("Checking for missing database columns...");
            await DatabaseMigrationHelper.EnsureMissingColumnsAsync(db, databaseProvider, logger);

            logger.LogInformation("Database initialization complete!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing the database with provider {Provider}", databaseProvider);
            logger.LogError(
                "Connection String (sanitized): {ConnectionString}",
                connectionString.Contains("Password", StringComparison.OrdinalIgnoreCase)
                    ? "***CONTAINS PASSWORD***"
                    : connectionString);

            // Swallow the error deliberately - the app must still start so
            // operators can access the Setup page and repair configuration.
            logger.LogWarning("Application will continue, but database operations may fail");
        }
    }
}
