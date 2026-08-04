using LagersystemLVHome.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Session;

public class DeviceFingerprintServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static DeviceFingerprintService CreateSut(IDbContextFactory<InventoryDbContext>? factory = null)
        => new(factory ?? CreateFactory(Guid.NewGuid().ToString()),
               NullLogger<DeviceFingerprintService>.Instance,
               thumbmarkJsInterop: null);

    private static HttpContext CreateContextWithHeaders(
        string userAgent = "Mozilla/5.0",
        string acceptLanguage = "de-DE",
        string acceptEncoding = "gzip")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["User-Agent"] = userAgent;
        ctx.Request.Headers["Accept-Language"] = acceptLanguage;
        ctx.Request.Headers["Accept-Encoding"] = acceptEncoding;
        return ctx;
    }

    [Fact]
    public void GenerateFingerprint_StableForSameHeaders()
    {
        var sut = CreateSut();
        var ctx1 = CreateContextWithHeaders();
        var ctx2 = CreateContextWithHeaders();

        var fp1 = sut.GenerateFingerprint(ctx1);
        var fp2 = sut.GenerateFingerprint(ctx2);

        fp1.Should().Be(fp2);
        fp1.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateFingerprint_DifferentForDifferentUserAgent()
    {
        var sut = CreateSut();

        var fp1 = sut.GenerateFingerprint(CreateContextWithHeaders(userAgent: "Chrome/120"));
        var fp2 = sut.GenerateFingerprint(CreateContextWithHeaders(userAgent: "Firefox/115"));

        fp1.Should().NotBe(fp2);
    }

    [Fact]
    public void GenerateFingerprint_DifferentForDifferentLanguage()
    {
        var sut = CreateSut();

        var fp1 = sut.GenerateFingerprint(CreateContextWithHeaders(acceptLanguage: "de-DE"));
        var fp2 = sut.GenerateFingerprint(CreateContextWithHeaders(acceptLanguage: "en-US"));

        fp1.Should().NotBe(fp2);
    }

    [Fact]
    public async Task GenerateBrowserFingerprintAsync_NoInterop_FallsBack()
    {
        var sut = CreateSut();

        var fp = await sut.GenerateBrowserFingerprintAsync("instance-1");

        fp.Should().StartWith("fallback-");
    }

    [Fact]
    public async Task IsKnownDeviceAsync_NoMatch_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(IsKnownDeviceAsync_NoMatch_ReturnsFalse));
        var sut = CreateSut(factory);

        var result = await sut.IsKnownDeviceAsync(userId: 1, fingerprint: "unknown-fp");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsKnownDeviceAsync_DirectActiveSessionMatch_ReturnsTrue()
    {
        var factory = CreateFactory(nameof(IsKnownDeviceAsync_DirectActiveSessionMatch_ReturnsTrue));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(new LagersystemLVHome.Domain.Models.UserSession
            {
                UserId = 1,
                SessionId = "s1",
                Username = "u1",
                DeviceFingerprint = "fp-known",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var result = await sut.IsKnownDeviceAsync(userId: 1, fingerprint: "fp-known");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsKnownDeviceAsync_InactiveSession_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(IsKnownDeviceAsync_InactiveSession_ReturnsFalse));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(new LagersystemLVHome.Domain.Models.UserSession
            {
                UserId = 1,
                SessionId = "s1",
                Username = "u1",
                DeviceFingerprint = "fp-known",
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var result = await sut.IsKnownDeviceAsync(userId: 1, fingerprint: "fp-known");

        result.Should().BeFalse();
    }
}
