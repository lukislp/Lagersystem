using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.UnitTests.Services.Auth;

/// <summary>
/// Covers the 2FA/email-OTP *verification* methods on <see cref="AuthService"/>
/// (<see cref="AuthService.Verify2FACodeAsync"/>, <see cref="AuthService.Verify2FARecoveryCodeAsync"/>,
/// <see cref="AuthService.VerifyEmailOtpAsync"/>). These orchestrate lockout bookkeeping around
/// the injected <see cref="ITwoFactorService"/> / <see cref="IEmailOtpService"/>, which are
/// substituted here - this is distinct from <c>TwoFactorServiceTests</c> /
/// <c>EmailOtpServiceTests</c>, which cover those services' own (TOTP/email-delivery) logic.
/// </summary>
public class AuthServiceTwoFactorVerificationTests
{
    // ---- Verify2FACodeAsync -----------------------------------------------------------

    [Fact]
    public async Task Verify2FACodeAsync_WithoutTwoFactorService_ReturnsFalse()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(Verify2FACodeAsync_WithoutTwoFactorService_ReturnsFalse), includeTwoFactor: false);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u => { u.TwoFactorEnabled = true; u.TwoFactorSecret = "secret"; });

        (await fixture.Sut.Verify2FACodeAsync(user.Id, "123456")).Should().BeFalse();
    }

    [Fact]
    public async Task Verify2FACodeAsync_WithUnknownUser_ReturnsFalse()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(Verify2FACodeAsync_WithUnknownUser_ReturnsFalse));

        (await fixture.Sut.Verify2FACodeAsync(999, "123456")).Should().BeFalse();
    }

    [Fact]
    public async Task Verify2FACodeAsync_WithTwoFactorNotEnabled_ReturnsFalse()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(Verify2FACodeAsync_WithTwoFactorNotEnabled_ReturnsFalse));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u => { u.TwoFactorEnabled = false; u.TwoFactorSecret = "secret"; });

        (await fixture.Sut.Verify2FACodeAsync(user.Id, "123456")).Should().BeFalse();
    }

    [Fact]
    public async Task Verify2FACodeAsync_WithNoSecretConfigured_ReturnsFalse()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(Verify2FACodeAsync_WithNoSecretConfigured_ReturnsFalse));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u => { u.TwoFactorEnabled = true; u.TwoFactorSecret = null; });

        (await fixture.Sut.Verify2FACodeAsync(user.Id, "123456")).Should().BeFalse();
    }

    [Fact]
    public async Task Verify2FACodeAsync_WhileLockedOut_ReturnsFalseAndAuditsCritical()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(Verify2FACodeAsync_WhileLockedOut_ReturnsFalseAndAuditsCritical));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u =>
        {
            u.TwoFactorEnabled = true;
            u.TwoFactorSecret = "secret";
            u.TwoFAFailedAttempts = 10;
            u.TwoFALockedUntil = DateTime.UtcNow.AddMinutes(5);
        });

        (await fixture.Sut.Verify2FACodeAsync(user.Id, "123456")).Should().BeFalse();

        fixture.TwoFactor.DidNotReceiveWithAnyArgs().ValidateCode(default!, default!);
        await fixture.Audit.Received(1).LogAsync("2FA_LOCKED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Critical);
    }

    [Fact]
    public async Task Verify2FACodeAsync_WithExpiredLockout_ResetsCountersAndValidates()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(Verify2FACodeAsync_WithExpiredLockout_ResetsCountersAndValidates));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u =>
        {
            u.TwoFactorEnabled = true;
            u.TwoFactorSecret = "secret";
            u.TwoFAFailedAttempts = 10;
            u.TwoFALockedUntil = DateTime.UtcNow.AddMinutes(-1);
        });
        fixture.TwoFactor.ValidateCode("secret", "123456").Returns(true);

        (await fixture.Sut.Verify2FACodeAsync(user.Id, "123456")).Should().BeTrue();

        await using var verify = fixture.Factory.CreateDbContext();
        var refreshed = await verify.Users.FindAsync(user.Id);
        refreshed!.TwoFAFailedAttempts.Should().Be(0);
        refreshed.TwoFALockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task Verify2FACodeAsync_WithInvalidCode_IncrementsAttemptsAndAuditsWarning()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(Verify2FACodeAsync_WithInvalidCode_IncrementsAttemptsAndAuditsWarning));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u => { u.TwoFactorEnabled = true; u.TwoFactorSecret = "secret"; });
        fixture.TwoFactor.ValidateCode("secret", "000000").Returns(false);

        (await fixture.Sut.Verify2FACodeAsync(user.Id, "000000")).Should().BeFalse();

        await using var verify = fixture.Factory.CreateDbContext();
        (await verify.Users.FindAsync(user.Id))!.TwoFAFailedAttempts.Should().Be(1);
        await fixture.Audit.Received(1).LogAsync("2FA_VERIFICATION_FAILED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Warning);
    }

    [Fact]
    public async Task Verify2FACodeAsync_WithTenthInvalidCode_LocksAccountAndAuditsCritical()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(Verify2FACodeAsync_WithTenthInvalidCode_LocksAccountAndAuditsCritical));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u =>
        {
            u.TwoFactorEnabled = true;
            u.TwoFactorSecret = "secret";
            u.TwoFAFailedAttempts = 9;
        });
        fixture.TwoFactor.ValidateCode("secret", "000000").Returns(false);

        (await fixture.Sut.Verify2FACodeAsync(user.Id, "000000")).Should().BeFalse();

        await using var verify = fixture.Factory.CreateDbContext();
        var refreshed = await verify.Users.FindAsync(user.Id);
        refreshed!.TwoFAFailedAttempts.Should().Be(10);
        refreshed.TwoFALockedUntil.Should().HaveValue();
        await fixture.Audit.Received(1).LogAsync(
            "2FA_LOCKED_DUE_TO_FAILED_ATTEMPTS", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Critical);
    }

    [Fact]
    public async Task Verify2FACodeAsync_WithValidCode_ReturnsTrueAndResetsCounters()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(Verify2FACodeAsync_WithValidCode_ReturnsTrueAndResetsCounters));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u =>
        {
            u.TwoFactorEnabled = true;
            u.TwoFactorSecret = "secret";
            u.TwoFAFailedAttempts = 3;
        });
        fixture.TwoFactor.ValidateCode("secret", "654321").Returns(true);

        (await fixture.Sut.Verify2FACodeAsync(user.Id, "654321")).Should().BeTrue();

        await using var verify = fixture.Factory.CreateDbContext();
        (await verify.Users.FindAsync(user.Id))!.TwoFAFailedAttempts.Should().Be(0);
    }

    // ---- Verify2FARecoveryCodeAsync ----------------------------------------------------

    [Fact]
    public async Task Verify2FARecoveryCodeAsync_WithoutTwoFactorService_ReturnsFalse()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(Verify2FARecoveryCodeAsync_WithoutTwoFactorService_ReturnsFalse), includeTwoFactor: false);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);

        (await fixture.Sut.Verify2FARecoveryCodeAsync(user.Id, "code")).Should().BeFalse();
    }

    [Fact]
    public async Task Verify2FARecoveryCodeAsync_WithUnknownUser_ReturnsFalse()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(Verify2FARecoveryCodeAsync_WithUnknownUser_ReturnsFalse));

        (await fixture.Sut.Verify2FARecoveryCodeAsync(999, "code")).Should().BeFalse();
    }

    [Fact]
    public async Task Verify2FARecoveryCodeAsync_WithNoRecoveryCodesConfigured_ReturnsFalse()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(Verify2FARecoveryCodeAsync_WithNoRecoveryCodesConfigured_ReturnsFalse));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u => { u.TwoFactorEnabled = true; u.TwoFactorRecoveryCodes = null; });

        (await fixture.Sut.Verify2FARecoveryCodeAsync(user.Id, "code")).Should().BeFalse();
    }

    [Fact]
    public async Task Verify2FARecoveryCodeAsync_WithInvalidCode_ReturnsFalseAndAuditsWarning()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(Verify2FARecoveryCodeAsync_WithInvalidCode_ReturnsFalseAndAuditsWarning));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u => { u.TwoFactorEnabled = true; u.TwoFactorRecoveryCodes = "[\"a\"]"; });
        fixture.TwoFactor.ValidateRecoveryCode("[\"a\"]", "wrong").Returns(false);

        (await fixture.Sut.Verify2FARecoveryCodeAsync(user.Id, "wrong")).Should().BeFalse();

        fixture.TwoFactor.DidNotReceiveWithAnyArgs().RemoveUsedRecoveryCode(default!, default!);
        await fixture.Audit.Received(1).LogAsync("2FA_RECOVERY_CODE_FAILED", "User", user.Id, null, AuditSeverity.Warning);
    }

    [Fact]
    public async Task Verify2FARecoveryCodeAsync_WithValidCode_RemovesCodeAndAuditsInfo()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(Verify2FARecoveryCodeAsync_WithValidCode_RemovesCodeAndAuditsInfo));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u => { u.TwoFactorEnabled = true; u.TwoFactorRecoveryCodes = "[\"a\",\"b\"]"; });
        fixture.TwoFactor.ValidateRecoveryCode("[\"a\",\"b\"]", "a").Returns(true);
        fixture.TwoFactor.RemoveUsedRecoveryCode("[\"a\",\"b\"]", "a").Returns("[\"b\"]");

        (await fixture.Sut.Verify2FARecoveryCodeAsync(user.Id, "a")).Should().BeTrue();

        await using var verify = fixture.Factory.CreateDbContext();
        (await verify.Users.FindAsync(user.Id))!.TwoFactorRecoveryCodes.Should().Be("[\"b\"]");
        await fixture.Audit.Received(1).LogAsync("2FA_RECOVERY_CODE_USED", "User", user.Id, null, AuditSeverity.Info);
    }

    // ---- VerifyEmailOtpAsync -----------------------------------------------------------

    [Fact]
    public async Task VerifyEmailOtpAsync_WithoutEmailOtpService_ReturnsFalse()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(VerifyEmailOtpAsync_WithoutEmailOtpService_ReturnsFalse), includeEmailOtp: false);
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);

        (await fixture.Sut.VerifyEmailOtpAsync(user.Id, "123456")).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyEmailOtpAsync_WithUnknownUser_ReturnsFalse()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(VerifyEmailOtpAsync_WithUnknownUser_ReturnsFalse));

        (await fixture.Sut.VerifyEmailOtpAsync(999, "123456")).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyEmailOtpAsync_WhileLockedOut_ReturnsFalseWithoutValidating()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(VerifyEmailOtpAsync_WhileLockedOut_ReturnsFalseWithoutValidating));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u =>
        {
            u.TwoFAFailedAttempts = 10;
            u.TwoFALockedUntil = DateTime.UtcNow.AddMinutes(5);
        });

        (await fixture.Sut.VerifyEmailOtpAsync(user.Id, "123456")).Should().BeFalse();
        await fixture.EmailOtp.DidNotReceiveWithAnyArgs().ValidateOtpAsync(default, default!);
    }

    [Fact]
    public async Task VerifyEmailOtpAsync_WithExpiredLockout_ResetsCountersAndValidates()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(VerifyEmailOtpAsync_WithExpiredLockout_ResetsCountersAndValidates));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u =>
        {
            u.TwoFAFailedAttempts = 10;
            u.TwoFALockedUntil = DateTime.UtcNow.AddMinutes(-1);
        });
        fixture.EmailOtp.ValidateOtpAsync(user.Id, "123456").Returns(Task.FromResult(true));

        (await fixture.Sut.VerifyEmailOtpAsync(user.Id, "123456")).Should().BeTrue();
    }

    [Fact]
    public async Task VerifyEmailOtpAsync_WithInvalidCode_IncrementsAttemptsAndAuditsWarning()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(VerifyEmailOtpAsync_WithInvalidCode_IncrementsAttemptsAndAuditsWarning));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory);
        fixture.EmailOtp.ValidateOtpAsync(user.Id, "000000").Returns(Task.FromResult(false));

        (await fixture.Sut.VerifyEmailOtpAsync(user.Id, "000000")).Should().BeFalse();

        await using var verify = fixture.Factory.CreateDbContext();
        (await verify.Users.FindAsync(user.Id))!.TwoFAFailedAttempts.Should().Be(1);
        await fixture.Audit.Received(1).LogAsync("EMAIL_OTP_VERIFICATION_FAILED", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Warning);
    }

    [Fact]
    public async Task VerifyEmailOtpAsync_WithTenthInvalidCode_LocksAccountAndAuditsCritical()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(VerifyEmailOtpAsync_WithTenthInvalidCode_LocksAccountAndAuditsCritical));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u => u.TwoFAFailedAttempts = 9);
        fixture.EmailOtp.ValidateOtpAsync(user.Id, "000000").Returns(Task.FromResult(false));

        (await fixture.Sut.VerifyEmailOtpAsync(user.Id, "000000")).Should().BeFalse();

        await using var verify = fixture.Factory.CreateDbContext();
        var refreshed = await verify.Users.FindAsync(user.Id);
        refreshed!.TwoFAFailedAttempts.Should().Be(10);
        refreshed.TwoFALockedUntil.Should().HaveValue();
        await fixture.Audit.Received(1).LogAsync(
            "2FA_LOCKED_DUE_TO_FAILED_ATTEMPTS", "User", user.Id, Arg.Any<object?>(), AuditSeverity.Critical);
    }

    [Fact]
    public async Task VerifyEmailOtpAsync_WithValidCode_ReturnsTrueResetsCountersAndAuditsInfo()
    {
        var fixture = AuthServiceTestSupport.CreateFixture(nameof(VerifyEmailOtpAsync_WithValidCode_ReturnsTrueResetsCountersAndAuditsInfo));
        var user = await AuthServiceTestSupport.SeedUserAsync(fixture.Factory, u => u.TwoFAFailedAttempts = 4);
        fixture.EmailOtp.ValidateOtpAsync(user.Id, "654321").Returns(Task.FromResult(true));

        (await fixture.Sut.VerifyEmailOtpAsync(user.Id, "654321")).Should().BeTrue();

        await using var verify = fixture.Factory.CreateDbContext();
        (await verify.Users.FindAsync(user.Id))!.TwoFAFailedAttempts.Should().Be(0);
        await fixture.Audit.Received(1).LogAsync("EMAIL_OTP_VERIFIED", "User", user.Id, null, AuditSeverity.Info);
    }
}
