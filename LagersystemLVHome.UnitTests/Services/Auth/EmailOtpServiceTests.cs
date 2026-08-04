using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Auth;

public class EmailOtpServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task SeedUserAsync(IDbContextFactory<InventoryDbContext> factory, int id, string? email)
    {
        await using var db = factory.CreateDbContext();
        db.Users.Add(new User
        {
            Id = id,
            Username = $"u{id}",
            Email = email ?? "",
            PasswordHash = "h",
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private static EmailOtpService Build(IDbContextFactory<InventoryDbContext> factory, IEmailService email)
        => new(factory, email, NullLogger<EmailOtpService>.Instance);

    [Fact]
    public async Task SendOtpAsync_UnknownUser_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(SendOtpAsync_UnknownUser_ReturnsFalse));
        var email = Substitute.For<IEmailService>();
        var sut = Build(factory, email);

        (await sut.SendOtpAsync(999)).Should().BeFalse();
        await email.DidNotReceiveWithAnyArgs().SendTwoFactorCodeEmailAsync(default!, default!);
    }

    [Fact]
    public async Task SendOtpAsync_UserWithoutEmail_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(SendOtpAsync_UserWithoutEmail_ReturnsFalse));
        await SeedUserAsync(factory, 1, email: "");
        var email = Substitute.For<IEmailService>();

        (await Build(factory, email).SendOtpAsync(1)).Should().BeFalse();
    }

    [Fact]
    public async Task SendOtpAsync_HappyPath_PersistsTokenAndSendsEmail()
    {
        var factory = CreateFactory(nameof(SendOtpAsync_HappyPath_PersistsTokenAndSendsEmail));
        await SeedUserAsync(factory, 1, "u@x.de");
        var email = Substitute.For<IEmailService>();

        var ok = await Build(factory, email).SendOtpAsync(1, ipAddress: "1.2.3.4");

        ok.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        var token = await db.EmailOtpTokens.SingleAsync();
        token.Code.Should().MatchRegex("^[0-9]{6}$");
        token.IpAddress.Should().Be("1.2.3.4");
        token.IsUsed.Should().BeFalse();
        await email.Received(1).SendTwoFactorCodeEmailAsync("u@x.de", token.Code);
    }

    [Fact]
    public async Task SendOtpAsync_TooManyActiveTokens_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(SendOtpAsync_TooManyActiveTokens_ReturnsFalse));
        await SeedUserAsync(factory, 1, "u@x.de");
        await using (var seed = factory.CreateDbContext())
        {
            for (int i = 0; i < 3; i++)
            {
                seed.EmailOtpTokens.Add(new EmailOtpToken
                {
                    UserId = 1,
                    Code = "111111",
                    ExpiresAt = DateTime.UtcNow.AddMinutes(5)
                });
            }
            await seed.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();

        (await Build(factory, email).SendOtpAsync(1)).Should().BeFalse();
    }

    [Fact]
    public async Task SendOtpAsync_EmailFailure_RemovesToken()
    {
        var factory = CreateFactory(nameof(SendOtpAsync_EmailFailure_RemovesToken));
        await SeedUserAsync(factory, 1, "u@x.de");
        var email = Substitute.For<IEmailService>();
        email
            .When(e => e.SendTwoFactorCodeEmailAsync(Arg.Any<string>(), Arg.Any<string>()))
            .Do(_ => throw new InvalidOperationException("smtp down"));

        var ok = await Build(factory, email).SendOtpAsync(1);

        ok.Should().BeFalse();
        await using var db = factory.CreateDbContext();
        (await db.EmailOtpTokens.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ValidateOtpAsync_CorrectCode_InvalidatesAllActiveTokens()
    {
        var factory = CreateFactory(nameof(ValidateOtpAsync_CorrectCode_InvalidatesAllActiveTokens));
        await SeedUserAsync(factory, 1, "u@x.de");
        await using (var seed = factory.CreateDbContext())
        {
            seed.EmailOtpTokens.Add(new EmailOtpToken { UserId = 1, Code = "654321", ExpiresAt = DateTime.UtcNow.AddMinutes(5), CreatedAt = DateTime.UtcNow });
            seed.EmailOtpTokens.Add(new EmailOtpToken { UserId = 1, Code = "999999", ExpiresAt = DateTime.UtcNow.AddMinutes(5), CreatedAt = DateTime.UtcNow.AddSeconds(-30) });
            await seed.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();

        var ok = await Build(factory, email).ValidateOtpAsync(1, "654321");

        ok.Should().BeTrue();
        await using var db = factory.CreateDbContext();
        (await db.EmailOtpTokens.CountAsync(t => !t.IsUsed)).Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    public async Task ValidateOtpAsync_InvalidLength_ReturnsFalse(string code)
    {
        var factory = CreateFactory(nameof(ValidateOtpAsync_InvalidLength_ReturnsFalse) + code);
        var email = Substitute.For<IEmailService>();

        (await Build(factory, email).ValidateOtpAsync(1, code)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateOtpAsync_NoActiveToken_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(ValidateOtpAsync_NoActiveToken_ReturnsFalse));
        var email = Substitute.For<IEmailService>();

        (await Build(factory, email).ValidateOtpAsync(1, "123456")).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateOtpAsync_WrongCode_IncrementsFailedAttempts()
    {
        var factory = CreateFactory(nameof(ValidateOtpAsync_WrongCode_IncrementsFailedAttempts));
        await SeedUserAsync(factory, 1, "u@x.de");
        await using (var seed = factory.CreateDbContext())
        {
            seed.EmailOtpTokens.Add(new EmailOtpToken { UserId = 1, Code = "111111", ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
            await seed.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();

        var ok = await Build(factory, email).ValidateOtpAsync(1, "222222");

        ok.Should().BeFalse();
        await using var db = factory.CreateDbContext();
        (await db.EmailOtpTokens.SingleAsync()).FailedAttempts.Should().Be(1);
    }

    [Fact]
    public async Task ValidateOtpAsync_TooManyFailedAttempts_InvalidatesToken()
    {
        var factory = CreateFactory(nameof(ValidateOtpAsync_TooManyFailedAttempts_InvalidatesToken));
        await SeedUserAsync(factory, 1, "u@x.de");
        await using (var seed = factory.CreateDbContext())
        {
            seed.EmailOtpTokens.Add(new EmailOtpToken { UserId = 1, Code = "111111", ExpiresAt = DateTime.UtcNow.AddMinutes(5), FailedAttempts = 5 });
            await seed.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();

        var ok = await Build(factory, email).ValidateOtpAsync(1, "111111");

        ok.Should().BeFalse();
        await using var db = factory.CreateDbContext();
        (await db.EmailOtpTokens.SingleAsync()).IsUsed.Should().BeTrue();
    }

    [Fact]
    public async Task CleanupExpiredTokensAsync_RemovesOldExpiredAndUsed()
    {
        var factory = CreateFactory(nameof(CleanupExpiredTokensAsync_RemovesOldExpiredAndUsed));
        await using (var seed = factory.CreateDbContext())
        {
            seed.EmailOtpTokens.Add(new EmailOtpToken { UserId = 1, Code = "x", ExpiresAt = DateTime.UtcNow.AddHours(-3), CreatedAt = DateTime.UtcNow.AddHours(-3) });
            seed.EmailOtpTokens.Add(new EmailOtpToken { UserId = 1, Code = "y", IsUsed = true, ExpiresAt = DateTime.UtcNow.AddMinutes(5), CreatedAt = DateTime.UtcNow.AddHours(-2) });
            seed.EmailOtpTokens.Add(new EmailOtpToken { UserId = 1, Code = "z", ExpiresAt = DateTime.UtcNow.AddMinutes(5), CreatedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();

        await Build(factory, email).CleanupExpiredTokensAsync();

        await using var db = factory.CreateDbContext();
        (await db.EmailOtpTokens.SingleAsync()).Code.Should().Be("z");
    }
}
