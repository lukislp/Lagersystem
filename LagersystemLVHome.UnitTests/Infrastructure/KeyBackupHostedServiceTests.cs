using System.Reflection;
using LagersystemLVHome.Infrastructure.HostedServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Infrastructure;

/// <summary>
/// Covers <see cref="KeyBackupHostedService"/>. Same rationale/technique as
/// BackupHostedServiceTests: the real branching logic lives in the private
/// <c>CheckAndPerformKeyBackupAsync</c>, invoked directly via reflection through a real
/// minimal <see cref="ServiceCollection"/>-built <see cref="IServiceProvider"/> so the
/// production <c>CreateScope()</c> call resolves a substituted <see cref="IKeyBackupService"/>.
/// </summary>
public sealed class KeyBackupHostedServiceTests
{
    private static IServiceProvider BuildServiceProvider(IKeyBackupService keyBackupService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(keyBackupService);
        return services.BuildServiceProvider();
    }

    private static KeyBackupHostedService CreateSut(IServiceProvider serviceProvider)
        => new(serviceProvider, NullLogger<KeyBackupHostedService>.Instance);

    private static Task InvokeCheckAsync(KeyBackupHostedService sut)
    {
        var method = typeof(KeyBackupHostedService).GetMethod(
            "CheckAndPerformKeyBackupAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(sut, new object[] { CancellationToken.None })!;
    }

    /// <summary>
    /// See BackupHostedServiceTests.NearestWindowBackupHour for the full rationale: there
    /// is no injectable clock, and the production +-15 minute window is only reachable for
    /// a given BackupHour during ~half of each hour, so the test must adapt its expectation
    /// to whichever branch is actually reachable "right now" instead of assuming success.
    /// </summary>
    private static (int BackupHour, bool ExpectedWithinWindow) NearestWindowBackupHour()
    {
        var now = DateTime.Now;
        if (now.Minute <= 14) return (now.Hour, true);
        if (now.Minute >= 46) return ((now.Hour + 1) % 24, true);
        return (now.Hour, false);
    }

    [Fact]
    public async Task CheckAndPerformKeyBackupAsync_Disabled_DoesNotCreateBackup()
    {
        var keyBackupService = Substitute.For<IKeyBackupService>();
        keyBackupService.GetSettingsAsync().Returns(new LagersystemLVHome.Domain.Models.KeyBackupSettings { Enabled = false });
        var sut = CreateSut(BuildServiceProvider(keyBackupService));

        await InvokeCheckAsync(sut);

        await keyBackupService.DidNotReceiveWithAnyArgs().CreateKeyBackupAsync(default);
    }

    [Fact]
    public async Task CheckAndPerformKeyBackupAsync_OutsideBackupWindow_DoesNotCreateBackup()
    {
        var keyBackupService = Substitute.For<IKeyBackupService>();
        var farHour = (DateTime.Now.Hour + 6) % 24;
        keyBackupService.GetSettingsAsync().Returns(new LagersystemLVHome.Domain.Models.KeyBackupSettings { Enabled = true, BackupHour = farHour });
        var sut = CreateSut(BuildServiceProvider(keyBackupService));

        await InvokeCheckAsync(sut);

        await keyBackupService.DidNotReceiveWithAnyArgs().CreateKeyBackupAsync(default);
    }

    [Fact]
    public async Task CheckAndPerformKeyBackupAsync_WithinWindow_CreatesKeyBackup()
    {
        var (backupHour, expectedWithinWindow) = NearestWindowBackupHour();
        var keyBackupService = Substitute.For<IKeyBackupService>();
        keyBackupService.GetSettingsAsync().Returns(new LagersystemLVHome.Domain.Models.KeyBackupSettings
        {
            Enabled = true,
            BackupHour = backupHour
        });
        keyBackupService.CreateKeyBackupAsync().Returns(new KeyBackupResult { Success = true, FileName = "keys.zip" });
        var sut = CreateSut(BuildServiceProvider(keyBackupService));

        await InvokeCheckAsync(sut);

        if (expectedWithinWindow)
        {
            await keyBackupService.Received(1).CreateKeyBackupAsync(Arg.Any<CancellationToken>());
        }
        else
        {
            await keyBackupService.DidNotReceiveWithAnyArgs().CreateKeyBackupAsync(default);
        }
    }

    [Fact]
    public async Task CheckAndPerformKeyBackupAsync_CreateBackupFails_DoesNotThrow()
    {
        var keyBackupService = Substitute.For<IKeyBackupService>();
        keyBackupService.GetSettingsAsync().Returns(new LagersystemLVHome.Domain.Models.KeyBackupSettings
        {
            Enabled = true,
            BackupHour = NearestWindowBackupHour().BackupHour
        });
        keyBackupService.CreateKeyBackupAsync().Returns(new KeyBackupResult { Success = false, ErrorMessage = "no provider" });
        var sut = CreateSut(BuildServiceProvider(keyBackupService));

        var act = async () => await InvokeCheckAsync(sut);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckAndPerformKeyBackupAsync_GetSettingsThrows_IsCaughtAndDoesNotPropagate()
    {
        var keyBackupService = Substitute.For<IKeyBackupService>();
        keyBackupService.GetSettingsAsync().Returns<LagersystemLVHome.Domain.Models.KeyBackupSettings>(
            _ => throw new InvalidOperationException("db unavailable"));
        var sut = CreateSut(BuildServiceProvider(keyBackupService));

        var act = async () => await InvokeCheckAsync(sut);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckAndPerformKeyBackupAsync_CreateKeyBackupThrows_IsCaughtAndDoesNotPropagate()
    {
        var keyBackupService = Substitute.For<IKeyBackupService>();
        keyBackupService.GetSettingsAsync().Returns(new LagersystemLVHome.Domain.Models.KeyBackupSettings
        {
            Enabled = true,
            BackupHour = NearestWindowBackupHour().BackupHour
        });
        keyBackupService.CreateKeyBackupAsync().Returns<KeyBackupResult>(_ => throw new InvalidOperationException("boom"));
        var sut = CreateSut(BuildServiceProvider(keyBackupService));

        var act = async () => await InvokeCheckAsync(sut);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_LogsAndCompletes()
    {
        var keyBackupService = Substitute.For<IKeyBackupService>();
        var sut = CreateSut(BuildServiceProvider(keyBackupService));

        var act = async () => await sut.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
