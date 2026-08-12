using LagersystemLVHome.Middleware;
using Scalar.AspNetCore;

namespace LagersystemLVHome;

public static class MiddlewarePipelineExtensions
{
    public static WebApplication ConfigureMiddleware(
        this WebApplication app, bool enableRateLimiting)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }
        else
        {
            app.MapOpenApi();
            app.MapScalarApiReference(options =>
            {
                options.Title = "LagerSystem API Documentation";
                options.Theme = ScalarTheme.BluePlanet;
                options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
                options.ShowSidebar = true;
            });
        }

        app.UseForwardedHeaders();
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseAntiforgery();


        app.UseCircuitConnectionMapping();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseSession();
        app.UseSessionValidation();
        app.UseMiddleware<CloudflareSecurityMiddleware>();
        app.UseRateLimit();

        if (enableRateLimiting)
        {
            app.UseRateLimiter();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseMiddleware<ApplicationInsightsMiddleware>();
        app.UseMiddleware<SetupCheckMiddleware>();

        return app;
    }
}
