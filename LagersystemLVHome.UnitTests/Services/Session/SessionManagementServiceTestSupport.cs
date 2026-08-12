using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.UnitTests.Services.Session;

/// <summary>
/// Shared test infrastructure for the SessionManagementService test files
/// (Create/Query/Lifecycle/Security/ApiSession). Kept in one place since all
/// five files exercise the same SUT with the same InMemory + factory setup.
/// </summary>
internal static class SessionManagementServiceTestSupport
{
    internal sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    internal static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    internal static SessionManagementService BuildService(
        IDbContextFactory<InventoryDbContext> factory,
        IHttpContextAccessor? httpContextAccessor = null,
        VpnDetectionConfig? vpnConfig = null)
        => new(
            factory,
            httpContextAccessor ?? NoHttpContextAccessor(),
            NullLogger<SessionManagementService>.Instance,
            Options.Create(vpnConfig ?? new VpnDetectionConfig()));

    internal static IHttpContextAccessor NoHttpContextAccessor()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        return accessor;
    }

    internal static IHttpContextAccessor AccessorFor(HttpContext context)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);
        return accessor;
    }

    internal static Warehouse MakeWarehouse(int id = 1) => new()
    {
        Id = id,
        Name = $"WH{id}",
        Code = $"WH{id}",
        Address = "Test Street 1",
        IsActive = true
    };

    internal static User MakeUser(int id = 1, int warehouseId = 1, string username = "u1") => new()
    {
        Id = id,
        Username = username,
        Email = $"{username}@x.local",
        DisplayName = username,
        PasswordHash = "x",
        WarehouseId = warehouseId,
        ApprovalStatus = UserApprovalStatus.Approved,
        Role = UserRole.User,
        IsActive = true
    };

    /// <summary>
    /// Seeds a Warehouse and a User for it. Required whenever a query under
    /// test does Include(s => s.User)/.Include(s => s.Warehouse): UserSession's
    /// FKs to both are non-nullable, so the EF Core InMemory provider treats
    /// those Includes as INNER JOINs and silently drops rows without a match.
    /// </summary>
    internal static async Task SeedWarehouseAndUserAsync(
        IDbContextFactory<InventoryDbContext> factory, int userId = 1, int warehouseId = 1)
    {
        await using var db = factory.CreateDbContext();
        if (!await db.Warehouses.AnyAsync(w => w.Id == warehouseId))
            db.Warehouses.Add(MakeWarehouse(warehouseId));
        if (!await db.Users.AnyAsync(u => u.Id == userId))
            db.Users.Add(MakeUser(userId, warehouseId, $"u{userId}"));
        await db.SaveChangesAsync();
    }

    internal static Domain.Models.UserSession MakeSession(
        string sessionId,
        int userId = 1,
        int warehouseId = 1,
        bool isActive = true,
        DateTime? startTime = null,
        DateTime? lastActivity = null,
        string? ipAddress = "1.1.1.1",
        string? deviceFingerprint = null,
        string? userAgent = "UA",
        string? deviceType = "Desktop",
        string? browser = null,
        string? country = null,
        bool isSuspicious = false,
        DateTime? lastSuspiciousActivity = null,
        SessionRiskLevel riskLevel = SessionRiskLevel.Low,
        DateTime? endTime = null) => new()
        {
            SessionId = sessionId,
            UserId = userId,
            Username = $"u{userId}",
            WarehouseId = warehouseId,
            IsActive = isActive,
            StartTime = startTime ?? DateTime.UtcNow.AddMinutes(-5),
            LastActivity = lastActivity ?? DateTime.UtcNow,
            EndTime = endTime,
            IpAddress = ipAddress,
            DeviceFingerprint = deviceFingerprint,
            UserAgent = userAgent,
            DeviceType = deviceType,
            Browser = browser,
            Country = country,
            IsSuspicious = isSuspicious,
            LastSuspiciousActivity = lastSuspiciousActivity,
            RiskLevel = riskLevel
        };
}
