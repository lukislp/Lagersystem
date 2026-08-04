using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Security;

public class TamperProofAuditServiceTests
{
    private static InventoryDbContext CreateContext(string name)
        => new(new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static TamperProofAuditService BuildSut(InventoryDbContext ctx)
        => new(ctx, NullLogger<TamperProofAuditService>.Instance);

    [Fact]
    public async Task CreateTamperProofAuditLogAsync_PersistsEntryWithHash()
    {
        await using var ctx = CreateContext(nameof(CreateTamperProofAuditLogAsync_PersistsEntryWithHash));
        var sut = BuildSut(ctx);

        var log = await sut.CreateTamperProofAuditLogAsync(
            userId: 5, action: "LOGIN", entityType: "User", entityId: 5, changes: null, ipAddress: "127.0.0.1");

        log.Id.Should().BeGreaterThan(0);
        log.UserId.Should().Be(5);
        log.Action.Should().Be("LOGIN");
        log.Hash.Should().NotBeNullOrEmpty();
        log.Timestamp.Millisecond.Should().Be(0);

        var stored = await ctx.AuditLogs.SingleAsync();
        stored.Hash.Should().Be(log.Hash);
    }

    [Fact]
    public async Task CreateTamperProofAuditLogAsync_UserIdZero_StoredAsNull()
    {
        await using var ctx = CreateContext(nameof(CreateTamperProofAuditLogAsync_UserIdZero_StoredAsNull));
        var sut = BuildSut(ctx);

        var log = await sut.CreateTamperProofAuditLogAsync(0, "ANON_ACTION");

        log.UserId.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAuditLogIntegrityAsync_CleanChain_ReturnsValid()
    {
        await using var ctx = CreateContext(nameof(VerifyAuditLogIntegrityAsync_CleanChain_ReturnsValid));
        var sut = BuildSut(ctx);

        await sut.CreateTamperProofAuditLogAsync(1, "A");
        await sut.CreateTamperProofAuditLogAsync(2, "B", entityType: "X", entityId: 9);
        await sut.CreateTamperProofAuditLogAsync(3, "C", changes: "{\"k\":1}");

        var result = await sut.VerifyAuditLogIntegrityAsync();

        result.IsValid.Should().BeTrue();
        result.TotalChecked.Should().Be(3);
        result.InvalidLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyAuditLogIntegrityAsync_TamperedEntry_DetectsMismatch()
    {
        await using var ctx = CreateContext(nameof(VerifyAuditLogIntegrityAsync_TamperedEntry_DetectsMismatch));
        var sut = BuildSut(ctx);

        await sut.CreateTamperProofAuditLogAsync(1, "A");
        var second = await sut.CreateTamperProofAuditLogAsync(2, "B");

        // Tamper: change the action without recomputing the hash
        second.Action = "TAMPERED";
        await ctx.SaveChangesAsync();

        var result = await sut.VerifyAuditLogIntegrityAsync();

        result.IsValid.Should().BeFalse();
        result.InvalidLogs.Should().ContainSingle().Which.LogId.Should().Be(second.Id);
    }

    [Fact]
    public async Task VerifyAuditLogIntegrityAsync_NoLogs_ReturnsValid()
    {
        await using var ctx = CreateContext(nameof(VerifyAuditLogIntegrityAsync_NoLogs_ReturnsValid));
        var sut = BuildSut(ctx);

        var result = await sut.VerifyAuditLogIntegrityAsync();

        result.IsValid.Should().BeTrue();
        result.TotalChecked.Should().Be(0);
    }

    [Fact]
    public async Task ExportAuditLogsAsync_ReturnsJsonContainingAllEntries()
    {
        await using var ctx = CreateContext(nameof(ExportAuditLogsAsync_ReturnsJsonContainingAllEntries));
        var sut = BuildSut(ctx);

        await sut.CreateTamperProofAuditLogAsync(1, "ALPHA");
        await sut.CreateTamperProofAuditLogAsync(2, "BETA");

        var json = await sut.ExportAuditLogsAsync();

        json.Should().Contain("ALPHA").And.Contain("BETA");
        json.Should().Contain("\"TotalLogs\": 2");
    }

    [Fact]
    public async Task DebugAuditLogHashAsync_UnknownLog_ReturnsNotFoundMessage()
    {
        await using var ctx = CreateContext(nameof(DebugAuditLogHashAsync_UnknownLog_ReturnsNotFoundMessage));
        var sut = BuildSut(ctx);

        var result = await sut.DebugAuditLogHashAsync(999);

        result.Should().Contain("999").And.Contain("nicht gefunden");
    }
}
