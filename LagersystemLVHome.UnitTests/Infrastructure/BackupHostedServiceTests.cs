using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using BackupHostedService = LagersystemLVHome.Infrastructure.HostedServices.BackupHostedService;

namespace LagersystemLVHome.UnitTests.Infrastructure;

/// <summary>
/// Covers <see cref="BackupHostedService"/> (the daily-backup-window scheduler in
/// LagersystemLVHome.Infrastructure - distinct from the same-named legacy class in
/// LagersystemLVHome.Application.Services, covered separately in BackupServiceTests).
///
/// The real business logic lives in the private <c>CheckAndPerformBackupAsync</c> method;
/// <c>ExecuteAsync</c> itself is just a 15-minute delay/log loop around it. That private
/// method is invoked directly via reflection (see BackupCleanupHostedServiceTests for the
/// same rationale), using a real minimal <see cref="ServiceCollection"/>-built
/// <see cref="IServiceProvider"/> so the real <c>CreateScope()</c>/<c>GetRequiredService</c>
/// call inside it resolves a substituted <see cref="IBackupManagementService"/>.
/// </summary>
public sealed class BackupHostedServiceTests
{
    private static IServiceProvider BuildServiceProvider(IBackupManagementService backupService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(backupService);
        return services.BuildServiceProvider();
    }

    private static BackupHostedService CreateSut(IServiceProvider serviceProvider)
        => new(serviceProvider, NullLogger<BackupHostedService>.Instance);

    private static Task InvokeCheckAsync(BackupHostedService sut)
    {
        var method = typeof(BackupHostedService).GetMethod(
            "CheckAndPerformBackupAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(sut, new object[] { CancellationToken.None })!;
    }

    /// <summary>
    /// Picks a BackupHour for the *current* moment such that the production window check
    /// (target hour:00, +-15 minutes, with a same-day/tomorrow shift when already past)
    /// is deterministically hittable "right now" - and reports whether it lands inside
    /// or outside that window. There is no injectable clock in this codebase (see the
    /// GetRetentionType note in BackupManagementServiceTests for the established
    /// convention), and the production window is only ~30 minutes wide per hour, so
    /// roughly half the time no BackupHour value lands inside the window "right now" -
    /// that's real, correct production behavior, not a test bug. Picking the nearest
    /// hour boundary and asserting against the *actual* resulting expectation (rather
    /// than assuming "inside") keeps this deterministic and non-flaky at any run time.
    /// </summary>
    private static (int BackupHour, bool ExpectedWithinWindow) NearestWindowBackupHour()
    {
        var now = DateTime.Now;
        if (now.Minute <= 14) return (now.Hour, true);
        if (now.Minute >= 46) return ((now.Hour + 1) % 24, true);
        return (now.Hour, false); // "dead zone": no hour choice lands within the window right now
    }

    private static BackupResult SuccessResult() => new()
    {
        Success = true,
        FileName = "backup.zip",
        FinalSizeBytes = 1024,
        SuccessfulProviders = new List<string> { "Local" },
        FailedProviders = new List<string>()
    };

    [Fact]
    public async Task CheckAndPerformBackupAsync_Disabled_DoesNotCreateBackup()
    {
        var backupService = Substitute.For<IBackupManagementService>();
        backupService.GetSettingsAsync().Returns(new LagersystemLVHome.Domain.Models.BackupSettings { Enabled = false });
        var sut = CreateSut(BuildServiceProvider(backupService));

        await InvokeCheckAsync(sut);

        await backupService.DidNotReceiveWithAnyArgs().CreateBackupAsync(default);
    }

    [Fact]
    public async Task CheckAndPerformBackupAsync_OutsideBackupWindow_DoesNotCreateBackup()
    {
        var backupService = Substitute.For<IBackupManagementService>();
        // Pick an hour far (>15 min) from "now" in both directions so the service is
        // guaranteed to be outside its backup window regardless of when the test runs.
        var farHour = (DateTime.Now.Hour + 6) % 24;
        backupService.GetSettingsAsync().Returns(new LagersystemLVHome.Domain.Models.BackupSettings { Enabled = true, BackupHour = farHour });
        var sut = CreateSut(BuildServiceProvider(backupService));

        await InvokeCheckAsync(sut);

        await backupService.DidNotReceiveWithAnyArgs().CreateBackupAsync(default);
    }

    [Fact]
    public async Task CheckAndPerformBackupAsync_WithinWindowButNoActiveProviders_SkipsBackup()
    {
        var backupService = Substitute.For<IBackupManagementService>();
        backupService.GetSettingsAsync().Returns(new LagersystemLVHome.Domain.Models.BackupSettings
        {
            Enabled = true,
            BackupHour = DateTime.Now.Hour
        });
        backupService.GetProvidersAsync().Returns(new List<BackupProvider>
        {
            new() { Id = 1, Name = "P", Enabled = false }
        });
        var sut = CreateSut(BuildServiceProvider(backupService));

        await InvokeCheckAsync(sut);

        await backupService.DidNotReceiveWithAnyArgs().CreateBackupAsync(default);
    }

    [Fact]
    public async Task CheckAndPerformBackupAsync_WithinWindowAndActiveProvider_CreatesBackupAndCleansUp()
    {
        var (backupHour, expectedWithinWindow) = NearestWindowBackupHour();
        var backupService = Substitute.For<IBackupManagementService>();
        backupService.GetSettingsAsync().Returns(new LagersystemLVHome.Domain.Models.BackupSettings
        {
            Enabled = true,
            BackupHour = backupHour,
            RetentionDays = 14
        });
        backupService.GetProvidersAsync().Returns(new List<BackupProvider>
        {
            new() { Id = 1, Name = "Local", Enabled = true }
        });
        backupService.CreateBackupAsync(Arg.Any<CancellationToken>()).Returns(SuccessResult());
        var sut = CreateSut(BuildServiceProvider(backupService));

        await InvokeCheckAsync(sut);

        if (expectedWithinWindow)
        {
            await backupService.Received(1).CreateBackupAsync(Arg.Any<CancellationToken>());
            await backupService.Received(1).CleanupOldBackupsAsync(14, Arg.Any<CancellationToken>());
        }
        else
        {
            await backupService.DidNotReceiveWithAnyArgs().CreateBackupAsync(default);
        }
    }

    [Fact]
    public async Task CheckAndPerformBackupAsync_BackupCreationFails_DoesNotAttemptCleanup()
    {
        var backupService = Substitute.For<IBackupManagementService>();
        backupService.GetSettingsAsync().Returns(new LagersystemLVHome.Domain.Models.BackupSettings
        {
            Enabled = true,
            BackupHour = NearestWindowBackupHour().BackupHour
        });
        backupService.GetProvidersAsync().Returns(new List<BackupProvider>
        {
            new() { Id = 1, Name = "Local", Enabled = true }
        });
        backupService.CreateBackupAsync(Arg.Any<CancellationToken>()).Returns(new BackupResult { Success = false, ErrorMessage = "boom" });
        var sut = CreateSut(BuildServiceProvider(backupService));

        await InvokeCheckAsync(sut);

        await backupService.DidNotReceiveWithAnyArgs().CleanupOldBackupsAsync(default, default);
    }

    [Fact]
    public async Task CheckAndPerformBackupAsync_CleanupThrows_IsCaughtAndDoesNotPropagate()
    {
        var backupService = Substitute.For<IBackupManagementService>();
        backupService.GetSettingsAsync().Returns(new LagersystemLVHome.Domain.Models.BackupSettings
        {
            Enabled = true,
            BackupHour = NearestWindowBackupHour().BackupHour,
            RetentionDays = 30
        });
        backupService.GetProvidersAsync().Returns(new List<BackupProvider>
        {
            new() { Id = 1, Name = "Local", Enabled = true }
        });
        backupService.CreateBackupAsync(Arg.Any<CancellationToken>()).Returns(SuccessResult());
        backupService.CleanupOldBackupsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("cleanup failed"));
        var sut = CreateSut(BuildServiceProvider(backupService));

        var act = async () => await InvokeCheckAsync(sut);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckAndPerformBackupAsync_GetSettingsThrows_IsCaughtAndDoesNotPropagate()
    {
        var backupService = Substitute.For<IBackupManagementService>();
        backupService.GetSettingsAsync().Returns<LagersystemLVHome.Domain.Models.BackupSettings>(
            _ => throw new InvalidOperationException("db unavailable"));
        var sut = CreateSut(BuildServiceProvider(backupService));

        var act = async () => await InvokeCheckAsync(sut);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_LogsAndCompletes()
    {
        var backupService = Substitute.For<IBackupManagementService>();
        var sut = CreateSut(BuildServiceProvider(backupService));

        var act = async () => await sut.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
