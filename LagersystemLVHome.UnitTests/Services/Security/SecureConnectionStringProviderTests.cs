using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Security;

/// <summary>
/// Covers <see cref="SecureConnectionStringProvider"/>: reads an AES-256 key + encrypted
/// password from a <c>Pass/</c> directory under the host's content root, decrypts the
/// password, caches it, and splices it into a connection-string template (PostgreSQL
/// <c>Password=</c>, MySQL <c>Pwd=</c>, or appended if the template has neither).
/// </summary>
public class SecureConnectionStringProviderTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "secure-conn-str-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort cleanup */ }
        }
        GC.SuppressFinalize(this);
    }

    private SecureConnectionStringProvider Build(string contentRoot)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(contentRoot);
        return new SecureConnectionStringProvider(env, NullLogger<SecureConnectionStringProvider>.Instance);
    }

    /// <summary>
    /// Writes real AES-256/CBC/PKCS7-encrypted secret files matching the exact layout
    /// <see cref="SecureConnectionStringProvider"/> expects: <c>encryption.key</c> holds a
    /// base64 32-byte key, and <c>db.password.enc</c> holds base64(IV(16 bytes) + ciphertext).
    /// </summary>
    private static void WriteEncryptedSecrets(string contentRoot, string password, byte[]? keyOverride = null)
    {
        var passDir = Path.Combine(contentRoot, "Pass");
        Directory.CreateDirectory(passDir);

        var key = keyOverride ?? RandomNumberGenerator.GetBytes(32);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(password);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var combined = new byte[aes.IV.Length + cipherBytes.Length];
        Array.Copy(aes.IV, combined, aes.IV.Length);
        Array.Copy(cipherBytes, 0, combined, aes.IV.Length, cipherBytes.Length);

        File.WriteAllText(Path.Combine(passDir, "encryption.key"), Convert.ToBase64String(key));
        File.WriteAllText(Path.Combine(passDir, "db.password.enc"), Convert.ToBase64String(combined));
    }

    // ---- HasSecureSecrets -----------------------------------------------------------------

    [Fact]
    public void HasSecureSecrets_NoPassDirectory_ReturnsFalse()
    {
        var sut = Build(CreateTempDir());

        sut.HasSecureSecrets().Should().BeFalse();
    }

    [Fact]
    public void HasSecureSecrets_OnlyKeyFilePresent_ReturnsFalse()
    {
        var root = CreateTempDir();
        var passDir = Path.Combine(root, "Pass");
        Directory.CreateDirectory(passDir);
        File.WriteAllText(Path.Combine(passDir, "encryption.key"), "abc");
        var sut = Build(root);

        sut.HasSecureSecrets().Should().BeFalse();
    }

    [Fact]
    public void HasSecureSecrets_BothFilesPresent_ReturnsTrue()
    {
        var root = CreateTempDir();
        WriteEncryptedSecrets(root, "irrelevant");
        var sut = Build(root);

        sut.HasSecureSecrets().Should().BeTrue();
    }

    // ---- GetSecureConnectionString: missing secrets fall back to template unchanged -------

    [Fact]
    public void GetSecureConnectionString_NoSecrets_ReturnsTemplateUnchanged()
    {
        var sut = Build(CreateTempDir());
        const string template = "Host=localhost;Password=PLACEHOLDER;Database=db";

        var result = sut.GetSecureConnectionString(template);

        result.Should().Be(template, "without encrypted secrets the provider must fall back to the template as-is, not crash or silently corrupt it");
    }

    // ---- GetSecureConnectionString: password placeholder replacement ----------------------

    [Fact]
    public void GetSecureConnectionString_PostgresStylePasswordField_IsReplacedWithDecryptedPassword()
    {
        var root = CreateTempDir();
        WriteEncryptedSecrets(root, "s3cr3t!Pass");
        var sut = Build(root);

        var result = sut.GetSecureConnectionString("Host=localhost;Password=PLACEHOLDER;Database=db;Username=app");

        result.Should().Be("Host=localhost;Password=s3cr3t!Pass;Database=db;Username=app");
    }

    [Fact]
    public void GetSecureConnectionString_MySqlStylePwdField_IsReplacedWithDecryptedPassword()
    {
        var root = CreateTempDir();
        WriteEncryptedSecrets(root, "mysqlPass1");
        var sut = Build(root);

        var result = sut.GetSecureConnectionString("Server=localhost;Pwd=PLACEHOLDER;Database=db");

        result.Should().Be("Server=localhost;Pwd=mysqlPass1;Database=db");
    }

    [Fact]
    public void GetSecureConnectionString_NoPasswordFieldInTemplate_AppendsOne()
    {
        var root = CreateTempDir();
        WriteEncryptedSecrets(root, "appendedPass");
        var sut = Build(root);

        var result = sut.GetSecureConnectionString("Host=localhost;Database=db");

        result.Should().Be("Host=localhost;Database=db;Password=appendedPass");
    }

    [Fact]
    public void GetSecureConnectionString_CalledTwice_ReusesCachedDecryptedPassword()
    {
        var root = CreateTempDir();
        WriteEncryptedSecrets(root, "cachedPass");
        var sut = Build(root);

        var first = sut.GetSecureConnectionString("Password=PLACEHOLDER;Host=a");
        // Corrupt the secret files after first decryption - if the provider re-reads them
        // on the second call, it should throw; if it correctly cached the password, the
        // second call must still succeed with the same value.
        File.WriteAllText(Path.Combine(root, "Pass", "db.password.enc"), "corrupted-not-base64!!");

        var second = sut.GetSecureConnectionString("Password=PLACEHOLDER;Host=b");

        second.Should().Be(first.Replace("Host=a", "Host=b"), "the decrypted password must be cached, not re-read/re-decrypted on every call");
    }

    // ---- Decryption failure paths ----------------------------------------------------------

    [Fact]
    public void GetSecureConnectionString_WrongKeyLength_ThrowsInvalidOperationException()
    {
        var root = CreateTempDir();
        var passDir = Path.Combine(root, "Pass");
        Directory.CreateDirectory(passDir);
        File.WriteAllText(Path.Combine(passDir, "encryption.key"), Convert.ToBase64String(new byte[16])); // wrong length: AES-128 key, not 256
        File.WriteAllText(Path.Combine(passDir, "db.password.enc"), Convert.ToBase64String(new byte[32]));
        var sut = Build(root);

        var act = () => sut.GetSecureConnectionString("Password=PLACEHOLDER");

        act.Should().Throw<InvalidOperationException>().WithMessage("*decrypt*");
    }

    [Fact]
    public void GetSecureConnectionString_EncryptedDataTooShortForIv_ThrowsInvalidOperationException()
    {
        var root = CreateTempDir();
        var passDir = Path.Combine(root, "Pass");
        Directory.CreateDirectory(passDir);
        File.WriteAllText(Path.Combine(passDir, "encryption.key"), Convert.ToBase64String(new byte[32]));
        File.WriteAllText(Path.Combine(passDir, "db.password.enc"), Convert.ToBase64String(new byte[8])); // shorter than the 16-byte IV
        var sut = Build(root);

        var act = () => sut.GetSecureConnectionString("Password=PLACEHOLDER");

        act.Should().Throw<InvalidOperationException>().WithMessage("*decrypt*");
    }

    [Fact]
    public void GetSecureConnectionString_CorruptCiphertext_ThrowsInvalidOperationException()
    {
        var root = CreateTempDir();
        WriteEncryptedSecrets(root, "willBeCorrupted");
        // Flip a byte in the middle of the ciphertext so PKCS7 unpadding fails.
        var encFile = Path.Combine(root, "Pass", "db.password.enc");
        var bytes = Convert.FromBase64String(File.ReadAllText(encFile));
        bytes[^1] ^= 0xFF;
        File.WriteAllText(encFile, Convert.ToBase64String(bytes));
        var sut = Build(root);

        var act = () => sut.GetSecureConnectionString("Password=PLACEHOLDER");

        act.Should().Throw<InvalidOperationException>().WithMessage("Unable to decrypt the database password");
    }

    [Fact]
    public void GetSecureConnectionString_NonBase64KeyFile_ThrowsInvalidOperationException()
    {
        var root = CreateTempDir();
        var passDir = Path.Combine(root, "Pass");
        Directory.CreateDirectory(passDir);
        File.WriteAllText(Path.Combine(passDir, "encryption.key"), "!!!not valid base64!!!");
        File.WriteAllText(Path.Combine(passDir, "db.password.enc"), Convert.ToBase64String(new byte[32]));
        var sut = Build(root);

        var act = () => sut.GetSecureConnectionString("Password=PLACEHOLDER");

        act.Should().Throw<InvalidOperationException>();
    }
}
