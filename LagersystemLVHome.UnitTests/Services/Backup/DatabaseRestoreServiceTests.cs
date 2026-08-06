using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.UnitTests.Services.Backup;

/// <summary>
/// Covers <see cref="DatabaseRestoreService"/>.
///
/// Two real bugs were found and fixed while writing these tests:
///
/// 1. <c>DecryptAndExtractAsync</c> never actually decrypted, and restoring an encrypted
///    backup was broken in two independent, compounding ways:
///    (a) <c>ValidateBackupAsync</c>'s first gate (<c>IsValidZipAsync</c>) required the
///    raw uploaded stream to already parse as a ZIP archive - but a genuinely
///    AES-encrypted backup (the IV-prefixed ciphertext
///    <c>BackupManagementService.EncryptBackupAsync</c> produces) is opaque binary, not a
///    ZIP, so it was rejected as "Keine gueltige ZIP-Datei" before encryption/password
///    handling was ever reached. Any non-ZIP stream long enough to hold a 16-byte IV is
///    now treated as an encrypted-backup candidate instead. See
///    ValidateBackupAsync_NotAZipButLongEnoughForAnIV_IsTreatedAsEncrypted.
///    (b) even for an input that cleared that gate, <c>DecryptAndExtractAsync</c> - despite
///    its comment "Decryption delegated to EncryptionService when backup encryption is
///    enabled" - just copied the stream verbatim to a .zip path and extracted it as-is.
///    It now performs the actual AES-CBC decryption (key derived from the password the
///    same way <c>BackupManagementService.EncryptBackupAsync</c> does), and a wrong
///    password now fails cleanly instead of silently importing nothing. See
///    RestoreFromBackupAsync_RealAesEncryptedBackup_DecryptsAndImportsSuccessfully and
///    RestoreFromBackupAsync_EncryptedWithWrongPassword_FailsCleanlyWithoutImporting.
///
/// 2. <c>IsBackupEncryptedAsync</c>'s fast-path check looked for a zip entry named
///    <c>"backup_metadata.json"</c>, but the real metadata file
///    <see cref="JsonBackupHelper"/> writes is named <c>"metadata.json"</c> - so that
///    check was dead code for every real backup and detection always fell through to the
///    byte-sniffing heuristic. Now matches the real filename. See
///    IsBackupEncryptedAsync_MetadataJsonEntryPresent_ReturnsFalse.
///
/// The final step of a fully successful <c>RestoreFromBackupAsync</c> call
/// (<c>CountTablesAsync</c>) calls <c>context.Database.GetDbConnection()</c>, which is a
/// relational-provider-only API the EF Core InMemory provider does not support. That
/// means the true "Success = true" happy path cannot be reached with an InMemory-backed
/// context factory; RestoreFromBackupAsync_ValidUnencryptedBackup_RunsImportButFailsAtRelationalOnlyTallyStep
/// documents this InMemory-only seam explicitly while still proving validation, safety
/// backup creation, extraction and the JSON import all work correctly.
/// </summary>
public sealed class DatabaseRestoreServiceTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"drs_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        _tempPaths.Add(dir);
        return dir;
    }

    /// <summary>A stream that reports CanSeek == false, to exercise the "BrowserFileStream"
    /// non-seekable code paths that copy into a MemoryStream first.</summary>
    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private DatabaseRestoreService CreateSut(
        IDbContextFactory<InventoryDbContext> factory,
        string webRoot,
        DatabaseProvider provider = DatabaseProvider.SQLite,
        string connectionString = "",
        IBackupManagementService? backupService = null)
    {
        var jsonHelper = new JsonBackupHelper(
            factory, NullLogger<JsonBackupHelper>.Instance,
            Options.Create(new DatabaseSettings { Provider = provider, ConnectionString = connectionString }));

        var databaseProviderService = Substitute.For<IDatabaseProviderService>();
        databaseProviderService.Provider.Returns(provider);

        var env = Substitute.For<IWebHostEnvironment>();
        env.WebRootPath.Returns(webRoot);

        return new DatabaseRestoreService(
            factory,
            NullLogger<DatabaseRestoreService>.Instance,
            Options.Create(new DatabaseSettings { Provider = provider, ConnectionString = connectionString }),
            Substitute.For<IEncryptionService>(),
            backupService ?? Substitute.For<IBackupManagementService>(),
            env,
            databaseProviderService,
            jsonHelper);
    }

    private static byte[] BuildZip(params (string Name, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var s = entry.Open();
                s.Write(content, 0, content.Length);
            }
        }
        return ms.ToArray();
    }

    private static byte[] EncryptLikeBackupManagementService(byte[] plainZipBytes, string password)
    {
        using var sha256 = SHA256.Create();
        var key = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(plainZipBytes, 0, plainZipBytes.Length);
        return aes.IV.Concat(cipher).ToArray();
    }

    // ----- ValidateBackupAsync -----

    [Fact]
    public async Task ValidateBackupAsync_ValidZipWithMetadata_ReturnsValidAndParsesMetadata()
    {
        var metadata = new BackupMetadata { DatabaseProvider = "SQLite", Version = "1.1", BackupType = "JSON", TableCounts = new() { ["Users"] = 3 } };
        var zip = BuildZip(("metadata.json", JsonSerializer.SerializeToUtf8Bytes(metadata)));
        var sut = CreateSut(CreateFactory(nameof(ValidateBackupAsync_ValidZipWithMetadata_ReturnsValidAndParsesMetadata)), NewTempDir(), backupService: null);

        var result = await sut.ValidateBackupAsync(new MemoryStream(zip));

        result.IsValid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.IsEncrypted.Should().BeFalse();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.TableCounts["Users"].Should().Be(3);
    }

    [Fact]
    public async Task ValidateBackupAsync_TooShortToContainAnIV_ReturnsInvalidWithGermanErrorMessage()
    {
        var sut = CreateSut(CreateFactory(nameof(ValidateBackupAsync_TooShortToContainAnIV_ReturnsInvalidWithGermanErrorMessage)), NewTempDir(), backupService: null);

        var result = await sut.ValidateBackupAsync(new MemoryStream(new byte[] { 1, 2, 3, 4 }));

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ZIP");
    }

    /// <summary>
    /// Regression test: a genuinely AES-encrypted backup (the IV-prefixed ciphertext
    /// BackupManagementService.EncryptBackupAsync produces) is not a valid ZIP by itself -
    /// it used to be rejected outright here as "Keine gueltige ZIP-Datei". Any non-ZIP
    /// stream long enough to plausibly hold a 16-byte IV is now treated as an encrypted
    /// backup candidate instead.
    /// </summary>
    [Fact]
    public async Task ValidateBackupAsync_NotAZipButLongEnoughForAnIV_IsTreatedAsEncrypted()
    {
        var encrypted = EncryptLikeBackupManagementService(BuildZip(("a.json", "{}"u8.ToArray())), "pw");
        var sut = CreateSut(CreateFactory(nameof(ValidateBackupAsync_NotAZipButLongEnoughForAnIV_IsTreatedAsEncrypted)), NewTempDir(), backupService: null);

        var result = await sut.ValidateBackupAsync(new MemoryStream(encrypted));

        result.IsValid.Should().BeTrue();
        result.IsEncrypted.Should().BeTrue();
        result.RequiresPassword.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ValidateBackupAsync_NonSeekableStream_CopiesToMemoryStreamFirst()
    {
        var zip = BuildZip(("a.json", "{}"u8.ToArray()));
        var sut = CreateSut(CreateFactory(nameof(ValidateBackupAsync_NonSeekableStream_CopiesToMemoryStreamFirst)), NewTempDir(), backupService: null);

        var result = await sut.ValidateBackupAsync(new ForwardOnlyStream(zip));

        result.IsValid.Should().BeTrue();
    }

    // ----- IsBackupEncryptedAsync -----

    [Fact]
    public async Task IsBackupEncryptedAsync_EncryptedMarkerEntryPresent_ReturnsTrue()
    {
        var zip = BuildZip((".encrypted", Array.Empty<byte>()), ("payload.bin", new byte[] { 1, 2, 3 }));
        var sut = CreateSut(CreateFactory(nameof(IsBackupEncryptedAsync_EncryptedMarkerEntryPresent_ReturnsTrue)), NewTempDir(), backupService: null);

        (await sut.IsBackupEncryptedAsync(new MemoryStream(zip))).Should().BeTrue();
    }

    [Fact]
    public async Task IsBackupEncryptedAsync_MetadataJsonEntryPresent_ReturnsFalse()
    {
        // Regression test: this fast-path check used to look for the wrong filename
        // ("backup_metadata.json"), so it was dead code for every real backup -
        // JsonBackupHelper actually writes "metadata.json". Now matches the real name.
        var zip = BuildZip(("metadata.json", "{}"u8.ToArray()));
        var sut = CreateSut(CreateFactory(nameof(IsBackupEncryptedAsync_MetadataJsonEntryPresent_ReturnsFalse)), NewTempDir(), backupService: null);

        (await sut.IsBackupEncryptedAsync(new MemoryStream(zip))).Should().BeFalse();
    }

    [Fact]
    public async Task IsBackupEncryptedAsync_SqliteMagicHeader_ReturnsFalse()
    {
        var sqliteHeader = "SQLite format 3\0"u8.ToArray();
        var zip = BuildZip(("db.sqlite", sqliteHeader));
        var sut = CreateSut(CreateFactory(nameof(IsBackupEncryptedAsync_SqliteMagicHeader_ReturnsFalse)), NewTempDir(), backupService: null);

        (await sut.IsBackupEncryptedAsync(new MemoryStream(zip))).Should().BeFalse();
    }

    [Fact]
    public async Task IsBackupEncryptedAsync_PostgresDumpHeader_ReturnsFalse()
    {
        var pgHeader = new byte[] { 0x50, 0x47, 0x44, 0x4D, 0x50 }; // "PGDMP"
        var zip = BuildZip(("dump.backup_extra", pgHeader));
        // Give the entry a name that does NOT match the known db-extension fast path,
        // so detection is forced through the byte-header sniff instead.
        var sut = CreateSut(CreateFactory(nameof(IsBackupEncryptedAsync_PostgresDumpHeader_ReturnsFalse)), NewTempDir(), backupService: null);

        (await sut.IsBackupEncryptedAsync(new MemoryStream(zip))).Should().BeFalse();
    }

    [Fact]
    public async Task IsBackupEncryptedAsync_JsonArrayContent_ReturnsFalse()
    {
        var content = Encoding.UTF8.GetBytes("[{\"Id\":1,\"Name\":\"x\"}]");
        var zip = BuildZip(("Warehouses.json", content));
        var sut = CreateSut(CreateFactory(nameof(IsBackupEncryptedAsync_JsonArrayContent_ReturnsFalse)), NewTempDir(), backupService: null);

        (await sut.IsBackupEncryptedAsync(new MemoryStream(zip))).Should().BeFalse();
    }

    [Fact]
    public async Task IsBackupEncryptedAsync_HighNonPrintableByteRatio_ReturnsTrue()
    {
        // A run of a single non-printable, non-whitespace control byte (0x01) is
        // unambiguously >90% "non-printable" under the heuristic (< 32, excluding
        // \n/\r/\t), regardless of exact composition.
        var allNonPrintable = Enumerable.Repeat((byte)0x01, 600).ToArray();
        var zip = BuildZip(("payload.enc", allNonPrintable));
        var sut = CreateSut(CreateFactory(nameof(IsBackupEncryptedAsync_HighNonPrintableByteRatio_ReturnsTrue)), NewTempDir(), backupService: null);

        (await sut.IsBackupEncryptedAsync(new MemoryStream(zip))).Should().BeTrue();
    }

    [Fact]
    public async Task IsBackupEncryptedAsync_EmptyArchive_ReturnsFalse()
    {
        using var ms = new MemoryStream();
        using (new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) { }
        var sut = CreateSut(CreateFactory(nameof(IsBackupEncryptedAsync_EmptyArchive_ReturnsFalse)), NewTempDir(), backupService: null);

        (await sut.IsBackupEncryptedAsync(new MemoryStream(ms.ToArray()))).Should().BeFalse();
    }

    [Fact]
    public async Task IsBackupEncryptedAsync_NotAZip_CaughtAndReturnsFalse()
    {
        var sut = CreateSut(CreateFactory(nameof(IsBackupEncryptedAsync_NotAZip_CaughtAndReturnsFalse)), NewTempDir(), backupService: null);

        (await sut.IsBackupEncryptedAsync(new MemoryStream(new byte[] { 9, 9, 9 }))).Should().BeFalse();
    }

    // ----- RestoreFromBackupAsync -----

    [Fact]
    public async Task RestoreFromBackupAsync_InvalidZip_ReturnsFailureWithoutCreatingSafetyBackup()
    {
        var backupService = Substitute.For<IBackupManagementService>();
        var sut = CreateSut(CreateFactory(nameof(RestoreFromBackupAsync_InvalidZip_ReturnsFailureWithoutCreatingSafetyBackup)), NewTempDir(), backupService: backupService);

        var result = await sut.RestoreFromBackupAsync(new MemoryStream(new byte[] { 1, 2, 3 }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ZIP");
        await backupService.DidNotReceiveWithAnyArgs().CreateBackupAsync(default);
    }

    [Fact]
    public async Task RestoreFromBackupAsync_EncryptedButNoPassword_ReturnsFailureBeforeSafetyBackup()
    {
        var zip = BuildZip((".encrypted", Array.Empty<byte>()));
        var backupService = Substitute.For<IBackupManagementService>();
        var sut = CreateSut(CreateFactory(nameof(RestoreFromBackupAsync_EncryptedButNoPassword_ReturnsFailureBeforeSafetyBackup)), NewTempDir(), backupService: backupService);

        var result = await sut.RestoreFromBackupAsync(new MemoryStream(zip), password: null);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Passwort");
        await backupService.DidNotReceiveWithAnyArgs().CreateBackupAsync(default);
    }

    [Fact]
    public async Task RestoreFromBackupAsync_RealAesEncryptedBackup_DecryptsAndImportsSuccessfully()
    {
        // Regression test for both halves of the fixed bug: a genuinely AES-encrypted
        // backup (same IV-prefixed scheme BackupManagementService.EncryptBackupAsync
        // produces) is no longer a valid ZIP container by itself - ValidateBackupAsync now
        // recognizes "not a ZIP, but plausibly sized" as an encrypted-backup candidate
        // instead of rejecting it outright, and DecryptAndExtractAsync now genuinely
        // decrypts (same key derivation, same IV placement) before extracting. Uses a real
        // JsonBackupHelper export as the plaintext so a successful decrypt is proven by the
        // seeded Warehouse actually landing in the target database.
        var sourceFactory = CreateFactory(nameof(RestoreFromBackupAsync_RealAesEncryptedBackup_DecryptsAndImportsSuccessfully) + "_src");
        await using (var db = sourceFactory.CreateDbContext())
        {
            db.Warehouses.Add(new Warehouse { Id = 1, Name = "WH1", Address = "a", IsActive = true });
            await db.SaveChangesAsync();
        }
        var exportHelper = new JsonBackupHelper(sourceFactory, NullLogger<JsonBackupHelper>.Instance,
            Options.Create(new DatabaseSettings { Provider = DatabaseProvider.SQLite }));
        var zipPath = Path.Combine(NewTempDir(), "src.zip");
        await exportHelper.CreateJsonBackupAsync(zipPath);
        var plainZip = await File.ReadAllBytesAsync(zipPath);
        var encrypted = EncryptLikeBackupManagementService(plainZip, "correct-password");

        var targetFactory = CreateFactory(nameof(RestoreFromBackupAsync_RealAesEncryptedBackup_DecryptsAndImportsSuccessfully) + "_dst");
        var backupService = Substitute.For<IBackupManagementService>();
        var sut = CreateSut(targetFactory, NewTempDir(), backupService: backupService);
        var progressEvents = new List<RestoreProgress>();
        IProgress<RestoreProgress> progress = new SyncProgress<RestoreProgress>(p => progressEvents.Add(p));

        var result = await sut.RestoreFromBackupAsync(new MemoryStream(encrypted), password: "correct-password", progress: progress);

        // Same InMemory-provider seam as the unencrypted happy-path test (see class
        // remarks): the import itself succeeds, only the final relational-only tally fails.
        result.SafetyBackupPath.Should().NotBeNullOrEmpty();
        progressEvents.Should().Contain(p => p.Step == RestoreStep.Decrypting);
        await backupService.Received(1).CreateBackupAsync(Arg.Any<CancellationToken>());
        await using var verifyDb = targetFactory.CreateDbContext();
        (await verifyDb.Warehouses.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RestoreFromBackupAsync_EncryptedWithWrongPassword_FailsCleanlyWithoutImporting()
    {
        var plainZip = BuildZip(("Warehouses.json", "[]"u8.ToArray()));
        var encrypted = EncryptLikeBackupManagementService(plainZip, "correct-password");

        var targetFactory = CreateFactory(nameof(RestoreFromBackupAsync_EncryptedWithWrongPassword_FailsCleanlyWithoutImporting));
        var backupService = Substitute.For<IBackupManagementService>();
        var sut = CreateSut(targetFactory, NewTempDir(), backupService: backupService);

        var result = await sut.RestoreFromBackupAsync(new MemoryStream(encrypted), password: "wrong-password");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Passwort");
        await using var verifyDb = targetFactory.CreateDbContext();
        (await verifyDb.Warehouses.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RestoreFromBackupAsync_ValidUnencryptedBackup_RunsImportButFailsAtRelationalOnlyTallyStep()
    {
        // Documents the InMemory-provider seam (see class remarks): validation, safety
        // backup, extraction and the JSON import all genuinely execute and succeed; only
        // the final CountTablesAsync step (which needs a real relational DbConnection)
        // fails, so the overall result is Success = false even though the data landed.
        var sourceFactory = CreateFactory(nameof(RestoreFromBackupAsync_ValidUnencryptedBackup_RunsImportButFailsAtRelationalOnlyTallyStep) + "_src");
        await using (var db = sourceFactory.CreateDbContext())
        {
            db.Warehouses.Add(new Warehouse { Id = 1, Name = "WH1", Address = "a", IsActive = true });
            await db.SaveChangesAsync();
        }
        var exportHelper = new JsonBackupHelper(sourceFactory, NullLogger<JsonBackupHelper>.Instance,
            Options.Create(new DatabaseSettings { Provider = DatabaseProvider.SQLite }));
        var zipPath = Path.Combine(NewTempDir(), "src.zip");
        await exportHelper.CreateJsonBackupAsync(zipPath);
        var zipBytes = await File.ReadAllBytesAsync(zipPath);

        var targetFactory = CreateFactory(nameof(RestoreFromBackupAsync_ValidUnencryptedBackup_RunsImportButFailsAtRelationalOnlyTallyStep) + "_dst");
        var backupService = Substitute.For<IBackupManagementService>();
        var sut = CreateSut(targetFactory, NewTempDir(), backupService: backupService);
        var progressEvents = new List<RestoreProgress>();
        IProgress<RestoreProgress> progress = new SyncProgress<RestoreProgress>(p => progressEvents.Add(p));

        var result = await sut.RestoreFromBackupAsync(new MemoryStream(zipBytes), progress: progress);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        result.SafetyBackupPath.Should().NotBeNullOrEmpty();
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        await backupService.Received(1).CreateBackupAsync(Arg.Any<CancellationToken>());
        progressEvents.Should().Contain(p => p.Step == RestoreStep.Validating);
        progressEvents.Should().Contain(p => p.Step == RestoreStep.CreatingSafetyBackup);
        progressEvents.Should().Contain(p => p.Step == RestoreStep.Extracting);
        progressEvents.Should().Contain(p => p.Step == RestoreStep.ReplacingDatabase);

        // The import itself (JsonBackupHelper.RestoreFromJsonBackupAsync) did complete
        // before the relational-only tally step blew up.
        await using var verifyDb = targetFactory.CreateDbContext();
        (await verifyDb.Warehouses.CountAsync()).Should().Be(1);
    }

    // ----- GetCurrentDatabaseInfoAsync -----

    [Fact]
    public async Task GetCurrentDatabaseInfoAsync_SQLiteProvider_MissingFile_SizeBytesStaysZero()
    {
        var factory = CreateFactory(nameof(GetCurrentDatabaseInfoAsync_SQLiteProvider_MissingFile_SizeBytesStaysZero));
        var missingPath = Path.Combine(Path.GetTempPath(), "no-such-db-" + Guid.NewGuid() + ".db");
        var sut = CreateSut(factory, NewTempDir(), DatabaseProvider.SQLite, $"Data Source={missingPath};Cache=Shared", backupService: null);

        var info = await sut.GetCurrentDatabaseInfoAsync();

        info.Provider.Should().Be(DatabaseProvider.SQLite.ToString());
        info.SizeBytes.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentDatabaseInfoAsync_SQLiteProvider_NoTrailingSemicolon_ParsesToEndOfString()
    {
        var factory = CreateFactory(nameof(GetCurrentDatabaseInfoAsync_SQLiteProvider_NoTrailingSemicolon_ParsesToEndOfString));
        var missingPath = Path.Combine(Path.GetTempPath(), "no-such-db-" + Guid.NewGuid() + ".db");
        var sut = CreateSut(factory, NewTempDir(), DatabaseProvider.SQLite, $"Data Source={missingPath}", backupService: null);

        var info = await sut.GetCurrentDatabaseInfoAsync();

        info.SizeBytes.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentDatabaseInfoAsync_NonSqliteProvider_SkipsFileSizeLookupEntirely()
    {
        var factory = CreateFactory(nameof(GetCurrentDatabaseInfoAsync_NonSqliteProvider_SkipsFileSizeLookupEntirely));
        var sut = CreateSut(factory, NewTempDir(), DatabaseProvider.PostgreSQL, "Host=nonexistent;Database=x", backupService: null);

        var info = await sut.GetCurrentDatabaseInfoAsync();

        info.Provider.Should().Be(DatabaseProvider.PostgreSQL.ToString());
        info.SizeBytes.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentDatabaseInfoAsync_CountsProductsCategoriesUsersWarehouses()
    {
        var factory = CreateFactory(nameof(GetCurrentDatabaseInfoAsync_CountsProductsCategoriesUsersWarehouses));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(new Warehouse { Id = 1, Name = "WH1", Address = "a", IsActive = true });
            db.Categories.Add(new Category { Name = "C1" });
            db.Products.Add(new Product { Name = "P1", WarehouseId = 1 });
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory, NewTempDir(), backupService: null);

        var info = await sut.GetCurrentDatabaseInfoAsync();

        info.WarehouseCount.Should().Be(1);
        info.CategoryCount.Should().Be(1);
        info.ProductCount.Should().Be(1);
    }

    // ----- GetBackupInfoAsync -----

    [Fact]
    public async Task GetBackupInfoAsync_WithMetadata_PopulatesFieldsFromTableCounts()
    {
        var metadata = new BackupMetadata
        {
            BackupDate = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            DatabaseProvider = "SQLite",
            TableCounts = new() { ["Products"] = 5, ["Categories"] = 2, ["Users"] = 1, ["Warehouses"] = 1 }
        };
        var zip = BuildZip(("metadata.json", JsonSerializer.SerializeToUtf8Bytes(metadata)));
        var sut = CreateSut(CreateFactory(nameof(GetBackupInfoAsync_WithMetadata_PopulatesFieldsFromTableCounts)), NewTempDir(), backupService: null);

        var info = await sut.GetBackupInfoAsync(new MemoryStream(zip));

        info.CreatedAt.Should().Be(metadata.BackupDate);
        info.Provider.Should().Be("SQLite");
        info.ProductCount.Should().Be(5);
        info.CategoryCount.Should().Be(2);
        info.UserCount.Should().Be(1);
        info.WarehouseCount.Should().Be(1);
        info.IsEncrypted.Should().BeFalse();
        info.SizeBytes.Should().Be(zip.Length);
    }

    [Fact]
    public async Task GetBackupInfoAsync_NoMetadataEntry_LeavesCountsAtZero()
    {
        var zip = BuildZip(("somefile.txt", "hello"u8.ToArray()));
        var sut = CreateSut(CreateFactory(nameof(GetBackupInfoAsync_NoMetadataEntry_LeavesCountsAtZero)), NewTempDir(), backupService: null);

        var info = await sut.GetBackupInfoAsync(new MemoryStream(zip));

        info.ProductCount.Should().Be(0);
        info.Provider.Should().Be(string.Empty);
    }

    // ----- CreateSafetyBackupAsync -----

    [Fact]
    public async Task CreateSafetyBackupAsync_CreatesSafetyDirectoryAndDelegatesToBackupService()
    {
        var webRoot = NewTempDir();
        var backupService = Substitute.For<IBackupManagementService>();
        var sut = CreateSut(CreateFactory(nameof(CreateSafetyBackupAsync_CreatesSafetyDirectoryAndDelegatesToBackupService)), webRoot, backupService: backupService);

        var path = await sut.CreateSafetyBackupAsync();

        path.Should().StartWith(Path.Combine(webRoot, "backups", "safety"));
        Directory.Exists(Path.Combine(webRoot, "backups", "safety")).Should().BeTrue();
    }
}

/// <summary>Invokes the progress handler synchronously on the calling thread, avoiding the async dispatch of <see cref="Progress{T}"/>.</summary>
file sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
