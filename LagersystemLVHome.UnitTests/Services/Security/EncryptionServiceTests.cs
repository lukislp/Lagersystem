using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Security;

public class EncryptionServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static EncryptionService CreateSut(IDbContextFactory<InventoryDbContext> factory)
        => new(factory, NullLogger<EncryptionService>.Instance);

    [Fact]
    public async Task Encrypt_Then_Decrypt_RoundtripsPlainText()
    {
        var factory = CreateFactory(nameof(Encrypt_Then_Decrypt_RoundtripsPlainText));
        var sut = CreateSut(factory);

        const string plain = "geheime nachricht mit Ümlauten 🦊";

        var cipher = await sut.Encrypt(plain);
        var decrypted = await sut.Decrypt(cipher);

        cipher.Should().NotBe(plain);
        decrypted.Should().Be(plain);
    }

    [Fact]
    public async Task Encrypt_PersistsKeysInDatabaseOnFirstUse()
    {
        var factory = CreateFactory(nameof(Encrypt_PersistsKeysInDatabaseOnFirstUse));
        var sut = CreateSut(factory);

        await sut.Encrypt("seed");

        await using var db = factory.CreateDbContext();
        (await db.SystemSettings.AnyAsync(s => s.Key == "EncryptionKey")).Should().BeTrue();
        (await db.SystemSettings.AnyAsync(s => s.Key == "EncryptionIV")).Should().BeTrue();
    }

    [Fact]
    public async Task Decrypt_ReusesPersistedKeysAcrossInstances()
    {
        var factory = CreateFactory(nameof(Decrypt_ReusesPersistedKeysAcrossInstances));
        var first = CreateSut(factory);

        var cipher = await first.Encrypt("hello world");

        var second = CreateSut(factory);
        var decrypted = await second.Decrypt(cipher);

        decrypted.Should().Be("hello world");
    }

    [Fact]
    public async Task Encrypt_EmptyInput_ReturnsEmpty()
    {
        var factory = CreateFactory(nameof(Encrypt_EmptyInput_ReturnsEmpty));
        var sut = CreateSut(factory);

        (await sut.Encrypt(string.Empty)).Should().Be(string.Empty);
        (await sut.Decrypt(string.Empty)).Should().Be(string.Empty);
    }

    [Fact]
    public async Task Decrypt_TamperedCipher_Throws()
    {
        var factory = CreateFactory(nameof(Decrypt_TamperedCipher_Throws));
        var sut = CreateSut(factory);

        var cipher = await sut.Encrypt("payload");
        var tamperedBytes = Convert.FromBase64String(cipher);
        // The first 16 bytes are the per-value IV; tamper a byte in the
        // ciphertext body after it so PKCS7 padding validation fails.
        tamperedBytes[^1] ^= 0xFF;
        var tampered = Convert.ToBase64String(tamperedBytes);

        var act = async () => await sut.Decrypt(tampered);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Decrypt_LegacyStaticIvFormat_StillDecrypts()
    {
        var factory = CreateFactory(nameof(Decrypt_LegacyStaticIvFormat_StillDecrypts));
        var sut = CreateSut(factory);

        // Seed keys via a real encryption first, then read the stored
        // key/IV back out and produce ciphertext using the OLD scheme
        // (whole payload under the single static IV, no IV prefix) to
        // simulate data encrypted before the per-value IV fix.
        await sut.Encrypt("seed");

        await using var db = factory.CreateDbContext();
        var keyBase64 = (await db.SystemSettings.SingleAsync(s => s.Key == "EncryptionKey")).Value;
        var ivBase64 = (await db.SystemSettings.SingleAsync(s => s.Key == "EncryptionIV")).Value;

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = Convert.FromBase64String(keyBase64);
        aes.IV = Convert.FromBase64String(ivBase64);
        aes.Mode = System.Security.Cryptography.CipherMode.CBC;
        aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        using (var cs = new System.Security.Cryptography.CryptoStream(ms, encryptor, System.Security.Cryptography.CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write("legacy plaintext");
        }
        var legacyCipher = Convert.ToBase64String(ms.ToArray());

        var decrypted = await sut.Decrypt(legacyCipher);

        decrypted.Should().Be("legacy plaintext");
    }
}
