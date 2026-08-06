using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using static LagersystemLVHome.UnitTests.Services.Session.SessionManagementServiceTestSupport;

namespace LagersystemLVHome.UnitTests.Services.Session;

/// <summary>
/// Covers API-key session management: <see cref="SessionManagementService.GetOrCreateApiSessionAsync"/>
/// and <see cref="SessionManagementService.IncrementApiRequestCountAsync"/>.
/// </summary>
public class SessionManagementServiceApiSessionTests
{
    /// <summary>
    /// Factory whose context creation always fails - used to exercise the
    /// try/catch around the DB access in IncrementApiRequestCountAsync.
    /// </summary>
    private sealed class ThrowingContextFactory : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => throw new InvalidOperationException("Simulated DB failure");
    }

    [Fact]
    public async Task IncrementApiRequestCountAsync_DbFailure_IsCaughtAndDoesNotThrow()
    {
        var sut = BuildService(new ThrowingContextFactory());

        var act = async () => await sut.IncrementApiRequestCountAsync("api-123");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetOrCreateApiSessionAsync_UnknownUser_ReturnsNull()
    {
        var factory = CreateFactory(nameof(GetOrCreateApiSessionAsync_UnknownUser_ReturnsNull));
        var sut = BuildService(factory);

        var session = await sut.GetOrCreateApiSessionAsync(999, warehouseId: 1, "1.2.3.4", "my-key");

        session.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateApiSessionAsync_NoExistingSession_CreatesNewApiSession()
    {
        var factory = CreateFactory(nameof(GetOrCreateApiSessionAsync_NoExistingSession_CreatesNewApiSession));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var session = await sut.GetOrCreateApiSessionAsync(1, warehouseId: 1, "5.5.5.5", "integration-key", "/api/products");

        session.Should().NotBeNull();
        session!.SessionId.Should().StartWith("api-");
        session.DeviceType.Should().Be("API");
        session.Browser.Should().Be("integration-key");
        session.ApiRequestsCount.Should().Be(1);
        session.PageViewsCount.Should().Be(0);
        session.RiskLevel.Should().Be(SessionRiskLevel.Low);
        session.LastPageUrl.Should().Be("/api/products");
        session.IsActive.Should().BeTrue();

        (await factory.CreateDbContext().UserSessions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateApiSessionAsync_NoRequestPath_DefaultsLastPageUrlToApiRoot()
    {
        var factory = CreateFactory(nameof(GetOrCreateApiSessionAsync_NoRequestPath_DefaultsLastPageUrlToApiRoot));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var session = await sut.GetOrCreateApiSessionAsync(1, warehouseId: 1, "5.5.5.5", "integration-key");

        session!.LastPageUrl.Should().Be("/api");
    }

    [Fact]
    public async Task GetOrCreateApiSessionAsync_ExistingActiveApiSession_UpdatesActivityAndIncrementsCount()
    {
        var factory = CreateFactory(nameof(GetOrCreateApiSessionAsync_ExistingActiveApiSession_UpdatesActivityAndIncrementsCount));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(new Domain.Models.UserSession
            {
                SessionId = "api-existing",
                UserId = 1,
                Username = "u1",
                WarehouseId = 1,
                IsActive = true,
                DeviceType = "API",
                Browser = "integration-key",
                IpAddress = "1.1.1.1",
                ApiRequestsCount = 5
            });
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var session = await sut.GetOrCreateApiSessionAsync(1, warehouseId: 1, "9.9.9.9", "integration-key", "/api/orders");

        session.Should().NotBeNull();
        session!.SessionId.Should().Be("api-existing");
        session.ApiRequestsCount.Should().Be(6);
        session.IpAddress.Should().Be("9.9.9.9");
        session.LastPageUrl.Should().Be("/api/orders");

        (await factory.CreateDbContext().UserSessions.CountAsync()).Should().Be(1); // no duplicate created
    }

    [Fact]
    public async Task GetOrCreateApiSessionAsync_ExistingSession_NoRequestPath_KeepsLastPageUrl()
    {
        var factory = CreateFactory(nameof(GetOrCreateApiSessionAsync_ExistingSession_NoRequestPath_KeepsLastPageUrl));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(new Domain.Models.UserSession
            {
                SessionId = "api-existing",
                UserId = 1,
                Username = "u1",
                WarehouseId = 1,
                IsActive = true,
                DeviceType = "API",
                Browser = "integration-key",
                LastPageUrl = "/api/previous"
            });
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var session = await sut.GetOrCreateApiSessionAsync(1, warehouseId: 1, "9.9.9.9", "integration-key");

        session!.LastPageUrl.Should().Be("/api/previous");
    }

    [Fact]
    public async Task GetOrCreateApiSessionAsync_DifferentApiKeyName_CreatesSeparateSession()
    {
        var factory = CreateFactory(nameof(GetOrCreateApiSessionAsync_DifferentApiKeyName_CreatesSeparateSession));
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(MakeUser(1));
            db.UserSessions.Add(new Domain.Models.UserSession
            {
                SessionId = "api-existing",
                UserId = 1,
                Username = "u1",
                WarehouseId = 1,
                IsActive = true,
                DeviceType = "API",
                Browser = "key-a"
            });
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        var session = await sut.GetOrCreateApiSessionAsync(1, warehouseId: 1, "9.9.9.9", "key-b");

        session!.SessionId.Should().NotBe("api-existing");
        (await factory.CreateDbContext().UserSessions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task IncrementApiRequestCountAsync_EmptySessionId_IsNoOp()
    {
        var factory = CreateFactory(nameof(IncrementApiRequestCountAsync_EmptySessionId_IsNoOp));
        var sut = BuildService(factory);

        await sut.IncrementApiRequestCountAsync(""); // should not throw
    }

    [Fact]
    public async Task IncrementApiRequestCountAsync_NonApiSessionId_IsNoOp()
    {
        var factory = CreateFactory(nameof(IncrementApiRequestCountAsync_NonApiSessionId_IsNoOp));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(MakeSession("browser-session"));
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        await sut.IncrementApiRequestCountAsync("browser-session");

        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.ApiRequestsCount.Should().Be(0); // untouched - id doesn't start with "api-"
    }

    [Fact]
    public async Task IncrementApiRequestCountAsync_UnknownApiSessionId_IsNoOp()
    {
        var factory = CreateFactory(nameof(IncrementApiRequestCountAsync_UnknownApiSessionId_IsNoOp));
        var sut = BuildService(factory);

        await sut.IncrementApiRequestCountAsync("api-missing"); // should not throw
    }

    [Fact]
    public async Task IncrementApiRequestCountAsync_KnownActiveApiSession_IncrementsCount()
    {
        var factory = CreateFactory(nameof(IncrementApiRequestCountAsync_KnownActiveApiSession_IncrementsCount));
        await using (var db = factory.CreateDbContext())
        {
            db.UserSessions.Add(new Domain.Models.UserSession
            {
                SessionId = "api-123",
                UserId = 1,
                Username = "u1",
                WarehouseId = 1,
                IsActive = true,
                DeviceType = "API",
                ApiRequestsCount = 2
            });
            await db.SaveChangesAsync();
        }
        var sut = BuildService(factory);

        await sut.IncrementApiRequestCountAsync("api-123");

        var session = await factory.CreateDbContext().UserSessions.SingleAsync();
        session.ApiRequestsCount.Should().Be(3);
    }
}
