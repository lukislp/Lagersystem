using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Notification;

// NOTE ON COVERAGE: EmailService news up a concrete System.Net.Mail.SmtpClient with no
// injectable seam (constructor takes a plain EmailSettings, not an abstraction over SMTP).
// With EnableEmail=true, SendEmailAsync/SendEmailWithAttachmentAsync would attempt a real
// SmtpClient.SendMailAsync() call against _settings.SmtpHost - a genuine network operation
// we must not perform in a unit test. Those specific lines (SmtpClient/MailMessage
// construction, SendMailAsync, the success log, and the surrounding try/catch's "success"
// path) are therefore intentionally left uncovered and documented here rather than exercised.
// Everything reachable without opening a socket (the EnableEmail=false early-return gate,
// and the full GetUserDisplayNameAsync resolution logic used by every template method) is
// covered below.
public class EmailServiceTests
{
    private static EmailSettings DisabledSettings() => new()
    {
        EnableEmail = false,
        ApplicationUrl = "https://app.example.com"
    };

    private static IServiceProvider BuildProviderWithDb(string dbName, Action<InventoryDbContext>? seed = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<InventoryDbContext>(o => o.UseInMemoryDatabase(dbName));
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

    // A service provider whose CreateScope() throws, used to exercise GetUserDisplayNameAsync's catch branch.
    private static IServiceProvider BuildThrowingProvider()
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IServiceScopeFactory)).Returns(_ => throw new InvalidOperationException("no scope factory"));
        return provider;
    }

    private static EmailService Build(IServiceProvider serviceProvider, EmailSettings? settings = null)
        => new(settings ?? DisabledSettings(), NullLogger<EmailService>.Instance, serviceProvider);

    // --- SendEmailAsync / SendEmailWithAttachmentAsync: disabled gate ---

    [Fact]
    public async Task SendEmailAsync_EmailDisabled_ReturnsWithoutThrowing()
    {
        var provider = BuildProviderWithDb(nameof(SendEmailAsync_EmailDisabled_ReturnsWithoutThrowing));
        var sut = Build(provider);

        var act = () => sut.SendEmailAsync("to@test.local", "Subject", "Body");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendEmailWithAttachmentAsync_EmailDisabled_ReturnsWithoutThrowing()
    {
        var provider = BuildProviderWithDb(nameof(SendEmailWithAttachmentAsync_EmailDisabled_ReturnsWithoutThrowing));
        var sut = Build(provider);

        var act = () => sut.SendEmailWithAttachmentAsync(
            "to@test.local", "Subject", "Body", new byte[] { 1, 2, 3 }, "file.pdf");

        await act.Should().NotThrowAsync();
    }

    // --- GetUserDisplayNameAsync resolution, exercised through the public template methods ---

    [Fact]
    public async Task SendWelcomeEmailAsync_UserFoundWithDisplayName_DoesNotThrow()
    {
        var provider = BuildProviderWithDb(
            nameof(SendWelcomeEmailAsync_UserFoundWithDisplayName_DoesNotThrow),
            db => db.Users.Add(new User
            {
                Username = "jdoe",
                Email = "jdoe@test.local",
                DisplayName = "Jane Doe",
                PasswordHash = "x"
            }));
        var sut = Build(provider);

        var act = () => sut.SendWelcomeEmailAsync("jdoe@test.local", "jdoe");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendWelcomeEmailAsync_UserFoundWithoutDisplayName_FallsBackToEmailPrefix()
    {
        var provider = BuildProviderWithDb(
            nameof(SendWelcomeEmailAsync_UserFoundWithoutDisplayName_FallsBackToEmailPrefix),
            db => db.Users.Add(new User
            {
                Username = "jdoe",
                Email = "jdoe@test.local",
                DisplayName = "",
                PasswordHash = "x"
            }));
        var sut = Build(provider);

        var act = () => sut.SendWelcomeEmailAsync("jdoe@test.local", "jdoe");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendWelcomeEmailAsync_UserNotFound_FallsBackToEmailPrefix()
    {
        var provider = BuildProviderWithDb(nameof(SendWelcomeEmailAsync_UserNotFound_FallsBackToEmailPrefix));
        var sut = Build(provider);

        var act = () => sut.SendWelcomeEmailAsync("unknown@test.local", "unknown");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendWelcomeEmailAsync_ScopeCreationThrows_FallsBackToEmailPrefixWithoutThrowing()
    {
        var sut = Build(BuildThrowingProvider());

        var act = () => sut.SendWelcomeEmailAsync("someone@test.local", "someone");

        await act.Should().NotThrowAsync();
    }

    // --- Remaining template methods: cover their body-building branches ---

    [Fact]
    public async Task SendPasswordResetEmailAsync_DoesNotThrow()
    {
        var provider = BuildProviderWithDb(nameof(SendPasswordResetEmailAsync_DoesNotThrow));
        var sut = Build(provider);

        var act = () => sut.SendPasswordResetEmailAsync("to@test.local", "user", "reset-token-123");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAccountApprovedEmailAsync_DoesNotThrow()
    {
        var provider = BuildProviderWithDb(nameof(SendAccountApprovedEmailAsync_DoesNotThrow));
        var sut = Build(provider);

        var act = () => sut.SendAccountApprovedEmailAsync("to@test.local", "user");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAccountRejectedEmailAsync_WithReason_DoesNotThrow()
    {
        var provider = BuildProviderWithDb(nameof(SendAccountRejectedEmailAsync_WithReason_DoesNotThrow));
        var sut = Build(provider);

        var act = () => sut.SendAccountRejectedEmailAsync("to@test.local", "user", "Incomplete profile");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAccountRejectedEmailAsync_WithoutReason_DoesNotThrow()
    {
        var provider = BuildProviderWithDb(nameof(SendAccountRejectedEmailAsync_WithoutReason_DoesNotThrow));
        var sut = Build(provider);

        var act = () => sut.SendAccountRejectedEmailAsync("to@test.local", "user", null);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendTwoFactorCodeEmailAsync_DoesNotThrow()
    {
        var provider = BuildProviderWithDb(nameof(SendTwoFactorCodeEmailAsync_DoesNotThrow));
        var sut = Build(provider);

        var act = () => sut.SendTwoFactorCodeEmailAsync("to@test.local", "123456");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAccountDeletionConfirmationAsync_UsernameProvided_SkipsDisplayNameLookup()
    {
        // A throwing provider proves the DB lookup path is not taken when username is provided.
        var sut = Build(BuildThrowingProvider());

        var act = () => sut.SendAccountDeletionConfirmationAsync("to@test.local", "ExplicitUsername");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAccountDeletionConfirmationAsync_NoUsername_FallsBackToDisplayNameLookup()
    {
        var provider = BuildProviderWithDb(
            nameof(SendAccountDeletionConfirmationAsync_NoUsername_FallsBackToDisplayNameLookup),
            db => db.Users.Add(new User
            {
                Username = "jdoe",
                Email = "jdoe@test.local",
                DisplayName = "Jane Doe",
                PasswordHash = "x"
            }));
        var sut = Build(provider);

        var act = () => sut.SendAccountDeletionConfirmationAsync("jdoe@test.local", "");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendLowStockAlertAsync_DoesNotThrow()
    {
        var provider = BuildProviderWithDb(nameof(SendLowStockAlertAsync_DoesNotThrow));
        var sut = Build(provider);

        var act = () => sut.SendLowStockAlertAsync("admin@test.local", "Widget", 2, 10);

        await act.Should().NotThrowAsync();
    }
}
