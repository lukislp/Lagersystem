using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace LagersystemLVHome.UnitTests.Services.Reporting;

// WeeklyReportService is a BackgroundService with no injectable clock/timer seam. Its private
// scheduling helpers (GetTimeUntilNextSunday, GetWeekNumber) and its orchestration method
// (ExecuteWeeklyReportAsync) are exercised via reflection - the same pattern already used in
// this codebase for DatabaseHealthService's private members.
public class WeeklyReportServiceTests
{
    private static IServiceProvider BuildProvider(
        IPdfReportService pdf, IEmailService email, Action<InventoryDbContext>? seed = null)
    {
        // IMPORTANT: the db name must be captured in a local BEFORE being passed into the
        // options lambda. AddDbContext re-invokes this lambda every time a new DbContext
        // instance is created (i.e. once per scope), so generating Guid.NewGuid() inline here
        // would hand every scope a different, mutually-invisible InMemory database.
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<InventoryDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton(pdf);
        services.AddSingleton(email);
        var provider = services.BuildServiceProvider();

        if (seed != null)
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            seed(db);
            db.SaveChanges();
        }

        return provider;
    }

    private static WeeklyReportService Build(IServiceProvider provider)
        => new(provider, NullLogger<WeeklyReportService>.Instance);

    private static async Task InvokeExecuteWeeklyReportAsync(WeeklyReportService sut)
    {
        var method = typeof(WeeklyReportService).GetMethod("ExecuteWeeklyReportAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task)method.Invoke(sut, new object[] { CancellationToken.None })!;
        await task;
    }

    private static TimeSpan InvokeGetTimeUntilNextSunday(WeeklyReportService sut)
    {
        var method = typeof(WeeklyReportService).GetMethod("GetTimeUntilNextSunday", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (TimeSpan)method.Invoke(sut, null)!;
    }

    private static int InvokeGetWeekNumber(WeeklyReportService sut, DateTime date)
    {
        var method = typeof(WeeklyReportService).GetMethod("GetWeekNumber", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (int)method.Invoke(sut, new object[] { date })!;
    }

    private static User SuperAdmin(string username) => new()
    {
        Username = username,
        Email = $"{username}@test.local",
        PasswordHash = "x",
        Role = UserRole.SuperAdmin,
        ApprovalStatus = UserApprovalStatus.Approved
    };

    // --- GetTimeUntilNextSunday: pure scheduling math based on DateTime.Now. We accept any
    // valid outcome (see task conventions) rather than pinning wall-clock-dependent values. ---

    [Fact]
    public void GetTimeUntilNextSunday_ReturnsNonNegativeDurationWithinAWeek()
    {
        var sut = Build(BuildProvider(Substitute.For<IPdfReportService>(), Substitute.For<IEmailService>()));

        var result = InvokeGetTimeUntilNextSunday(sut);

        result.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        result.Should().BeLessThanOrEqualTo(TimeSpan.FromDays(7));
    }

    // --- GetWeekNumber: deterministic given an explicit date, independent of wall clock. ---

    [Fact]
    public void GetWeekNumber_OneWeekLater_IncrementsByOne()
    {
        var sut = Build(BuildProvider(Substitute.For<IPdfReportService>(), Substitute.For<IEmailService>()));
        var date = new DateTime(2026, 7, 6); // mid-year Monday, safely away from year-boundary wraparound

        var week1 = InvokeGetWeekNumber(sut, date);
        var week2 = InvokeGetWeekNumber(sut, date.AddDays(7));

        week2.Should().Be(week1 + 1);
    }

    // --- ExecuteWeeklyReportAsync: full orchestration (PDF generation + per-SuperAdmin email). ---

    [Fact]
    public async Task ExecuteWeeklyReportAsync_SendsReportOnlyToApprovedSuperAdmins()
    {
        var pdf = Substitute.For<IPdfReportService>();
        pdf.GenerateWeeklyReportAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new byte[] { 1, 2, 3 });
        var email = Substitute.For<IEmailService>();

        var provider = BuildProvider(pdf, email, db =>
        {
            db.Users.Add(SuperAdmin("approved1"));
            db.Users.Add(SuperAdmin("approved2"));
            var pending = SuperAdmin("pending");
            pending.ApprovalStatus = UserApprovalStatus.Pending;
            db.Users.Add(pending);
            db.Users.Add(new User
            {
                Username = "regular",
                Email = "regular@test.local",
                PasswordHash = "x",
                Role = UserRole.User,
                ApprovalStatus = UserApprovalStatus.Approved
            });
        });
        var sut = Build(provider);

        await InvokeExecuteWeeklyReportAsync(sut);

        await email.Received(1).SendEmailWithAttachmentAsync(
            "approved1@test.local", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
        await email.Received(1).SendEmailWithAttachmentAsync(
            "approved2@test.local", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
        await email.DidNotReceive().SendEmailWithAttachmentAsync(
            "pending@test.local", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
        await email.DidNotReceive().SendEmailWithAttachmentAsync(
            "regular@test.local", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteWeeklyReportAsync_NoSuperAdmins_CompletesWithoutSendingEmail()
    {
        var pdf = Substitute.For<IPdfReportService>();
        pdf.GenerateWeeklyReportAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new byte[] { 1 });
        var email = Substitute.For<IEmailService>();
        var provider = BuildProvider(pdf, email);
        var sut = Build(provider);

        var act = () => InvokeExecuteWeeklyReportAsync(sut);

        await act.Should().NotThrowAsync();
        await email.DidNotReceiveWithAnyArgs().SendEmailWithAttachmentAsync(
            default!, default!, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task ExecuteWeeklyReportAsync_PdfGenerationThrows_IsCaughtAndDoesNotPropagate()
    {
        var pdf = Substitute.For<IPdfReportService>();
        pdf.GenerateWeeklyReportAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<Task<byte[]>>(_ => throw new InvalidOperationException("pdf render failed"));
        var email = Substitute.For<IEmailService>();
        var provider = BuildProvider(pdf, email, db => db.Users.Add(SuperAdmin("admin")));
        var sut = Build(provider);

        var act = () => InvokeExecuteWeeklyReportAsync(sut);

        await act.Should().NotThrowAsync();
        await email.DidNotReceiveWithAnyArgs().SendEmailWithAttachmentAsync(
            default!, default!, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task ExecuteWeeklyReportAsync_OneAdminEmailFails_OtherAdminsStillReceiveReport()
    {
        var pdf = Substitute.For<IPdfReportService>();
        pdf.GenerateWeeklyReportAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new byte[] { 1, 2, 3 });
        var email = Substitute.For<IEmailService>();
        email.SendEmailWithAttachmentAsync(
                "failing@test.local", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("smtp down"));

        var provider = BuildProvider(pdf, email, db =>
        {
            var failing = SuperAdmin("failing");
            db.Users.Add(failing);
            db.Users.Add(SuperAdmin("succeeding"));
        });
        var sut = Build(provider);

        var act = () => InvokeExecuteWeeklyReportAsync(sut);

        await act.Should().NotThrowAsync();
        await email.Received(1).SendEmailWithAttachmentAsync(
            "succeeding@test.local", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
    }

    // --- Lifecycle: StartAsync schedules the timer (via ExecuteAsync), Dispose tears it down. ---

    [Fact]
    public async Task StartAsync_SchedulesTimerWithoutThrowing_AndDisposeCleansUp()
    {
        var provider = BuildProvider(Substitute.For<IPdfReportService>(), Substitute.For<IEmailService>());
        var sut = Build(provider);

        var act = async () =>
        {
            await sut.StartAsync(CancellationToken.None);
            await sut.StopAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
        sut.Dispose();
    }
}
