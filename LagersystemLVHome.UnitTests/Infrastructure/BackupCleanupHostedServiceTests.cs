using System.Reflection;
using LagersystemLVHome.Infrastructure.HostedServices;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Infrastructure;

/// <summary>
/// Covers <see cref="BackupCleanupHostedService"/>.
///
/// The service is a <c>BackgroundService</c> whose <c>ExecuteAsync</c> loop is just a
/// 15-minute delay/log wrapper around one real, testable piece of logic:
/// <c>CleanupByMaxBackupsCountAsync</c> (and the config-parsing helper
/// <c>GetMaxBackupsCountFromProvider</c> it calls). Both are private with no public
/// surface, so they are invoked directly via reflection - the standard way to unit-test a
/// BackgroundService's actual business logic without waiting through real
/// <c>Task.Delay</c> calls tied to wall-clock time (which would make tests either flaky or
/// impractically slow). <c>IBackupManagementService</c> is substituted; a scope-capable
/// <see cref="IServiceProvider"/> isn't needed since these tests call the logic method
/// directly rather than going through <c>_serviceProvider.CreateScope()</c>.
/// </summary>
public sealed class BackupCleanupHostedServiceTests
{
    private static BackupCleanupHostedService CreateSut()
        => new(Substitute.For<IServiceProvider>(), NullLogger<BackupCleanupHostedService>.Instance);

    private static Task InvokeCleanupAsync(BackupCleanupHostedService sut, IBackupManagementService backupService)
    {
        var method = typeof(BackupCleanupHostedService).GetMethod(
            "CleanupByMaxBackupsCountAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(sut, new object[] { backupService, CancellationToken.None })!;
    }

    private static int InvokeGetMaxBackupsCount(BackupCleanupHostedService sut, BackupProvider provider)
    {
        var method = typeof(BackupCleanupHostedService).GetMethod(
            "GetMaxBackupsCountFromProvider", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (int)method.Invoke(sut, new object[] { provider })!;
    }

    private static BackupHistory History(int id, int providerId, BackupStatus status, DateTime backupDate)
        => new() { Id = id, BackupProviderId = providerId, FileName = $"b{id}.zip", Status = status, BackupDate = backupDate };

    // ----- GetMaxBackupsCountFromProvider -----

    [Fact]
    public void GetMaxBackupsCountFromProvider_EmptyConfiguration_ReturnsDefault30()
    {
        var sut = CreateSut();
        var provider = new BackupProvider { Configuration = "" };

        InvokeGetMaxBackupsCount(sut, provider).Should().Be(30);
    }

    [Fact]
    public void GetMaxBackupsCountFromProvider_NumberValue_ReturnsConfiguredValue()
    {
        var sut = CreateSut();
        var provider = new BackupProvider { Configuration = "{\"MaxBackupsCount\": 7}" };

        InvokeGetMaxBackupsCount(sut, provider).Should().Be(7);
    }

    [Fact]
    public void GetMaxBackupsCountFromProvider_StringValue_ParsesAndReturnsValue()
    {
        var sut = CreateSut();
        var provider = new BackupProvider { Configuration = "{\"maxBackups\": \"12\"}" };

        InvokeGetMaxBackupsCount(sut, provider).Should().Be(12);
    }

    [Fact]
    public void GetMaxBackupsCountFromProvider_ZeroOrNegative_FallsBackToDefault()
    {
        var sut = CreateSut();
        var provider = new BackupProvider { Configuration = "{\"MaxBackupsCount\": 0}" };

        InvokeGetMaxBackupsCount(sut, provider).Should().Be(30);
    }

    [Fact]
    public void GetMaxBackupsCountFromProvider_UnknownKey_ReturnsDefault30()
    {
        var sut = CreateSut();
        var provider = new BackupProvider { Configuration = "{\"SomethingElse\": 7}" };

        InvokeGetMaxBackupsCount(sut, provider).Should().Be(30);
    }

    [Fact]
    public void GetMaxBackupsCountFromProvider_InvalidJson_ReturnsDefault30()
    {
        var sut = CreateSut();
        var provider = new BackupProvider { Configuration = "not-json" };

        InvokeGetMaxBackupsCount(sut, provider).Should().Be(30);
    }

    [Fact]
    public void GetMaxBackupsCountFromProvider_NullJson_ReturnsDefault30()
    {
        var sut = CreateSut();
        var provider = new BackupProvider { Configuration = "null" };

        InvokeGetMaxBackupsCount(sut, provider).Should().Be(30);
    }

    // ----- CleanupByMaxBackupsCountAsync -----

    [Fact]
    public async Task CleanupByMaxBackupsCountAsync_NoProviders_CompletesWithoutError()
    {
        var sut = CreateSut();
        var backupService = Substitute.For<IBackupManagementService>();
        backupService.GetProvidersAsync().Returns(new List<BackupProvider>());

        var act = async () => await InvokeCleanupAsync(sut, backupService);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CleanupByMaxBackupsCountAsync_DisabledProvider_IsSkipped()
    {
        var sut = CreateSut();
        var backupService = Substitute.For<IBackupManagementService>();
        var provider = new BackupProvider { Id = 1, Name = "P", Enabled = false, Configuration = "{\"MaxBackupsCount\":1}" };
        backupService.GetProvidersAsync().Returns(new List<BackupProvider> { provider });

        await InvokeCleanupAsync(sut, backupService);

        await backupService.DidNotReceiveWithAnyArgs().GetHistoryAsync(default, default, default);
    }

    [Fact]
    public async Task CleanupByMaxBackupsCountAsync_WithinLimit_DeletesNothing()
    {
        var sut = CreateSut();
        var backupService = Substitute.For<IBackupManagementService>();
        var provider = new BackupProvider { Id = 1, Name = "P", Enabled = true, Configuration = "{\"MaxBackupsCount\":5}" };
        backupService.GetProvidersAsync().Returns(new List<BackupProvider> { provider });
        backupService.GetHistoryAsync(1, 1000, Arg.Any<CancellationToken>()).Returns(new List<BackupHistory>
        {
            History(1, 1, BackupStatus.Success, DateTime.UtcNow.AddDays(-1)),
            History(2, 1, BackupStatus.Success, DateTime.UtcNow)
        });

        await InvokeCleanupAsync(sut, backupService);

        await backupService.DidNotReceiveWithAnyArgs().DeleteBackupAsync(default, default);
    }

    [Fact]
    public async Task CleanupByMaxBackupsCountAsync_OverLimit_DeletesOldestBeyondMax()
    {
        var sut = CreateSut();
        var backupService = Substitute.For<IBackupManagementService>();
        var provider = new BackupProvider { Id = 1, Name = "P", Enabled = true, Configuration = "{\"MaxBackupsCount\":2}" };
        backupService.GetProvidersAsync().Returns(new List<BackupProvider> { provider });
        var now = DateTime.UtcNow;
        backupService.GetHistoryAsync(1, 1000, Arg.Any<CancellationToken>()).Returns(new List<BackupHistory>
        {
            History(1, 1, BackupStatus.Success, now.AddDays(-4)),
            History(2, 1, BackupStatus.Success, now.AddDays(-3)),
            History(3, 1, BackupStatus.Success, now.AddDays(-2)),
            History(4, 1, BackupStatus.Success, now.AddDays(-1)),
            History(5, 1, BackupStatus.Success, now)
        });

        await InvokeCleanupAsync(sut, backupService);

        // Newest 2 (ids 4 and 5) are kept; the 3 oldest are deleted.
        await backupService.Received(1).DeleteBackupAsync(1, Arg.Any<CancellationToken>());
        await backupService.Received(1).DeleteBackupAsync(2, Arg.Any<CancellationToken>());
        await backupService.Received(1).DeleteBackupAsync(3, Arg.Any<CancellationToken>());
        await backupService.DidNotReceive().DeleteBackupAsync(4, Arg.Any<CancellationToken>());
        await backupService.DidNotReceive().DeleteBackupAsync(5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CleanupByMaxBackupsCountAsync_FailedBackupsExcludedFromCountAndNeverDeleted()
    {
        var sut = CreateSut();
        var backupService = Substitute.For<IBackupManagementService>();
        var provider = new BackupProvider { Id = 1, Name = "P", Enabled = true, Configuration = "{\"MaxBackupsCount\":1}" };
        backupService.GetProvidersAsync().Returns(new List<BackupProvider> { provider });
        var now = DateTime.UtcNow;
        backupService.GetHistoryAsync(1, 1000, Arg.Any<CancellationToken>()).Returns(new List<BackupHistory>
        {
            History(1, 1, BackupStatus.Failed, now.AddDays(-5)),
            History(2, 1, BackupStatus.Success, now)
        });

        await InvokeCleanupAsync(sut, backupService);

        // Only 1 successful backup exists (<= max of 1) -> nothing to delete, and the
        // failed entry is never even considered for deletion.
        await backupService.DidNotReceiveWithAnyArgs().DeleteBackupAsync(default, default);
    }

    [Fact]
    public async Task CleanupByMaxBackupsCountAsync_OneProviderThrows_OtherProviderStillProcessed()
    {
        var sut = CreateSut();
        var backupService = Substitute.For<IBackupManagementService>();
        var badProvider = new BackupProvider { Id = 1, Name = "Bad", Enabled = true, Configuration = "{\"MaxBackupsCount\":1}" };
        var goodProvider = new BackupProvider { Id = 2, Name = "Good", Enabled = true, Configuration = "{\"MaxBackupsCount\":1}" };
        backupService.GetProvidersAsync().Returns(new List<BackupProvider> { badProvider, goodProvider });
        backupService.GetHistoryAsync(1, 1000, Arg.Any<CancellationToken>())
            .Returns<Task<List<BackupHistory>>>(_ => throw new InvalidOperationException("db unavailable"));
        var now = DateTime.UtcNow;
        backupService.GetHistoryAsync(2, 1000, Arg.Any<CancellationToken>()).Returns(new List<BackupHistory>
        {
            History(3, 2, BackupStatus.Success, now.AddDays(-1)),
            History(4, 2, BackupStatus.Success, now)
        });

        var act = async () => await InvokeCleanupAsync(sut, backupService);

        await act.Should().NotThrowAsync();
        await backupService.Received(1).DeleteBackupAsync(3, Arg.Any<CancellationToken>());
    }
}
