using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Session;

public class TrustedDeviceServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static TrustedDeviceService Build(IDbContextFactory<InventoryDbContext> factory, IAuditService? audit = null)
        => new(factory, NullLogger<TrustedDeviceService>.Instance, audit);

    [Fact]
    public async Task IsDeviceTrustedAsync_EmptyFingerprint_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(IsDeviceTrustedAsync_EmptyFingerprint_ReturnsFalse));
        (await Build(factory).IsDeviceTrustedAsync(1, "")).Should().BeFalse();
    }

    [Fact]
    public async Task IsDeviceTrustedAsync_DirectTrustedActiveDevice_ReturnsTrue()
    {
        var factory = CreateFactory(nameof(IsDeviceTrustedAsync_DirectTrustedActiveDevice_ReturnsTrue));
        await using (var db = factory.CreateDbContext())
        {
            db.TrustedDevices.Add(new Domain.Models.TrustedDevice
            {
                UserId = 1,
                DeviceFingerprint = "fp-1",
                IsActive = true,
                ExpiresAt = DateTime.UtcNow.AddDays(10)
            });
            await db.SaveChangesAsync();
        }

        (await Build(factory).IsDeviceTrustedAsync(1, "fp-1")).Should().BeTrue();
    }

    [Fact]
    public async Task IsDeviceTrustedAsync_ExpiredDevice_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(IsDeviceTrustedAsync_ExpiredDevice_ReturnsFalse));
        await using (var db = factory.CreateDbContext())
        {
            db.TrustedDevices.Add(new Domain.Models.TrustedDevice
            {
                UserId = 1,
                DeviceFingerprint = "fp-1",
                IsActive = true,
                ExpiresAt = DateTime.UtcNow.AddDays(-1)
            });
            await db.SaveChangesAsync();
        }

        (await Build(factory).IsDeviceTrustedAsync(1, "fp-1")).Should().BeFalse();
    }

    [Fact]
    public async Task IsDeviceTrustedAsync_InactiveDevice_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(IsDeviceTrustedAsync_InactiveDevice_ReturnsFalse));
        await using (var db = factory.CreateDbContext())
        {
            db.TrustedDevices.Add(new Domain.Models.TrustedDevice
            {
                UserId = 1,
                DeviceFingerprint = "fp-1",
                IsActive = false,
                ExpiresAt = DateTime.UtcNow.AddDays(10)
            });
            await db.SaveChangesAsync();
        }

        (await Build(factory).IsDeviceTrustedAsync(1, "fp-1")).Should().BeFalse();
    }

    [Fact]
    public async Task TrustDeviceAsync_NewDevice_PersistsAndAudits()
    {
        var factory = CreateFactory(nameof(TrustDeviceAsync_NewDevice_PersistsAndAudits));
        var audit = Substitute.For<IAuditService>();

        var ok = await Build(factory, audit).TrustDeviceAsync(1, "fp-new", "Chrome on Mac", "1.2.3.4", trustDays: 14);

        ok.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        var device = await db.TrustedDevices.SingleAsync();
        device.UserId.Should().Be(1);
        device.DeviceName.Should().Be("Chrome on Mac");
        device.IsActive.Should().BeTrue();
        device.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(1));
        await audit.Received(1).LogAsync("DEVICE_TRUSTED", "TrustedDevice", 1, Arg.Any<object?>(), AuditSeverity.Info);
    }

    [Fact]
    public async Task TrustDeviceAsync_ExistingDevice_RenewsTrust()
    {
        var factory = CreateFactory(nameof(TrustDeviceAsync_ExistingDevice_RenewsTrust));
        await using (var db = factory.CreateDbContext())
        {
            db.TrustedDevices.Add(new Domain.Models.TrustedDevice
            {
                UserId = 1,
                DeviceFingerprint = "fp-x",
                IsActive = false,
                ExpiresAt = DateTime.UtcNow.AddDays(-10),
                DeviceName = "old"
            });
            await db.SaveChangesAsync();
        }

        await Build(factory).TrustDeviceAsync(1, "fp-x", "fresh", trustDays: 30);

        await using var db2 = factory.CreateDbContext();
        var device = await db2.TrustedDevices.SingleAsync();
        device.IsActive.Should().BeTrue();
        device.DeviceName.Should().Be("fresh");
        device.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task TrustDeviceAsync_EmptyFingerprint_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(TrustDeviceAsync_EmptyFingerprint_ReturnsFalse));
        (await Build(factory).TrustDeviceAsync(1, "")).Should().BeFalse();
    }

    [Fact]
    public async Task UntrustDeviceAsync_KnownDevice_RemovesAndAudits()
    {
        var factory = CreateFactory(nameof(UntrustDeviceAsync_KnownDevice_RemovesAndAudits));
        await using (var db = factory.CreateDbContext())
        {
            db.TrustedDevices.Add(new Domain.Models.TrustedDevice { UserId = 1, DeviceFingerprint = "fp", ExpiresAt = DateTime.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }
        var audit = Substitute.For<IAuditService>();

        var ok = await Build(factory, audit).UntrustDeviceAsync(1, "fp");

        ok.Should().BeTrue();
        await using var db2 = factory.CreateDbContext();
        (await db2.TrustedDevices.CountAsync()).Should().Be(0);
        await audit.Received(1).LogAsync("DEVICE_UNTRUSTED", "TrustedDevice", 1, Arg.Any<object?>(), AuditSeverity.Info);
    }

    [Fact]
    public async Task UntrustDeviceAsync_UnknownDevice_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(UntrustDeviceAsync_UnknownDevice_ReturnsFalse));
        (await Build(factory).UntrustDeviceAsync(1, "fp-missing")).Should().BeFalse();
    }

    [Fact]
    public async Task UntrustDeviceByIdAsync_WrongUser_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(UntrustDeviceByIdAsync_WrongUser_ReturnsFalse));
        await using (var db = factory.CreateDbContext())
        {
            db.TrustedDevices.Add(new Domain.Models.TrustedDevice { Id = 7, UserId = 1, DeviceFingerprint = "fp", ExpiresAt = DateTime.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }

        (await Build(factory).UntrustDeviceByIdAsync(userId: 2, trustedDeviceId: 7)).Should().BeFalse();
    }

    [Fact]
    public async Task GetTrustedDevicesAsync_ReturnsOnlyActiveAndUnexpired_OrderedByTrustedAtDesc()
    {
        var factory = CreateFactory(nameof(GetTrustedDevicesAsync_ReturnsOnlyActiveAndUnexpired_OrderedByTrustedAtDesc));
        await using (var db = factory.CreateDbContext())
        {
            db.TrustedDevices.Add(new Domain.Models.TrustedDevice { UserId = 1, DeviceFingerprint = "a", TrustedAt = DateTime.UtcNow.AddDays(-1), ExpiresAt = DateTime.UtcNow.AddDays(5), IsActive = true });
            db.TrustedDevices.Add(new Domain.Models.TrustedDevice { UserId = 1, DeviceFingerprint = "b", TrustedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(5), IsActive = true });
            db.TrustedDevices.Add(new Domain.Models.TrustedDevice { UserId = 1, DeviceFingerprint = "c", TrustedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(-1), IsActive = true });
            db.TrustedDevices.Add(new Domain.Models.TrustedDevice { UserId = 1, DeviceFingerprint = "d", TrustedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(5), IsActive = false });
            await db.SaveChangesAsync();
        }

        var devices = await Build(factory).GetTrustedDevicesAsync(1);

        devices.Should().HaveCount(2);
        devices[0].DeviceFingerprint.Should().Be("b");
        devices[1].DeviceFingerprint.Should().Be("a");
    }
}
