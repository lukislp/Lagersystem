using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Reporting;

/// <summary>
/// Covers <see cref="ApplicationUptimeService"/>.
///
/// DESIGN NOTE / POTENTIAL BUG: <c>ApplicationUptimeService.UptimeFilePath</c> is a hardcoded
/// <c>static readonly</c> path under the real <c>%APPDATA%\LagerSystem\uptime.json</c> - it is not
/// injected (no <c>IFileSystem</c>/path-provider abstraction), so every instance of this service in
/// every process on the same machine (including, notably, concurrent "dotnet test" runs from
/// unrelated worktrees checked out under the same Windows user profile) reads and writes the exact
/// same file. That makes the class inherently hard to unit test in isolation and a latent source of
/// cross-process test flakiness/data races outside of this test class's control. These tests
/// mitigate the risk for THIS test class by snapshotting/restoring the real file's bytes around each
/// test (xUnit does not parallelize [Fact]s within one class instance sequence by default), but
/// cannot protect against a genuinely concurrent writer in another process.
/// </summary>
public sealed class ApplicationUptimeServiceTests : IDisposable
{
    private static readonly string UptimeFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LagerSystem",
        "uptime.json");

    private readonly byte[]? _originalContent;
    private readonly bool _originalExisted;

    public ApplicationUptimeServiceTests()
    {
        _originalExisted = File.Exists(UptimeFilePath);
        _originalContent = _originalExisted ? File.ReadAllBytes(UptimeFilePath) : null;
    }

    public void Dispose()
    {
        try
        {
            if (_originalExisted && _originalContent is not null)
            {
                var dir = Path.GetDirectoryName(UptimeFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllBytes(UptimeFilePath, _originalContent);
            }
            else if (File.Exists(UptimeFilePath))
            {
                File.Delete(UptimeFilePath);
            }
        }
        catch { /* best-effort restore */ }
    }

    private static void DeleteUptimeFileIfExists()
    {
        if (File.Exists(UptimeFilePath))
        {
            File.Delete(UptimeFilePath);
        }
    }

    [Fact]
    public void Constructor_NoExistingFile_CreatesFreshUptimeDataWithZeroRecycles()
    {
        DeleteUptimeFileIfExists();

        var sut = new ApplicationUptimeService(NullLogger<ApplicationUptimeService>.Instance);

        sut.RecycleCount.Should().Be(0);
        sut.ApplicationStartTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        sut.LastRecycleTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        File.Exists(UptimeFilePath).Should().BeTrue("first initialization should persist the new uptime data");
    }

    [Fact]
    public void Constructor_ExistingValidFile_IncrementsRecycleCountAndPreservesOriginalStartTime()
    {
        DeleteUptimeFileIfExists();
        var originalStart = DateTime.UtcNow.AddDays(-3);
        var dir = Path.GetDirectoryName(UptimeFilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(UptimeFilePath, System.Text.Json.JsonSerializer.Serialize(new
        {
            ApplicationStartTime = originalStart,
            LastRecycleTime = originalStart,
            RecycleCount = 4
        }));

        var sut = new ApplicationUptimeService(NullLogger<ApplicationUptimeService>.Instance);

        sut.RecycleCount.Should().Be(5, "loading an existing file counts as a recycle/IIS restart");
        sut.ApplicationStartTime.Should().BeCloseTo(originalStart, TimeSpan.FromSeconds(2), "start time must be preserved across recycles");
        sut.LastRecycleTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Constructor_CorruptFile_FallsBackToFreshInMemoryData()
    {
        DeleteUptimeFileIfExists();
        var dir = Path.GetDirectoryName(UptimeFilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(UptimeFilePath, "{ this is not valid json ][");

        var sut = new ApplicationUptimeService(NullLogger<ApplicationUptimeService>.Instance);

        sut.RecycleCount.Should().Be(0);
        sut.ApplicationStartTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Constructor_FileDeserializesToNull_TreatsAsFirstInitialization()
    {
        // "null" is valid JSON but deserializes UptimeData? to null, which the production code
        // explicitly checks for (data != null) and falls through to "first initialization" instead
        // of the corrupt-file catch block.
        DeleteUptimeFileIfExists();
        var dir = Path.GetDirectoryName(UptimeFilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(UptimeFilePath, "null");

        var sut = new ApplicationUptimeService(NullLogger<ApplicationUptimeService>.Instance);

        sut.RecycleCount.Should().Be(0);
    }

    [Fact]
    public void ApplicationUptime_ReflectsElapsedTimeSinceStart()
    {
        DeleteUptimeFileIfExists();
        var sut = new ApplicationUptimeService(NullLogger<ApplicationUptimeService>.Instance);

        sut.ApplicationUptime.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
        sut.ApplicationUptime.Should().BeLessThan(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ProcessUptime_ReflectsElapsedTimeSinceProcessStart()
    {
        DeleteUptimeFileIfExists();
        var sut = new ApplicationUptimeService(NullLogger<ApplicationUptimeService>.Instance);

        // The current test process has necessarily been running for a non-negative duration.
        sut.ProcessUptime.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public void Constructor_DirectoryMissing_CreatesItBeforeWriting()
    {
        var dir = Path.GetDirectoryName(UptimeFilePath)!;
        DeleteUptimeFileIfExists();
        if (Directory.Exists(dir))
        {
            // Only remove the directory if it is now empty (uptime.json was the only file we manage there).
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }

        var act = () => new ApplicationUptimeService(NullLogger<ApplicationUptimeService>.Instance);

        act.Should().NotThrow();
        Directory.Exists(dir).Should().BeTrue();
    }

    [Fact]
    public void Constructor_SaveUptimeDataFails_CatchesAndStillReturnsUsableInstance()
    {
        // Forces SaveUptimeData's own File.WriteAllText call to throw by making the target path
        // a directory instead of a file - the exception must be caught internally (SaveUptimeData
        // has its own try/catch) without preventing LoadOrCreateUptimeData/the constructor from
        // completing and returning a usable (if unpersisted) instance.
        DeleteUptimeFileIfExists();
        var dir = Path.GetDirectoryName(UptimeFilePath)!;
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(UptimeFilePath); // a directory occupies the file's own path

        try
        {
            var act = () => new ApplicationUptimeService(NullLogger<ApplicationUptimeService>.Instance);

            act.Should().NotThrow();
        }
        finally
        {
            Directory.Delete(UptimeFilePath, recursive: true);
        }
    }
}
