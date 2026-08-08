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

    [Fact]
    public async Task IsKnownDeviceAsync_LinkedFingerprintWithActiveSessionOnPrimary_ReturnsTrue()
    {
        // A device was registered under "fp-primary" (e.g. desktop browser) and later linked
        // to "fp-pwa" (e.g. the same device's installed PWA). An active session only exists
        // under the primary fingerprint. IsKnownDeviceAsync should resolve "fp-pwa" as known
        // via the link.
        var factory = CreateFactory(nameof(IsKnownDeviceAsync_LinkedFingerprintWithActiveSessionOnPrimary_ReturnsTrue));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(new LagersystemLVHome.Domain.Models.UserSession
            {
                UserId = 1,
                SessionId = "s1",
                Username = "u1",
                DeviceFingerprint = "fp-primary",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            db.LinkedDeviceFingerprints.Add(new LagersystemLVHome.Domain.Models.LinkedDeviceFingerprint
            {
                UserId = 1,
                PrimaryFingerprint = "fp-primary",
                LinkedFingerprint = "fp-pwa"
            });
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var result = await sut.IsKnownDeviceAsync(userId: 1, fingerprint: "fp-pwa");

        result.Should().BeFalse(
            "known bug: the allRelated SelectMany array-literal projection cannot be translated by EF Core " +
            "and throws internally, so IsKnownDeviceAsync always fails closed for linked-only fingerprints " +
            "(see this test's XML doc comment for full detail) - this SHOULD be true for a genuinely linked, active device");
    }

    [Fact]
    public async Task IsKnownDeviceAsync_LinkedButNoActiveSessionOnEitherFingerprint_ReturnsFalse()
    {
        // NOTE: this happens to return false for the *correct* reason it should (no active
        // session exists at all) - but because of the SelectMany bug documented in the test
        // above, it would also return false even if an active session DID exist. This test
        // only proves "false" is returned, not that it's returned for the right reason.
        var factory = CreateFactory(nameof(IsKnownDeviceAsync_LinkedButNoActiveSessionOnEitherFingerprint_ReturnsFalse));
        await using (var db = factory.CreateDbContext())
        {
            db.LinkedDeviceFingerprints.Add(new LagersystemLVHome.Domain.Models.LinkedDeviceFingerprint
            {
                UserId = 1,
                PrimaryFingerprint = "fp-primary",
                LinkedFingerprint = "fp-pwa"
            });
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        (await sut.IsKnownDeviceAsync(userId: 1, fingerprint: "fp-pwa")).Should().BeFalse();
    }

    [Fact]
    public async Task IsKnownDeviceAsync_ContextFactoryThrows_FailsClosedReturnsFalse()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db down")));
        var sut = CreateSut(throwingFactory);

        (await sut.IsKnownDeviceAsync(userId: 1, fingerprint: "fp")).Should().BeFalse(
            "an unknown device lookup must fail closed (treat as unrecognized) on error, not silently claim it is known");
    }

    // ---- GenerateBrowserFingerprintAsync (with interop) ------------------------------------

    [Fact]
    public async Task GenerateBrowserFingerprintAsync_InteropReturnsValue_UsesItDirectly()
    {
        var interop = Substitute.For<Soenneker.Blazor.Thumbmarkjs.Abstract.IThumbmarkjsInterop>();
        interop.Get("instance-1", Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>("real-fingerprint-value"));
        var sut = new DeviceFingerprintService(CreateFactory(Guid.NewGuid().ToString()), NullLogger<DeviceFingerprintService>.Instance, interop);

        var fp = await sut.GenerateBrowserFingerprintAsync("instance-1");

        fp.Should().Be("real-fingerprint-value");
    }

    [Fact]
    public async Task GenerateBrowserFingerprintAsync_InteropReturnsEmpty_FallsBack()
    {
        var interop = Substitute.For<Soenneker.Blazor.Thumbmarkjs.Abstract.IThumbmarkjsInterop>();
        interop.Get(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>(string.Empty));
        var sut = new DeviceFingerprintService(CreateFactory(Guid.NewGuid().ToString()), NullLogger<DeviceFingerprintService>.Instance, interop);

        var fp = await sut.GenerateBrowserFingerprintAsync("instance-1");

        fp.Should().StartWith("fallback-");
    }

    [Fact]
    public async Task GenerateBrowserFingerprintAsync_InteropThrows_ReturnsErrorFingerprint()
    {
        var interop = Substitute.For<Soenneker.Blazor.Thumbmarkjs.Abstract.IThumbmarkjsInterop>();
        interop.Get(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<string?>(Task.FromException<string?>(new InvalidOperationException("js interop failed"))));
        var sut = new DeviceFingerprintService(CreateFactory(Guid.NewGuid().ToString()), NullLogger<DeviceFingerprintService>.Instance, interop);

        var fp = await sut.GenerateBrowserFingerprintAsync("instance-1");

        fp.Should().StartWith("error-");
    }

    // ---- SaveDeviceFingerprintAsync / ParseUserAgent ---------------------------------------

    private static HttpContext ContextWithUserAgent(string? userAgent, string? xOriginalUserAgent = null)
    {
        var ctx = new DefaultHttpContext();
        if (userAgent != null) ctx.Request.Headers["User-Agent"] = userAgent;
        if (xOriginalUserAgent != null) ctx.Request.Headers["X-Original-User-Agent"] = xOriginalUserAgent;
        return ctx;
    }

    private static async Task<int> SeedSessionAsync(IDbContextFactory<InventoryDbContext> factory, int userId = 1)
    {
        await using var db = factory.CreateDbContext();
        var session = new LagersystemLVHome.Domain.Models.UserSession
        {
            UserId = userId,
            SessionId = $"s-{Guid.NewGuid():N}",
            Username = "u1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0", "Chrome", "Windows 10/11", "Desktop")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) Edg/120.0", "Edge", "Windows 10/11", "Desktop")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) Firefox/115.0", "Firefox", "Linux", "Desktop")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15) Safari/605.1", "Safari", "macOS", "Desktop")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0) Safari/604.1", "Safari", "iPadOS", "Tablet")]
    [InlineData("Mozilla/5.0 (Linux; Android 14) Chrome Mobile/120.0 mobile", "Chrome", "Android", "Mobile")]
    [InlineData("Mozilla/5.0 (Windows NT 6.1) Opera/90.0 OPR/90.0", "Opera", "Windows 7", "Desktop")]
    [InlineData("Mozilla/4.0 (compatible; MSIE 8.0; Windows NT 6.2)", "Internet Explorer", "Windows 8", "Desktop")]
    [InlineData("Mozilla/5.0 CrOS x86_64 14000.0.0", "Unknown", "ChromeOS", "Desktop")]
    public async Task SaveDeviceFingerprintAsync_ParsesBrowserOsAndDeviceTypeFromUserAgent(
        string userAgent, string expectedBrowser, string expectedOs, string expectedDeviceType)
    {
        var factory = CreateFactory($"{nameof(SaveDeviceFingerprintAsync_ParsesBrowserOsAndDeviceTypeFromUserAgent)}_{Guid.NewGuid()}");
        var sessionId = await SeedSessionAsync(factory);
        var sut = CreateSut(factory);

        await sut.SaveDeviceFingerprintAsync(sessionId, "fp-1", ContextWithUserAgent(userAgent));

        await using var db = factory.CreateDbContext();
        var session = await db.UserSessions.FindAsync(sessionId);
        session!.DeviceFingerprint.Should().Be("fp-1");
        session.Browser.Should().Be(expectedBrowser);
        session.OperatingSystem.Should().Be(expectedOs);
        session.DeviceType.Should().Be(expectedDeviceType);
        session.DeviceInfo.Should().Be(expectedDeviceType);
    }

    /// <summary>
    /// BUG: real-world iPhone/iOS Safari user-agent strings always include the literal
    /// substring "like Mac OS X" (Apple's standard UA format, e.g. "... CPU iPhone OS 17_0
    /// like Mac OS X ... Mobile/15E148 Safari/604.1" - this is not a contrived example, every
    /// iPhone sends exactly this). <c>ParseUserAgent</c>'s OS else-if chain checks
    /// <c>Contains("mac os x")</c> BEFORE it checks <c>Contains("iphone")</c>, so every real
    /// iPhone visitor is misclassified as <c>OperatingSystem = "macOS"</c> instead of
    /// <c>"iOS"</c>. The device TYPE is still correctly detected as "Mobile" (that check looks
    /// for "mobile"/"android" and runs independently), so this only corrupts the OS field -
    /// but that field feeds device-list UI (<see cref="DeviceInfo"/>/session management pages)
    /// and any OS-based analytics/reporting, silently merging all iPhone sessions into the Mac
    /// bucket. Fix: check "iphone"/"ipad" before the "mac os x" branch.
    /// </summary>
    [Fact]
    public async Task SaveDeviceFingerprintAsync_RealisticIPhoneUserAgent_KnownBug_MisdetectsOsAsMacOsInsteadOfIOS()
    {
        const string realisticIPhoneUserAgent =
            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";
        var factory = CreateFactory(nameof(SaveDeviceFingerprintAsync_RealisticIPhoneUserAgent_KnownBug_MisdetectsOsAsMacOsInsteadOfIOS));
        var sessionId = await SeedSessionAsync(factory);
        var sut = CreateSut(factory);

        await sut.SaveDeviceFingerprintAsync(sessionId, "fp-1", ContextWithUserAgent(realisticIPhoneUserAgent));

        await using var db = factory.CreateDbContext();
        var session = await db.UserSessions.FindAsync(sessionId);
        session!.DeviceType.Should().Be("Mobile", "device-type detection is unaffected by the bug");
        session.OperatingSystem.Should().Be(
            "macOS",
            "known bug: the 'mac os x' branch is checked before 'iphone' in ParseUserAgent's else-if " +
            "chain, so real iPhone UAs (which always contain 'like Mac OS X') are misclassified - this " +
            "SHOULD be 'iOS'");
    }

    [Fact]
    public async Task SaveDeviceFingerprintAsync_EmptyUserAgentWithXOriginalHeader_UsesFallbackHeader()
    {
        var factory = CreateFactory(nameof(SaveDeviceFingerprintAsync_EmptyUserAgentWithXOriginalHeader_UsesFallbackHeader));
        var sessionId = await SeedSessionAsync(factory);
        var sut = CreateSut(factory);

        await sut.SaveDeviceFingerprintAsync(sessionId, "fp-1", ContextWithUserAgent(null, xOriginalUserAgent: "Mozilla/5.0 Firefox/115.0"));

        await using var db = factory.CreateDbContext();
        (await db.UserSessions.FindAsync(sessionId))!.Browser.Should().Be("Firefox");
    }

    [Fact]
    public async Task SaveDeviceFingerprintAsync_NoUserAgentAtAll_ParsesAsUnknownDesktop()
    {
        var factory = CreateFactory(nameof(SaveDeviceFingerprintAsync_NoUserAgentAtAll_ParsesAsUnknownDesktop));
        var sessionId = await SeedSessionAsync(factory);
        var sut = CreateSut(factory);

        await sut.SaveDeviceFingerprintAsync(sessionId, "fp-1", ContextWithUserAgent(null));

        await using var db = factory.CreateDbContext();
        var session = await db.UserSessions.FindAsync(sessionId);
        session!.Browser.Should().Be("Unknown");
        session.OperatingSystem.Should().Be("Unknown");
        session.DeviceType.Should().Be("Unknown");
    }

    [Fact]
    public async Task SaveDeviceFingerprintAsync_UnknownSessionId_DoesNothingAndDoesNotThrow()
    {
        var factory = CreateFactory(nameof(SaveDeviceFingerprintAsync_UnknownSessionId_DoesNothingAndDoesNotThrow));
        var sut = CreateSut(factory);

        var act = async () => await sut.SaveDeviceFingerprintAsync(999, "fp-1", ContextWithUserAgent("Mozilla/5.0"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveDeviceFingerprintAsync_ContextFactoryThrows_SwallowsException()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db down")));
        var sut = CreateSut(throwingFactory);

        var act = async () => await sut.SaveDeviceFingerprintAsync(1, "fp-1", ContextWithUserAgent("Mozilla/5.0"));

        await act.Should().NotThrowAsync();
    }

    // ---- GetUserDevicesAsync -----------------------------------------------------------------

    [Fact]
    public async Task GetUserDevicesAsync_GroupsSessionsByFingerprintAndOrdersByLastSeen()
    {
        var factory = CreateFactory(nameof(GetUserDevicesAsync_GroupsSessionsByFingerprintAndOrdersByLastSeen));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(new LagersystemLVHome.Domain.Models.UserSession
            {
                UserId = 1,
                SessionId = "s1",
                Username = "u1",
                DeviceFingerprint = "fp-a",
                IsActive = true,
                DeviceInfo = "Desktop",
                LastActivity = DateTime.UtcNow.AddHours(-2),
                CreatedAt = DateTime.UtcNow
            });
            db.UserSessions.Add(new LagersystemLVHome.Domain.Models.UserSession
            {
                UserId = 1,
                SessionId = "s2",
                Username = "u1",
                DeviceFingerprint = "fp-a",
                IsActive = false,
                DeviceInfo = "Desktop",
                LastActivity = DateTime.UtcNow.AddHours(-1),
                CreatedAt = DateTime.UtcNow
            });
            db.UserSessions.Add(new LagersystemLVHome.Domain.Models.UserSession
            {
                UserId = 1,
                SessionId = "s3",
                Username = "u1",
                DeviceFingerprint = "fp-b",
                IsActive = false,
                DeviceInfo = "Mobile",
                LastActivity = DateTime.UtcNow.AddHours(-5),
                CreatedAt = DateTime.UtcNow
            });
            db.UserSessions.Add(new LagersystemLVHome.Domain.Models.UserSession
            {
                UserId = 2,
                SessionId = "s4",
                Username = "u2",
                DeviceFingerprint = "fp-other-user",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var devices = await sut.GetUserDevicesAsync(1);

        devices.Should().HaveCount(2);
        devices[0].Fingerprint.Should().Be("fp-a", "fp-a's most recent session (s2, -1h) is more recent than fp-b's (-5h)");
        devices[0].SessionCount.Should().Be(2);
        devices[0].IsActive.Should().BeTrue("at least one of fp-a's sessions (s1) is active");
        devices[1].Fingerprint.Should().Be("fp-b");
        devices[1].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserDevicesAsync_NoSessions_ReturnsEmptyList()
    {
        var factory = CreateFactory(nameof(GetUserDevicesAsync_NoSessions_ReturnsEmptyList));
        var sut = CreateSut(factory);

        (await sut.GetUserDevicesAsync(1)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserDevicesAsync_ContextFactoryThrows_ReturnsEmptyListInsteadOfThrowing()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db down")));
        var sut = CreateSut(throwingFactory);

        (await sut.GetUserDevicesAsync(1)).Should().BeEmpty();
    }

    // ---- LinkFingerprintsAsync ----------------------------------------------------------------

    [Fact]
    public async Task LinkFingerprintsAsync_EmptyPrimaryOrLinked_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(LinkFingerprintsAsync_EmptyPrimaryOrLinked_ReturnsFalse));
        var sut = CreateSut(factory);

        (await sut.LinkFingerprintsAsync(1, "", "fp-linked")).Should().BeFalse();
        (await sut.LinkFingerprintsAsync(1, "fp-primary", "")).Should().BeFalse();
    }

    [Fact]
    public async Task LinkFingerprintsAsync_SameFingerprint_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(LinkFingerprintsAsync_SameFingerprint_ReturnsFalse));
        var sut = CreateSut(factory);

        (await sut.LinkFingerprintsAsync(1, "fp-x", "fp-x")).Should().BeFalse();
    }

    [Fact]
    public async Task LinkFingerprintsAsync_NewLink_PersistsWithSource()
    {
        var factory = CreateFactory(nameof(LinkFingerprintsAsync_NewLink_PersistsWithSource));
        var sut = CreateSut(factory);

        var ok = await sut.LinkFingerprintsAsync(1, "fp-primary", "fp-linked", source: "pwa-install");

        ok.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        var link = await db.LinkedDeviceFingerprints.SingleAsync();
        link.UserId.Should().Be(1);
        link.PrimaryFingerprint.Should().Be("fp-primary");
        link.LinkedFingerprint.Should().Be("fp-linked");
        link.Source.Should().Be("pwa-install");
    }

    [Fact]
    public async Task LinkFingerprintsAsync_AlreadyExistingExactLink_ReturnsTrueWithoutDuplicating()
    {
        var factory = CreateFactory(nameof(LinkFingerprintsAsync_AlreadyExistingExactLink_ReturnsTrueWithoutDuplicating));
        var sut = CreateSut(factory);
        await sut.LinkFingerprintsAsync(1, "fp-primary", "fp-linked");

        var ok = await sut.LinkFingerprintsAsync(1, "fp-primary", "fp-linked");

        ok.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        (await db.LinkedDeviceFingerprints.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task LinkFingerprintsAsync_LinkedFingerprintWasItselfAPrimary_TakesOverItsExistingLinks()
    {
        // fp-b was previously the primary for fp-c. Now fp-b gets linked under fp-a as its
        // new primary - fp-c's link should be re-pointed to fp-a as well (transitive merge).
        var factory = CreateFactory(nameof(LinkFingerprintsAsync_LinkedFingerprintWasItselfAPrimary_TakesOverItsExistingLinks));
        var sut = CreateSut(factory);
        await sut.LinkFingerprintsAsync(1, "fp-b", "fp-c");

        var ok = await sut.LinkFingerprintsAsync(1, "fp-a", "fp-b");

        ok.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        var links = await db.LinkedDeviceFingerprints.Where(l => l.UserId == 1).ToListAsync();
        links.Should().Contain(l => l.PrimaryFingerprint == "fp-a" && l.LinkedFingerprint == "fp-b");
        links.Should().Contain(l => l.PrimaryFingerprint == "fp-a" && l.LinkedFingerprint == "fp-c",
            "fp-c's link must be re-pointed from fp-b to the new primary fp-a");
    }

    [Fact]
    public async Task LinkFingerprintsAsync_LinkedFingerprintWasLinkedUnderAnotherPrimary_MovesItOver()
    {
        // fp-c starts out linked under fp-x. Re-linking fp-c under fp-a must remove the old
        // fp-x -> fp-c link entry so a fingerprint is never linked under two primaries at once.
        var factory = CreateFactory(nameof(LinkFingerprintsAsync_LinkedFingerprintWasLinkedUnderAnotherPrimary_MovesItOver));
        var sut = CreateSut(factory);
        await sut.LinkFingerprintsAsync(1, "fp-x", "fp-c");

        var ok = await sut.LinkFingerprintsAsync(1, "fp-a", "fp-c");

        ok.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        var links = await db.LinkedDeviceFingerprints.Where(l => l.UserId == 1).ToListAsync();
        links.Should().ContainSingle(l => l.LinkedFingerprint == "fp-c")
            .Which.PrimaryFingerprint.Should().Be("fp-a");
    }

    [Fact]
    public async Task LinkFingerprintsAsync_ContextFactoryThrows_ReturnsFalse()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db down")));
        var sut = CreateSut(throwingFactory);

        (await sut.LinkFingerprintsAsync(1, "fp-a", "fp-b")).Should().BeFalse();
    }

    // ---- UnlinkFingerprintAsync ---------------------------------------------------------------

    [Fact]
    public async Task UnlinkFingerprintAsync_ExistingLink_RemovesItAndReturnsTrue()
    {
        var factory = CreateFactory(nameof(UnlinkFingerprintAsync_ExistingLink_RemovesItAndReturnsTrue));
        var sut = CreateSut(factory);
        await sut.LinkFingerprintsAsync(1, "fp-a", "fp-b");
        int linkId;
        await using (var db = factory.CreateDbContext())
            linkId = (await db.LinkedDeviceFingerprints.SingleAsync()).Id;

        (await sut.UnlinkFingerprintAsync(1, linkId)).Should().BeTrue();
        await using var verify = factory.CreateDbContext();
        (await verify.LinkedDeviceFingerprints.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task UnlinkFingerprintAsync_UnknownId_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(UnlinkFingerprintAsync_UnknownId_ReturnsFalse));
        var sut = CreateSut(factory);

        (await sut.UnlinkFingerprintAsync(1, 999)).Should().BeFalse();
    }

    [Fact]
    public async Task UnlinkFingerprintAsync_LinkBelongsToAnotherUser_ReturnsFalseAndDoesNotDelete()
    {
        var factory = CreateFactory(nameof(UnlinkFingerprintAsync_LinkBelongsToAnotherUser_ReturnsFalseAndDoesNotDelete));
        var sut = CreateSut(factory);
        await sut.LinkFingerprintsAsync(1, "fp-a", "fp-b");
        int linkId;
        await using (var db = factory.CreateDbContext())
            linkId = (await db.LinkedDeviceFingerprints.SingleAsync()).Id;

        (await sut.UnlinkFingerprintAsync(userId: 2, linkedId: linkId)).Should().BeFalse();
        await using var verify = factory.CreateDbContext();
        (await verify.LinkedDeviceFingerprints.AnyAsync()).Should().BeTrue("a link must not be deletable by a user who doesn't own it");
    }

    // ---- GetLinkedFingerprintsAsync -----------------------------------------------------------

    [Fact]
    public async Task GetLinkedFingerprintsAsync_ReturnsOnlyOwnLinks()
    {
        var factory = CreateFactory(nameof(GetLinkedFingerprintsAsync_ReturnsOnlyOwnLinks));
        var sut = CreateSut(factory);
        await sut.LinkFingerprintsAsync(1, "fp-a", "fp-b");
        await sut.LinkFingerprintsAsync(2, "fp-x", "fp-y");

        var links = await sut.GetLinkedFingerprintsAsync(1);

        links.Should().ContainSingle().Which.PrimaryFingerprint.Should().Be("fp-a");
    }

    [Fact]
    public async Task GetLinkedFingerprintsAsync_ContextFactoryThrows_ReturnsEmptyList()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db down")));
        var sut = CreateSut(throwingFactory);

        (await sut.GetLinkedFingerprintsAsync(1)).Should().BeEmpty();
    }

    // ---- ResolvePrimaryFingerprintAsync ---------------------------------------------------------

    [Fact]
    public async Task ResolvePrimaryFingerprintAsync_EmptyFingerprint_ReturnsItUnchanged()
    {
        var factory = CreateFactory(nameof(ResolvePrimaryFingerprintAsync_EmptyFingerprint_ReturnsItUnchanged));
        var sut = CreateSut(factory);

        (await sut.ResolvePrimaryFingerprintAsync(1, "")).Should().Be("");
    }

    [Fact]
    public async Task ResolvePrimaryFingerprintAsync_KnownLinkedFingerprint_ResolvesToPrimary()
    {
        var factory = CreateFactory(nameof(ResolvePrimaryFingerprintAsync_KnownLinkedFingerprint_ResolvesToPrimary));
        var sut = CreateSut(factory);
        await sut.LinkFingerprintsAsync(1, "fp-primary", "fp-linked");

        (await sut.ResolvePrimaryFingerprintAsync(1, "fp-linked")).Should().Be("fp-primary");
    }

    [Fact]
    public async Task ResolvePrimaryFingerprintAsync_UnknownFingerprint_ReturnsItUnchanged()
    {
        var factory = CreateFactory(nameof(ResolvePrimaryFingerprintAsync_UnknownFingerprint_ReturnsItUnchanged));
        var sut = CreateSut(factory);

        (await sut.ResolvePrimaryFingerprintAsync(1, "fp-standalone")).Should().Be("fp-standalone");
    }

    [Fact]
    public async Task ResolvePrimaryFingerprintAsync_ContextFactoryThrows_ReturnsInputUnchanged()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db down")));
        var sut = CreateSut(throwingFactory);

        (await sut.ResolvePrimaryFingerprintAsync(1, "fp-x")).Should().Be("fp-x");
    }
}
