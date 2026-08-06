using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Auth;

/// <summary>
/// Covers <see cref="WebAuthnService"/>. This service embeds a hand-rolled, minimal CBOR
/// reader/writer for WebAuthn attestation/assertion payloads (see the "Simplified CBOR
/// parsing" comment on <c>ParseAttestationObject</c>) instead of depending on the Fido2
/// NuGet library, so there is no library interface to substitute — the only way to reach
/// the parsing/signature-verification branches is to hand-construct byte-accurate
/// attestationObject / authenticatorData / COSE-key / signature payloads, which is what
/// the <c>Build*</c> helpers below do. All EF access goes through <see cref="IDbContextFactory{TContext}"/>,
/// backed by a per-test EF InMemory database.
///
/// Not exercised here (documented, not a coverage gap in the traditional sense):
/// <list type="bullet">
/// <item>Real hardware/platform authenticator ceremonies (actual WebAuthn JS API, actual
/// FIDO2 attestation statements other than "none") — would require a browser + authenticator.</item>
/// <item><c>CborReader</c>'s constructor is exercised (it is instantiated on every
/// <c>ParseAttestationObject</c> call) but the class itself has no public members beyond
/// the constructor, so there is nothing further to test.</item>
/// </list>
/// </summary>
public class WebAuthnServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private const string TestRpId = "localhost";
    private const string TestOrigin = "https://localhost";

    private static IConfiguration BuildConfig(string rpId = TestRpId, string rpName = "LagerSystem", string origin = TestOrigin)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebAuthn:RpId"] = rpId,
                ["WebAuthn:RpName"] = rpName,
                ["WebAuthn:Origin"] = origin
            })
            .Build();

    private static (WebAuthnService sut, IDbContextFactory<InventoryDbContext> factory, IAuditService audit) CreateSut(
        string dbName, IConfiguration? config = null, bool withAudit = true)
    {
        var factory = CreateFactory(dbName);
        var audit = Substitute.For<IAuditService>();
        var sut = new WebAuthnService(factory, NullLogger<WebAuthnService>.Instance, config ?? BuildConfig(), withAudit ? audit : null);
        return (sut, factory, audit);
    }

    private static async Task<User> SeedUserAsync(IDbContextFactory<InventoryDbContext> factory, int id = 1, string username = "alice")
    {
        await using var db = factory.CreateDbContext();
        if (!await db.Warehouses.AnyAsync(w => w.Id == 1))
        {
            db.Warehouses.Add(new Warehouse { Id = 1, Name = "WH", Code = "T", IsActive = true });
        }
        var user = new User
        {
            Id = id,
            Username = username,
            Email = $"{username}@test.local",
            DisplayName = username,
            PasswordHash = "x",
            IsActive = true,
            ApprovalStatus = UserApprovalStatus.Approved,
            WarehouseId = 1
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // ---- Base64Url / CBOR / COSE construction helpers ------------------------------

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Builds a WebAuthn "authenticator data" byte blob: rpIdHash(32) + flags(1) +
    /// counter(4) [+ aaguid(16) + credIdLen(2) + credId + publicKeyCbor when attested
    /// credential data is included].
    /// </summary>
    private static byte[] BuildAuthenticatorData(
        bool attestedCredentialIncluded,
        bool userPresent,
        bool userVerified,
        byte[]? rpIdHash = null,
        byte[]? aaguid = null,
        byte[]? credId = null,
        byte[]? publicKeyCbor = null,
        uint counter = 1)
    {
        rpIdHash ??= new byte[32];
        var flags = (byte)((userPresent ? 0x01 : 0) | (userVerified ? 0x04 : 0) | (attestedCredentialIncluded ? 0x40 : 0));
        var counterBytes = new byte[] { (byte)(counter >> 24), (byte)(counter >> 16), (byte)(counter >> 8), (byte)counter };

        using var ms = new MemoryStream();
        ms.Write(rpIdHash);
        ms.WriteByte(flags);
        ms.Write(counterBytes);

        if (attestedCredentialIncluded)
        {
            aaguid ??= Guid.NewGuid().ToByteArray();
            credId ??= RandomNumberGenerator.GetBytes(16);
            ms.Write(aaguid);
            ms.WriteByte((byte)(credId.Length >> 8));
            ms.WriteByte((byte)credId.Length);
            ms.Write(credId);
            if (publicKeyCbor != null)
                ms.Write(publicKeyCbor);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Wraps authData in a minimal CBOR blob that <c>ExtractAuthDataFromCbor</c> can find:
    /// a CBOR text-string key "authData" (0x68 + ascii) followed by a byte-string header
    /// whose length-encoding matches the real CBOR spec (adapts short/8-bit/16-bit length
    /// forms depending on payload size, exercising all three of the production parser's branches).
    /// </summary>
    private static byte[] BuildAttestationObject(byte[] authData)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x68);
        ms.Write(Encoding.ASCII.GetBytes("authData"));

        if (authData.Length < 24)
        {
            ms.WriteByte((byte)(0x40 | authData.Length));
        }
        else if (authData.Length < 256)
        {
            ms.WriteByte(0x58);
            ms.WriteByte((byte)authData.Length);
        }
        else
        {
            ms.WriteByte(0x59);
            ms.WriteByte((byte)(authData.Length >> 8));
            ms.WriteByte((byte)authData.Length);
        }

        ms.Write(authData);
        return ms.ToArray();
    }

    /// <summary>Builds a CBOR EC2/P-256 COSE key: A5 01 02 03 26 20 01 21 58 20 &lt;X32&gt; 22 58 20 &lt;Y32&gt;.</summary>
    private static byte[] BuildCoseKey(byte[] x, byte[] y)
    {
        using var ms = new MemoryStream();
        ms.Write([0xA5, 0x01, 0x02, 0x03, 0x26, 0x20, 0x01, 0x21, 0x58, 0x20]);
        ms.Write(x);
        ms.Write([0x22, 0x58, 0x20]);
        ms.Write(y);
        return ms.ToArray();
    }

    private static string BuildRegistrationCredentialJson(
        string credentialId, string clientDataJson, byte[] attestationObject, string[]? transports = null, string? deviceName = null)
        => JsonSerializer.Serialize(new
        {
            id = credentialId,
            type = "public-key",
            response = new
            {
                clientDataJSON = Base64UrlEncode(Encoding.UTF8.GetBytes(clientDataJson)),
                attestationObject = Base64UrlEncode(attestationObject),
                transports
            },
            deviceName
        });

    private static string BuildAuthenticationCredentialJson(
        string credentialId, string clientDataJson, byte[] authenticatorData, byte[] signature)
        => JsonSerializer.Serialize(new
        {
            id = credentialId,
            response = new
            {
                clientDataJSON = Base64UrlEncode(Encoding.UTF8.GetBytes(clientDataJson)),
                authenticatorData = Base64UrlEncode(authenticatorData),
                signature = Base64UrlEncode(signature)
            }
        });

    private static string BuildClientDataJson(string type, string challenge, string origin = TestOrigin)
        => $$"""{"type":"{{type}}","challenge":"{{challenge}}","origin":"{{origin}}"}""";

    // ---- GenerateRegistrationOptionsAsync --------------------------------------------

    [Fact]
    public async Task GenerateRegistrationOptionsAsync_WithUnknownUser_ReturnsFailure()
    {
        var (sut, _, _) = CreateSut(nameof(GenerateRegistrationOptionsAsync_WithUnknownUser_ReturnsFailure));

        var result = await sut.GenerateRegistrationOptionsAsync(999, "My Key");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Benutzer nicht gefunden");
    }

    [Fact]
    public async Task GenerateRegistrationOptionsAsync_WithNoExistingPasskeys_ReturnsEmptyExcludeList()
    {
        var (sut, factory, _) = CreateSut(nameof(GenerateRegistrationOptionsAsync_WithNoExistingPasskeys_ReturnsEmptyExcludeList));
        var user = await SeedUserAsync(factory);

        var result = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");

        result.Success.Should().BeTrue();
        result.ExcludeCredentials.Should().BeEmpty();
        result.RpId.Should().Be(TestRpId);
        result.UserName.Should().Be("alice");
        result.Timeout.Should().Be(300000);
        result.Attestation.Should().Be("none");

        await using var verify = factory.CreateDbContext();
        (await verify.WebAuthnChallenges.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GenerateRegistrationOptionsAsync_WithExistingPasskeyWithTransports_SplitsTransportsInExcludeList()
    {
        var (sut, factory, _) = CreateSut(nameof(GenerateRegistrationOptionsAsync_WithExistingPasskeyWithTransports_SplitsTransportsInExcludeList));
        var user = await SeedUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey
            {
                UserId = user.Id,
                CredentialId = "cred-1",
                PublicKey = "pk",
                IsActive = true,
                Transports = "usb,nfc"
            });
            await db.SaveChangesAsync();
        }

        var result = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");

        result.ExcludeCredentials.Should().ContainSingle();
        result.ExcludeCredentials[0].Id.Should().Be("cred-1");
        result.ExcludeCredentials[0].Transports.Should().BeEquivalentTo(["usb", "nfc"]);
    }

    [Fact]
    public async Task GenerateRegistrationOptionsAsync_WithExistingPasskeyWithoutTransports_UsesDefaultTransportList()
    {
        var (sut, factory, _) = CreateSut(nameof(GenerateRegistrationOptionsAsync_WithExistingPasskeyWithoutTransports_UsesDefaultTransportList));
        var user = await SeedUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = true, Transports = null });
            await db.SaveChangesAsync();
        }

        var result = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");

        result.ExcludeCredentials[0].Transports.Should().BeEquivalentTo(["internal", "usb", "ble", "nfc"]);
    }

    [Fact]
    public async Task GenerateRegistrationOptionsAsync_IgnoresInactivePasskeys()
    {
        var (sut, factory, _) = CreateSut(nameof(GenerateRegistrationOptionsAsync_IgnoresInactivePasskeys));
        var user = await SeedUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = false });
            await db.SaveChangesAsync();
        }

        var result = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");

        result.ExcludeCredentials.Should().BeEmpty();
    }

    // ---- VerifyRegistrationAsync -----------------------------------------------------

    [Fact]
    public async Task VerifyRegistrationAsync_WithEmptyCredentialJson_ReturnsFailure()
    {
        var (sut, _, _) = CreateSut(nameof(VerifyRegistrationAsync_WithEmptyCredentialJson_ReturnsFailure));

        var result = await sut.VerifyRegistrationAsync(1, "   ", "session-1");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Keine Credential-Daten erhalten");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithWrongSessionId_ReturnsChallengeInvalid()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithWrongSessionId_ReturnsChallengeInvalid));
        var user = await SeedUserAsync(factory);
        await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");

        var result = await sut.VerifyRegistrationAsync(user.Id, "{}", "wrong-session-id");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Challenge ungültig oder abgelaufen");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithWrongUserId_ReturnsChallengeInvalid()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithWrongUserId_ReturnsChallengeInvalid));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");

        var result = await sut.VerifyRegistrationAsync(user.Id + 1, "{}", options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Challenge ungültig oder abgelaufen");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithAlreadyUsedChallenge_ReturnsChallengeInvalid()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithAlreadyUsedChallenge_ReturnsChallengeInvalid));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");
        await using (var db = factory.CreateDbContext())
        {
            var challenge = await db.WebAuthnChallenges.SingleAsync();
            challenge.IsUsed = true;
            await db.SaveChangesAsync();
        }

        var result = await sut.VerifyRegistrationAsync(user.Id, "{}", options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Challenge ungültig oder abgelaufen");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithExpiredChallenge_ReturnsChallengeInvalid()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithExpiredChallenge_ReturnsChallengeInvalid));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");
        await using (var db = factory.CreateDbContext())
        {
            var challenge = await db.WebAuthnChallenges.SingleAsync();
            challenge.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var result = await sut.VerifyRegistrationAsync(user.Id, "{}", options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Challenge ungültig oder abgelaufen");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithWrongOperationType_ReturnsChallengeInvalid()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithWrongOperationType_ReturnsChallengeInvalid));
        var user = await SeedUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.WebAuthnChallenges.Add(new WebAuthnChallenge
            {
                UserId = user.Id,
                Challenge = "c",
                OperationType = "authenticate",
                SessionId = "sid",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            });
            await db.SaveChangesAsync();
        }

        var result = await sut.VerifyRegistrationAsync(user.Id, "{}", "sid");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Challenge ungültig oder abgelaufen");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithMalformedJson_ReturnsInternalError()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithMalformedJson_ReturnsInternalError));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");

        var result = await sut.VerifyRegistrationAsync(user.Id, "{ not valid json", options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Interner Fehler bei der Registrierung");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithNullCredentialLiteral_ReturnsInvalidCredentialData()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithNullCredentialLiteral_ReturnsInvalidCredentialData));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");

        var result = await sut.VerifyRegistrationAsync(user.Id, "null", options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Ungültige Credential-Daten");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithNullClientData_ReturnsInvalidClientData()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithNullClientData_ReturnsInvalidClientData));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");
        var credentialJson = BuildRegistrationCredentialJson("cred-1", "null", []);

        var result = await sut.VerifyRegistrationAsync(user.Id, credentialJson, options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Ungültige Client-Daten");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithChallengeMismatch_ReturnsFailure()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithChallengeMismatch_ReturnsFailure));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");
        var clientDataJson = BuildClientDataJson("webauthn.create", "wrong-challenge-value");
        var credentialJson = BuildRegistrationCredentialJson("cred-1", clientDataJson, []);

        var result = await sut.VerifyRegistrationAsync(user.Id, credentialJson, options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Challenge stimmt nicht überein");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithWrongType_ReturnsFailure()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithWrongType_ReturnsFailure));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");
        var clientDataJson = BuildClientDataJson("webauthn.get", options.Challenge);
        var credentialJson = BuildRegistrationCredentialJson("cred-1", clientDataJson, []);

        var result = await sut.VerifyRegistrationAsync(user.Id, credentialJson, options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Ungültiger Operation-Typ");
    }

    /// <summary>
    /// Suspected bug: origin verification only logs a warning
    /// (<c>"WebAuthn registration failed: Origin mismatch..."</c>) and does not return a
    /// failure result, so a credential presented with a completely unrelated origin is
    /// still registered. This test documents the current (permissive) behaviour rather
    /// than asserting a fix.
    /// </summary>
    [Fact]
    public async Task VerifyRegistrationAsync_WithMismatchedOrigin_StillSucceeds_DocumentingPermissiveBehaviour()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithMismatchedOrigin_StillSucceeds_DocumentingPermissiveBehaviour));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = ecdsa.ExportParameters(false);
        var coseKey = BuildCoseKey(pub.Q.X!, pub.Q.Y!);
        var credId = RandomNumberGenerator.GetBytes(16);
        var authData = BuildAuthenticatorData(true, true, true, credId: credId, publicKeyCbor: coseKey);
        var attestationObject = BuildAttestationObject(authData);

        var clientDataJson = BuildClientDataJson("webauthn.create", options.Challenge, origin: "https://evil.example");
        var credentialJson = BuildRegistrationCredentialJson(Base64UrlEncode(credId), clientDataJson, attestationObject);

        var result = await sut.VerifyRegistrationAsync(user.Id, credentialJson, options.SessionId);

        result.Success.Should().BeTrue("origin mismatch is only logged, not enforced, in the current implementation");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithUnparsableAttestationObject_ReturnsFailure()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithUnparsableAttestationObject_ReturnsFailure));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");
        var clientDataJson = BuildClientDataJson("webauthn.create", options.Challenge);
        // No "authData" marker anywhere in this payload.
        var garbage = Encoding.ASCII.GetBytes("this attestation object has no cbor marker in it at all, just padding");
        var credentialJson = BuildRegistrationCredentialJson("cred-1", clientDataJson, garbage);

        var result = await sut.VerifyRegistrationAsync(user.Id, credentialJson, options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Fehler beim Parsen der Attestation");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithoutAttestedCredentialData_ReturnsFailure()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithoutAttestedCredentialData_ReturnsFailure));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");
        var clientDataJson = BuildClientDataJson("webauthn.create", options.Challenge);
        // authData is well-formed but the "attested credential data included" flag is off,
        // so no public key can be extracted.
        var authData = BuildAuthenticatorData(attestedCredentialIncluded: false, userPresent: true, userVerified: true);
        var attestationObject = BuildAttestationObject(authData);
        var credentialJson = BuildRegistrationCredentialJson("cred-1", clientDataJson, attestationObject);

        var result = await sut.VerifyRegistrationAsync(user.Id, credentialJson, options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Fehler beim Parsen der Attestation");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithTooShortAuthenticatorData_ReturnsFailure()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithTooShortAuthenticatorData_ReturnsFailure));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");
        var clientDataJson = BuildClientDataJson("webauthn.create", options.Challenge);
        // Fewer than 37 bytes: ParseAuthenticatorData short-circuits to a default (all-false) result.
        var authData = new byte[20];
        var attestationObject = BuildAttestationObject(authData);
        var credentialJson = BuildRegistrationCredentialJson("cred-1", clientDataJson, attestationObject);

        var result = await sut.VerifyRegistrationAsync(user.Id, credentialJson, options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Fehler beim Parsen der Attestation");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithValidCredential_StoresPasskeyMarksChallengeUsedAndAudits()
    {
        var (sut, factory, audit) = CreateSut(nameof(VerifyRegistrationAsync_WithValidCredential_StoresPasskeyMarksChallengeUsedAndAudits));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = ecdsa.ExportParameters(false);
        var coseKey = BuildCoseKey(pub.Q.X!, pub.Q.Y!);
        var credId = RandomNumberGenerator.GetBytes(16);
        var authData = BuildAuthenticatorData(true, true, true, credId: credId, publicKeyCbor: coseKey);
        var attestationObject = BuildAttestationObject(authData);

        var clientDataJson = BuildClientDataJson("webauthn.create", options.Challenge);
        var credentialIdB64 = Base64UrlEncode(credId);
        var credentialJson = BuildRegistrationCredentialJson(
            credentialIdB64, clientDataJson, attestationObject, transports: ["usb", "internal"], deviceName: "My YubiKey");

        var result = await sut.VerifyRegistrationAsync(user.Id, credentialJson, options.SessionId);

        result.Success.Should().BeTrue($"error was '{result.Error}'");
        result.DeviceName.Should().Be("My YubiKey");
        result.PasskeyId.Should().BePositive();

        await using var verify = factory.CreateDbContext();
        var passkey = await verify.UserPasskeys.SingleAsync();
        passkey.UserId.Should().Be(user.Id);
        passkey.CredentialId.Should().Be(credentialIdB64);
        passkey.UserVerified.Should().BeTrue();
        passkey.Transports.Should().Be("usb,internal");
        passkey.AaGuid.Should().NotBeNullOrWhiteSpace();
        passkey.IsActive.Should().BeTrue();

        var challengeRow = await verify.WebAuthnChallenges.SingleAsync();
        challengeRow.IsUsed.Should().BeTrue();

        await audit.Received(1).LogAsync("PASSKEY_REGISTERED", "UserPasskey", passkey.Id, Arg.Any<object?>(), AuditSeverity.Info);
    }

    [Fact]
    public async Task VerifyRegistrationAsync_WithoutAuditService_StillSucceeds()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyRegistrationAsync_WithoutAuditService_StillSucceeds), withAudit: false);
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateRegistrationOptionsAsync(user.Id, "My Key");

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = ecdsa.ExportParameters(false);
        var coseKey = BuildCoseKey(pub.Q.X!, pub.Q.Y!);
        var credId = RandomNumberGenerator.GetBytes(16);
        var authData = BuildAuthenticatorData(true, true, true, credId: credId, publicKeyCbor: coseKey);
        var attestationObject = BuildAttestationObject(authData);
        var clientDataJson = BuildClientDataJson("webauthn.create", options.Challenge);
        var credentialJson = BuildRegistrationCredentialJson(Base64UrlEncode(credId), clientDataJson, attestationObject);

        var result = await sut.VerifyRegistrationAsync(user.Id, credentialJson, options.SessionId);

        result.Success.Should().BeTrue();
    }

    // ---- GenerateAuthenticationOptionsAsync ------------------------------------------

    [Fact]
    public async Task GenerateAuthenticationOptionsAsync_WithoutUsername_ReturnsUsernamelessOptions()
    {
        var (sut, factory, _) = CreateSut(nameof(GenerateAuthenticationOptionsAsync_WithoutUsername_ReturnsUsernamelessOptions));

        var result = await sut.GenerateAuthenticationOptionsAsync();

        result.Success.Should().BeTrue();
        result.AllowCredentials.Should().BeNull();
        result.RpId.Should().Be(TestRpId);

        await using var verify = factory.CreateDbContext();
        var challenge = await verify.WebAuthnChallenges.SingleAsync();
        challenge.UserId.Should().BeNull();
        challenge.OperationType.Should().Be("authenticate");
    }

    [Fact]
    public async Task GenerateAuthenticationOptionsAsync_WithUnknownUsername_ReturnsUsernamelessOptions()
    {
        var (sut, _, _) = CreateSut(nameof(GenerateAuthenticationOptionsAsync_WithUnknownUsername_ReturnsUsernamelessOptions));

        var result = await sut.GenerateAuthenticationOptionsAsync("ghost");

        result.Success.Should().BeTrue();
        result.AllowCredentials.Should().BeNull();
    }

    [Fact]
    public async Task GenerateAuthenticationOptionsAsync_WithKnownUsernameButNoPasskeys_ReturnsNullAllowCredentials()
    {
        var (sut, factory, _) = CreateSut(nameof(GenerateAuthenticationOptionsAsync_WithKnownUsernameButNoPasskeys_ReturnsNullAllowCredentials));
        var user = await SeedUserAsync(factory);

        var result = await sut.GenerateAuthenticationOptionsAsync(user.Username);

        result.AllowCredentials.Should().BeNull();
    }

    [Fact]
    public async Task GenerateAuthenticationOptionsAsync_WithKnownUsernameAndPasskeys_PopulatesAllowCredentials()
    {
        var (sut, factory, _) = CreateSut(nameof(GenerateAuthenticationOptionsAsync_WithKnownUsernameAndPasskeys_PopulatesAllowCredentials));
        var user = await SeedUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = true, Transports = "nfc" });
            await db.SaveChangesAsync();
        }

        var result = await sut.GenerateAuthenticationOptionsAsync(user.Username);

        result.AllowCredentials.Should().ContainSingle();
        result.AllowCredentials![0].Id.Should().Be("cred-1");
        result.AllowCredentials[0].Transports.Should().BeEquivalentTo(["nfc"]);

        await using var verify = factory.CreateDbContext();
        var challenge = await verify.WebAuthnChallenges.SingleAsync();
        challenge.UserId.Should().Be(user.Id);
    }

    // ---- VerifyAuthenticationAsync ----------------------------------------------------

    [Fact]
    public async Task VerifyAuthenticationAsync_WithEmptyCredentialJson_ReturnsFailure()
    {
        var (sut, _, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithEmptyCredentialJson_ReturnsFailure));

        var result = await sut.VerifyAuthenticationAsync("  ", "session-1");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Keine Credential-Daten erhalten");
    }

    [Fact]
    public async Task VerifyAuthenticationAsync_WithMalformedJson_ReturnsInternalError()
    {
        var (sut, _, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithMalformedJson_ReturnsInternalError));

        var result = await sut.VerifyAuthenticationAsync("{ not valid", "session-1");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Interner Fehler bei der Authentifizierung");
    }

    [Fact]
    public async Task VerifyAuthenticationAsync_WithNullCredentialLiteral_ReturnsInvalidCredentialData()
    {
        var (sut, _, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithNullCredentialLiteral_ReturnsInvalidCredentialData));

        var result = await sut.VerifyAuthenticationAsync("null", "session-1");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Ungültige Credential-Daten");
    }

    [Fact]
    public async Task VerifyAuthenticationAsync_WithUnknownCredentialId_ReturnsPasskeyNotFound()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithUnknownCredentialId_ReturnsPasskeyNotFound));
        var user = await SeedUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = user.Id, CredentialId = "known-cred", PublicKey = "pk", IsActive = true });
            await db.SaveChangesAsync();
        }
        var credentialJson = BuildAuthenticationCredentialJson("unknown-cred", "{}", [], []);

        var result = await sut.VerifyAuthenticationAsync(credentialJson, "session-1");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Passkey nicht gefunden");
    }

    /// <summary>
    /// The <c>passkey.User == null</c> defensive check in <c>VerifyAuthenticationAsync</c>
    /// is effectively unreachable in practice: <c>UserPasskey.UserId</c> is a non-nullable
    /// <c>int</c>, so EF Core treats the FK as required and <c>Include(p =&gt; p.User)</c>
    /// behaves like an inner join even against the InMemory provider (see the same caveat
    /// documented for <c>User.Warehouse</c> in <c>AuthServiceLoginTests</c>) - an orphaned
    /// passkey row is filtered out by the query itself rather than resolving to a null
    /// navigation. A real relational DB would additionally enforce the FK constraint. This
    /// test documents that the orphan row surfaces as "Passkey nicht gefunden" (filtered
    /// before the null-User check is ever reached), not as "Benutzer nicht gefunden".
    /// </summary>
    [Fact]
    public async Task VerifyAuthenticationAsync_WithOrphanedPasskey_IsFilteredByRequiredInclude_ReturnsPasskeyNotFound()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithOrphanedPasskey_IsFilteredByRequiredInclude_ReturnsPasskeyNotFound));
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = 999, CredentialId = "orphan-cred", PublicKey = "pk", IsActive = true });
            await db.SaveChangesAsync();
        }
        var credentialJson = BuildAuthenticationCredentialJson("orphan-cred", "{}", [], []);

        var result = await sut.VerifyAuthenticationAsync(credentialJson, "session-1");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Passkey nicht gefunden");
    }

    [Fact]
    public async Task VerifyAuthenticationAsync_WithNoMatchingChallenge_ReturnsChallengeInvalid()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithNoMatchingChallenge_ReturnsChallengeInvalid));
        var user = await SeedUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = true });
            // A challenge that exists but for a different (unrelated) session, to also
            // exercise the "available challenges" diagnostic-logging branch.
            db.WebAuthnChallenges.Add(new WebAuthnChallenge
            {
                Challenge = "c",
                OperationType = "authenticate",
                SessionId = "other-session",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            });
            await db.SaveChangesAsync();
        }
        var credentialJson = BuildAuthenticationCredentialJson("cred-1", "{}", [], []);

        var result = await sut.VerifyAuthenticationAsync(credentialJson, "missing-session");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Challenge ungültig oder abgelaufen");
    }

    [Fact]
    public async Task VerifyAuthenticationAsync_WithNullClientData_ReturnsInvalidClientData()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithNullClientData_ReturnsInvalidClientData));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateAuthenticationOptionsAsync(user.Username);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = true });
            await db.SaveChangesAsync();
        }
        var credentialJson = BuildAuthenticationCredentialJson("cred-1", "null", [], []);

        var result = await sut.VerifyAuthenticationAsync(credentialJson, options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Ungültige Client-Daten");
    }

    [Fact]
    public async Task VerifyAuthenticationAsync_WithChallengeMismatch_ReturnsFailure()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithChallengeMismatch_ReturnsFailure));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateAuthenticationOptionsAsync(user.Username);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = true });
            await db.SaveChangesAsync();
        }
        var clientDataJson = BuildClientDataJson("webauthn.get", "wrong-challenge");
        var credentialJson = BuildAuthenticationCredentialJson("cred-1", clientDataJson, [], []);

        var result = await sut.VerifyAuthenticationAsync(credentialJson, options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Challenge-Validierung fehlgeschlagen");
    }

    [Fact]
    public async Task VerifyAuthenticationAsync_WithWrongType_ReturnsFailure()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithWrongType_ReturnsFailure));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateAuthenticationOptionsAsync(user.Username);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = true });
            await db.SaveChangesAsync();
        }
        var clientDataJson = BuildClientDataJson("webauthn.create", options.Challenge);
        var credentialJson = BuildAuthenticationCredentialJson("cred-1", clientDataJson, [], []);

        var result = await sut.VerifyAuthenticationAsync(credentialJson, options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Ungültiger Operation-Typ");
    }

    [Fact]
    public async Task VerifyAuthenticationAsync_WithRpIdHashMismatch_ReturnsFailure()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithRpIdHashMismatch_ReturnsFailure));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateAuthenticationOptionsAsync(user.Username);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = true });
            await db.SaveChangesAsync();
        }
        var clientDataJson = BuildClientDataJson("webauthn.get", options.Challenge);
        // Wrong rpIdHash (all zero bytes instead of SHA256("localhost")).
        var authData = BuildAuthenticatorData(false, true, true, rpIdHash: new byte[32]);
        var credentialJson = BuildAuthenticationCredentialJson("cred-1", clientDataJson, authData, []);

        var result = await sut.VerifyAuthenticationAsync(credentialJson, options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("RP ID Hash stimmt nicht überein");
    }

    [Fact]
    public async Task VerifyAuthenticationAsync_WithUserPresentFlagFalse_ReturnsFailure()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithUserPresentFlagFalse_ReturnsFailure));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateAuthenticationOptionsAsync(user.Username);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = true });
            await db.SaveChangesAsync();
        }
        var clientDataJson = BuildClientDataJson("webauthn.get", options.Challenge);
        var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(TestRpId));
        var authData = BuildAuthenticatorData(false, userPresent: false, userVerified: false, rpIdHash: rpIdHash);
        var credentialJson = BuildAuthenticationCredentialJson("cred-1", clientDataJson, authData, []);

        var result = await sut.VerifyAuthenticationAsync(credentialJson, options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("User Presence nicht bestätigt");
    }

    [Fact]
    public async Task VerifyAuthenticationAsync_WithInvalidSignature_ReturnsFailure()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithInvalidSignature_ReturnsFailure));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateAuthenticationOptionsAsync(user.Username);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = ecdsa.ExportParameters(false);
        var coseKey = BuildCoseKey(pub.Q.X!, pub.Q.Y!);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey
            {
                UserId = user.Id,
                CredentialId = "cred-1",
                PublicKey = Convert.ToBase64String(coseKey),
                IsActive = true,
                SignatureCounter = 0
            });
            await db.SaveChangesAsync();
        }

        var clientDataJson = BuildClientDataJson("webauthn.get", options.Challenge);
        var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(TestRpId));
        var authData = BuildAuthenticatorData(false, true, true, rpIdHash: rpIdHash);
        // Random 64-byte "signature" that does not correspond to the key at all.
        var badSignature = RandomNumberGenerator.GetBytes(64);
        var credentialJson = BuildAuthenticationCredentialJson("cred-1", clientDataJson, authData, badSignature);

        var result = await sut.VerifyAuthenticationAsync(credentialJson, options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Signatur ungültig");
    }

    [Fact]
    public async Task VerifyAuthenticationAsync_WithInvalidBase64AuthenticatorData_ReturnsInternalError()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithInvalidBase64AuthenticatorData_ReturnsInternalError));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateAuthenticationOptionsAsync(user.Username);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = true });
            await db.SaveChangesAsync();
        }
        var clientDataJson = BuildClientDataJson("webauthn.get", options.Challenge);
        var credentialJson = JsonSerializer.Serialize(new
        {
            id = "cred-1",
            response = new
            {
                clientDataJSON = Base64UrlEncode(Encoding.UTF8.GetBytes(clientDataJson)),
                authenticatorData = "!!!not-base64-at-all!!!",
                signature = Base64UrlEncode([])
            }
        });

        var result = await sut.VerifyAuthenticationAsync(credentialJson, options.SessionId);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Interner Fehler bei der Authentifizierung");
    }

    [Fact]
    public async Task VerifyAuthenticationAsync_WithValidSignature_SucceedsUpdatesPasskeyAndAudits()
    {
        var (sut, factory, audit) = CreateSut(nameof(VerifyAuthenticationAsync_WithValidSignature_SucceedsUpdatesPasskeyAndAudits));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateAuthenticationOptionsAsync(user.Username);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = ecdsa.ExportParameters(false);
        var coseKey = BuildCoseKey(pub.Q.X!, pub.Q.Y!);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey
            {
                UserId = user.Id,
                CredentialId = "cred-1",
                PublicKey = Convert.ToBase64String(coseKey),
                IsActive = true,
                SignatureCounter = 0,
                UseCount = 3
            });
            await db.SaveChangesAsync();
        }

        var clientDataJson = BuildClientDataJson("webauthn.get", options.Challenge);
        var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(TestRpId));
        var authData = BuildAuthenticatorData(false, true, true, rpIdHash: rpIdHash, counter: 7);

        var clientDataHash = SHA256.HashData(Encoding.UTF8.GetBytes(clientDataJson));
        var signedData = authData.Concat(clientDataHash).ToArray();
        var signature = ecdsa.SignData(signedData, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var credentialJson = BuildAuthenticationCredentialJson("cred-1", clientDataJson, authData, signature);

        var result = await sut.VerifyAuthenticationAsync(credentialJson, options.SessionId);

        result.Success.Should().BeTrue($"error was '{result.Error}'");
        result.UserId.Should().Be(user.Id);
        result.Username.Should().Be(user.Username);
        result.User.Should().NotBeNull();

        await using var verify = factory.CreateDbContext();
        var passkey = await verify.UserPasskeys.SingleAsync();
        passkey.SignatureCounter.Should().Be(7u);
        passkey.UseCount.Should().Be(4);
        passkey.LastUsedAt.Should().NotBeNull();

        var challenge = await verify.WebAuthnChallenges.SingleAsync();
        challenge.IsUsed.Should().BeTrue();

        await audit.Received(1).LogAsync("PASSKEY_LOGIN", "User", passkey.UserId, Arg.Any<object?>(), AuditSeverity.Info);
    }

    [Fact]
    public async Task VerifyAuthenticationAsync_WithDerEncodedSignature_Succeeds()
    {
        var (sut, factory, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithDerEncodedSignature_Succeeds));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateAuthenticationOptionsAsync(user.Username);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = ecdsa.ExportParameters(false);
        var coseKey = BuildCoseKey(pub.Q.X!, pub.Q.Y!);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = Convert.ToBase64String(coseKey), IsActive = true });
            await db.SaveChangesAsync();
        }

        var clientDataJson = BuildClientDataJson("webauthn.get", options.Challenge);
        var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(TestRpId));
        var authData = BuildAuthenticatorData(false, true, true, rpIdHash: rpIdHash, counter: 1);
        var clientDataHash = SHA256.HashData(Encoding.UTF8.GetBytes(clientDataJson));
        var signedData = authData.Concat(clientDataHash).ToArray();
        // DER-encoded signature exercises the "DER to raw conversion" branch of VerifySignature.
        var derSignature = ecdsa.SignData(signedData, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        var credentialJson = BuildAuthenticationCredentialJson("cred-1", clientDataJson, authData, derSignature);

        var result = await sut.VerifyAuthenticationAsync(credentialJson, options.SessionId);

        result.Success.Should().BeTrue($"error was '{result.Error}'");
    }

    [Fact]
    public async Task VerifyAuthenticationAsync_WithSignatureCounterRegression_StillSucceedsButLogsWarning()
    {
        // Suspected weakness: a signature counter that goes backwards (a classic cloned
        // -authenticator indicator) is only logged as a warning, not rejected. The counter
        // is unconditionally overwritten afterwards, so a rollback is not actually detected
        // as a hard failure. This test documents the current (permissive) behaviour.
        var (sut, factory, _) = CreateSut(nameof(VerifyAuthenticationAsync_WithSignatureCounterRegression_StillSucceedsButLogsWarning));
        var user = await SeedUserAsync(factory);
        var options = await sut.GenerateAuthenticationOptionsAsync(user.Username);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = ecdsa.ExportParameters(false);
        var coseKey = BuildCoseKey(pub.Q.X!, pub.Q.Y!);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey
            {
                UserId = user.Id,
                CredentialId = "cred-1",
                PublicKey = Convert.ToBase64String(coseKey),
                IsActive = true,
                SignatureCounter = 50
            });
            await db.SaveChangesAsync();
        }

        var clientDataJson = BuildClientDataJson("webauthn.get", options.Challenge);
        var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(TestRpId));
        // New counter (1) is lower than the stored counter (50).
        var authData = BuildAuthenticatorData(false, true, true, rpIdHash: rpIdHash, counter: 1);
        var clientDataHash = SHA256.HashData(Encoding.UTF8.GetBytes(clientDataJson));
        var signedData = authData.Concat(clientDataHash).ToArray();
        var signature = ecdsa.SignData(signedData, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var credentialJson = BuildAuthenticationCredentialJson("cred-1", clientDataJson, authData, signature);

        var result = await sut.VerifyAuthenticationAsync(credentialJson, options.SessionId);

        result.Success.Should().BeTrue("counter regression is currently only logged, not enforced");

        await using var verify = factory.CreateDbContext();
        (await verify.UserPasskeys.SingleAsync()).SignatureCounter.Should().Be(1u, "the counter is overwritten unconditionally");
    }

    // ---- GetUserPasskeysAsync / DeletePasskeyAsync / RenamePasskeyAsync / HasPasskeysAsync ----

    [Fact]
    public async Task GetUserPasskeysAsync_ReturnsOnlyActiveOwnPasskeysOrderedByRecency()
    {
        var (sut, factory, _) = CreateSut(nameof(GetUserPasskeysAsync_ReturnsOnlyActiveOwnPasskeysOrderedByRecency));
        var user = await SeedUserAsync(factory);
        var other = await SeedUserAsync(factory, id: 2, username: "bob");
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.AddRange(
                new UserPasskey { UserId = user.Id, CredentialId = "old", PublicKey = "pk", IsActive = true, CreatedAt = DateTime.UtcNow.AddDays(-2), LastUsedAt = null },
                new UserPasskey { UserId = user.Id, CredentialId = "recent", PublicKey = "pk", IsActive = true, CreatedAt = DateTime.UtcNow.AddDays(-5), LastUsedAt = DateTime.UtcNow.AddHours(-1) },
                new UserPasskey { UserId = user.Id, CredentialId = "inactive", PublicKey = "pk", IsActive = false },
                new UserPasskey { UserId = other.Id, CredentialId = "not-mine", PublicKey = "pk", IsActive = true });
            await db.SaveChangesAsync();
        }

        var result = await sut.GetUserPasskeysAsync(user.Id);

        result.Select(p => p.CredentialId).Should().Equal("recent", "old");
    }

    [Fact]
    public async Task DeletePasskeyAsync_WithUnknownPasskey_ReturnsFalse()
    {
        var (sut, factory, audit) = CreateSut(nameof(DeletePasskeyAsync_WithUnknownPasskey_ReturnsFalse));
        var user = await SeedUserAsync(factory);

        var result = await sut.DeletePasskeyAsync(user.Id, 999);

        result.Should().BeFalse();
        await audit.DidNotReceiveWithAnyArgs().LogAsync(default!, default!);
    }

    [Fact]
    public async Task DeletePasskeyAsync_WithPasskeyOwnedByAnotherUser_ReturnsFalse()
    {
        var (sut, factory, _) = CreateSut(nameof(DeletePasskeyAsync_WithPasskeyOwnedByAnotherUser_ReturnsFalse));
        var user = await SeedUserAsync(factory);
        var other = await SeedUserAsync(factory, id: 2, username: "bob");
        int passkeyId;
        await using (var db = factory.CreateDbContext())
        {
            var passkey = new UserPasskey { UserId = other.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = true };
            db.UserPasskeys.Add(passkey);
            await db.SaveChangesAsync();
            passkeyId = passkey.Id;
        }

        var result = await sut.DeletePasskeyAsync(user.Id, passkeyId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeletePasskeyAsync_WithOwnPasskey_SoftDeletesAndAudits()
    {
        var (sut, factory, audit) = CreateSut(nameof(DeletePasskeyAsync_WithOwnPasskey_SoftDeletesAndAudits));
        var user = await SeedUserAsync(factory);
        int passkeyId;
        await using (var db = factory.CreateDbContext())
        {
            var passkey = new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = true, DeviceName = "Key A" };
            db.UserPasskeys.Add(passkey);
            await db.SaveChangesAsync();
            passkeyId = passkey.Id;
        }

        var result = await sut.DeletePasskeyAsync(user.Id, passkeyId);

        result.Should().BeTrue();
        await using var verify = factory.CreateDbContext();
        (await verify.UserPasskeys.FindAsync(passkeyId))!.IsActive.Should().BeFalse();
        await audit.Received(1).LogAsync("PASSKEY_DELETED", "UserPasskey", passkeyId, Arg.Any<object?>(), AuditSeverity.Info);
    }

    [Fact]
    public async Task RenamePasskeyAsync_WithUnknownPasskey_ReturnsFalse()
    {
        var (sut, factory, _) = CreateSut(nameof(RenamePasskeyAsync_WithUnknownPasskey_ReturnsFalse));
        var user = await SeedUserAsync(factory);

        var result = await sut.RenamePasskeyAsync(user.Id, 999, "New Name");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RenamePasskeyAsync_WithInactivePasskey_ReturnsFalse()
    {
        var (sut, factory, _) = CreateSut(nameof(RenamePasskeyAsync_WithInactivePasskey_ReturnsFalse));
        var user = await SeedUserAsync(factory);
        int passkeyId;
        await using (var db = factory.CreateDbContext())
        {
            var passkey = new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = false };
            db.UserPasskeys.Add(passkey);
            await db.SaveChangesAsync();
            passkeyId = passkey.Id;
        }

        var result = await sut.RenamePasskeyAsync(user.Id, passkeyId, "New Name");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RenamePasskeyAsync_WithOwnActivePasskey_RenamesAndReturnsTrue()
    {
        var (sut, factory, _) = CreateSut(nameof(RenamePasskeyAsync_WithOwnActivePasskey_RenamesAndReturnsTrue));
        var user = await SeedUserAsync(factory);
        int passkeyId;
        await using (var db = factory.CreateDbContext())
        {
            var passkey = new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = true, DeviceName = "Old" };
            db.UserPasskeys.Add(passkey);
            await db.SaveChangesAsync();
            passkeyId = passkey.Id;
        }

        var result = await sut.RenamePasskeyAsync(user.Id, passkeyId, "New Name");

        result.Should().BeTrue();
        await using var verify = factory.CreateDbContext();
        (await verify.UserPasskeys.FindAsync(passkeyId))!.DeviceName.Should().Be("New Name");
    }

    [Fact]
    public async Task HasPasskeysAsync_WithNoPasskeys_ReturnsFalse()
    {
        var (sut, factory, _) = CreateSut(nameof(HasPasskeysAsync_WithNoPasskeys_ReturnsFalse));
        var user = await SeedUserAsync(factory);

        (await sut.HasPasskeysAsync(user.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task HasPasskeysAsync_WithActivePasskey_ReturnsTrue()
    {
        var (sut, factory, _) = CreateSut(nameof(HasPasskeysAsync_WithActivePasskey_ReturnsTrue));
        var user = await SeedUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = true });
            await db.SaveChangesAsync();
        }

        (await sut.HasPasskeysAsync(user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task HasPasskeysAsync_WithOnlyInactivePasskey_ReturnsFalse()
    {
        var (sut, factory, _) = CreateSut(nameof(HasPasskeysAsync_WithOnlyInactivePasskey_ReturnsFalse));
        var user = await SeedUserAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.UserPasskeys.Add(new UserPasskey { UserId = user.Id, CredentialId = "cred-1", PublicKey = "pk", IsActive = false });
            await db.SaveChangesAsync();
        }

        (await sut.HasPasskeysAsync(user.Id)).Should().BeFalse();
    }

    // ---- CleanupExpiredChallengesAsync -----------------------------------------------

    [Fact]
    public async Task CleanupExpiredChallengesAsync_WithNoExpiredChallenges_DoesNothing()
    {
        var (sut, factory, _) = CreateSut(nameof(CleanupExpiredChallengesAsync_WithNoExpiredChallenges_DoesNothing));
        await using (var db = factory.CreateDbContext())
        {
            db.WebAuthnChallenges.Add(new WebAuthnChallenge { Challenge = "c", SessionId = "s", ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
            await db.SaveChangesAsync();
        }

        await sut.CleanupExpiredChallengesAsync();

        await using var verify = factory.CreateDbContext();
        (await verify.WebAuthnChallenges.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CleanupExpiredChallengesAsync_RemovesExpiredAndUsedChallenges()
    {
        var (sut, factory, _) = CreateSut(nameof(CleanupExpiredChallengesAsync_RemovesExpiredAndUsedChallenges));
        await using (var db = factory.CreateDbContext())
        {
            db.WebAuthnChallenges.AddRange(
                new WebAuthnChallenge { Challenge = "expired", SessionId = "s1", ExpiresAt = DateTime.UtcNow.AddMinutes(-5) },
                new WebAuthnChallenge { Challenge = "used", SessionId = "s2", IsUsed = true, ExpiresAt = DateTime.UtcNow.AddMinutes(5) },
                new WebAuthnChallenge { Challenge = "valid", SessionId = "s3", ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
            await db.SaveChangesAsync();
        }

        await sut.CleanupExpiredChallengesAsync();

        await using var verify = factory.CreateDbContext();
        var remaining = await verify.WebAuthnChallenges.ToListAsync();
        remaining.Should().ContainSingle().Which.Challenge.Should().Be("valid");
    }
}
