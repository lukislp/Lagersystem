using System.IO.Compression;
using System.Security.Cryptography;
using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Application.Services.BackupProviders;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.UnitTests.Services.Backup;

/// <summary>
/// Covers <see cref="BackupManagementService"/>.
///
/// <see cref="JsonBackupHelper"/> and <see cref="BackupProviderFactory"/> are sealed,
/// non-virtual concrete classes, so they cannot be substituted with NSubstitute; every
/// test below wires up a *real* <see cref="JsonBackupHelper"/> bound to the same EF
/// InMemory context factory as the system under test (so <c>CreateBackupAsync</c>'s JSON
/// export genuinely runs), and a real <see cref="BackupProviderFactory"/> populated with
/// NSubstitute-backed <see cref="IBackupProviderUploader"/> fakes (the actual seam this
/// service depends on for provider I/O). <c>GetRetentionType()</c> reads
/// <c>DateTime.UtcNow</c> directly (no injectable clock), so its Daily/Weekly/Monthly
/// branch selection cannot be pinned in a unit test; assertions accept any of the three
/// valid values instead of hard-coding one, to avoid date-dependent flakiness (only
/// today's actual branch is exercised).
/// </summary>
public sealed class BackupManagementServiceTests : IDisposable
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

    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private sealed class ThrowingContextFactory : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => throw new InvalidOperationException("db unavailable");
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    /// <summary>Fake "encryption" that's easy to assert on: wraps/unwraps with a marker prefix.</summary>
    private static ISecureConfigurationService CreateFakeSecureConfig()
    {
        var secureConfig = Substitute.For<ISecureConfigurationService>();
        secureConfig.Encrypt(Arg.Any<string>()).Returns(ci => "ENC(" + ci.Arg<string>() + ")");
        secureConfig.Decrypt(Arg.Any<string>()).Returns(ci =>
        {
            var s = ci.Arg<string>();
            return s.StartsWith("ENC(") ? s[4..^1] : s;
        });
        secureConfig.IsEncrypted(Arg.Any<string>()).Returns(ci => ci.Arg<string>().StartsWith("ENC("));
        return secureConfig;
    }

    private static IBackupProviderUploader CreateUploader(BackupProviderType type)
    {
        var uploader = Substitute.For<IBackupProviderUploader>();
        uploader.SupportedProviderType.Returns(type);
        return uploader;
    }

    private BackupManagementService CreateSut(
        IDbContextFactory<InventoryDbContext> factory,
        IEnumerable<IBackupProviderUploader>? uploaders = null,
        IEmailService? emailService = null,
        ISecureConfigurationService? secureConfig = null,
        IDatabaseProviderService? databaseProviderService = null,
        IEncryptionService? encryptionService = null)
    {
        var jsonHelper = new JsonBackupHelper(
            factory,
            NullLogger<JsonBackupHelper>.Instance,
            Options.Create(new DatabaseSettings { Provider = DatabaseProvider.SQLite }));

        var providerFactory = new BackupProviderFactory(
            uploaders ?? Array.Empty<IBackupProviderUploader>(),
            NullLogger<BackupProviderFactory>.Instance);

        return new BackupManagementService(
            factory,
            databaseProviderService ?? Substitute.For<IDatabaseProviderService>(),
            encryptionService ?? Substitute.For<IEncryptionService>(),
            emailService ?? Substitute.For<IEmailService>(),
            NullLogger<BackupManagementService>.Instance,
            jsonHelper,
            providerFactory,
            secureConfig ?? CreateFakeSecureConfig());
    }

    private static async Task<BackupProvider> SeedProviderAsync(
        IDbContextFactory<InventoryDbContext> factory,
        string name,
        BackupProviderType type,
        bool enabled = true,
        string configuration = "{}",
        DateTime? lastBackupAt = null)
    {
        await using var db = factory.CreateDbContext();
        var provider = new BackupProvider
        {
            Name = name,
            Type = type,
            Enabled = enabled,
            Configuration = configuration,
            LastBackupAt = lastBackupAt
        };
        db.BackupProviders.Add(provider);
        await db.SaveChangesAsync();
        return provider;
    }

    // ----- GetSettingsAsync / UpdateSettingsAsync -----

    [Fact]
    public async Task GetSettingsAsync_NoRow_CreatesAndPersistsDefaults()
    {
        var factory = CreateFactory(nameof(GetSettingsAsync_NoRow_CreatesAndPersistsDefaults));
        var sut = CreateSut(factory);

        var settings = await sut.GetSettingsAsync();

        settings.Enabled.Should().BeTrue();
        settings.RetentionDays.Should().Be(30);

        await using var db = factory.CreateDbContext();
        (await db.BackupSettings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetSettingsAsync_ExistingRow_ReturnsStoredValues()
    {
        var factory = CreateFactory(nameof(GetSettingsAsync_ExistingRow_ReturnsStoredValues));
        await using (var db = factory.CreateDbContext())
        {
            db.BackupSettings.Add(new LagersystemLVHome.Domain.Models.BackupSettings { Enabled = false, RetentionDays = 5 });
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var settings = await sut.GetSettingsAsync();

        settings.Enabled.Should().BeFalse();
        settings.RetentionDays.Should().Be(5);
    }

    [Fact]
    public async Task UpdateSettingsAsync_PersistsAndSetsUpdatedAt()
    {
        var factory = CreateFactory(nameof(UpdateSettingsAsync_PersistsAndSetsUpdatedAt));
        var sut = CreateSut(factory);
        var settings = await sut.GetSettingsAsync();
        settings.RetentionDays = 99;

        var updated = await sut.UpdateSettingsAsync(settings);

        updated.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        (await sut.GetSettingsAsync()).RetentionDays.Should().Be(99);
    }

    // ----- GetProvidersAsync -----

    [Fact]
    public async Task GetProvidersAsync_OrdersByName_ComputesStatsFromHistory_DecryptsConfig()
    {
        var factory = CreateFactory(nameof(GetProvidersAsync_OrdersByName_ComputesStatsFromHistory_DecryptsConfig));
        var providerB = await SeedProviderAsync(factory, "B-Provider", BackupProviderType.Local, configuration: "ENC(plain-config)");
        var providerA = await SeedProviderAsync(factory, "A-Provider", BackupProviderType.Local);

        await using (var db = factory.CreateDbContext())
        {
            db.BackupHistory.AddRange(
                new BackupHistory { BackupProviderId = providerB.Id, FileName = "f1", Status = BackupStatus.Success, SizeBytes = 100, BackupDate = DateTime.UtcNow.AddDays(-2) },
                new BackupHistory { BackupProviderId = providerB.Id, FileName = "f2", Status = BackupStatus.Success, SizeBytes = 200, BackupDate = DateTime.UtcNow.AddDays(-1) },
                new BackupHistory { BackupProviderId = providerB.Id, FileName = "f3", Status = BackupStatus.Failed, SizeBytes = 0, BackupDate = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var sut = CreateSut(factory);

        var providers = await sut.GetProvidersAsync();

        providers.Select(p => p.Name).Should().Equal("A-Provider", "B-Provider");
        var b = providers.Single(p => p.Name == "B-Provider");
        b.TotalBackups.Should().Be(2);
        b.TotalSizeBytes.Should().Be(300);
        b.FailedBackups.Should().Be(1);
        b.LastBackupAt.Should().NotBeNull();
        b.Configuration.Should().Be("plain-config");

        var a = providers.Single(p => p.Name == "A-Provider");
        a.TotalBackups.Should().Be(0);
        a.TotalSizeBytes.Should().Be(0);
        a.FailedBackups.Should().Be(0);
        a.LastBackupAt.Should().BeNull();
    }

    [Fact]
    public async Task GetProvidersAsync_DecryptThrows_LogsAndLeavesConfigurationUnchanged()
    {
        var factory = CreateFactory(nameof(GetProvidersAsync_DecryptThrows_LogsAndLeavesConfigurationUnchanged));
        await SeedProviderAsync(factory, "P1", BackupProviderType.Local, configuration: "unparseable");
        var secureConfig = Substitute.For<ISecureConfigurationService>();
        secureConfig.Decrypt(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("bad"));
        var sut = CreateSut(factory, secureConfig: secureConfig);

        var providers = await sut.GetProvidersAsync();

        providers.Should().ContainSingle();
        providers[0].Configuration.Should().Be("unparseable");
    }

    // ----- AddProviderAsync -----

    [Fact]
    public async Task AddProviderAsync_SetsCreatedAtNormalizesKindAndEncryptsConfig()
    {
        var factory = CreateFactory(nameof(AddProviderAsync_SetsCreatedAtNormalizesKindAndEncryptsConfig));
        var sut = CreateSut(factory);
        var unspecifiedLastBackup = DateTime.SpecifyKind(new DateTime(2025, 1, 1), DateTimeKind.Unspecified);
        var provider = new BackupProvider
        {
            Name = "New",
            Type = BackupProviderType.Local,
            Configuration = "plain-json",
            LastBackupAt = unspecifiedLastBackup
        };

        var added = await sut.AddProviderAsync(provider);

        added.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        added.LastBackupAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
        added.Configuration.Should().Be("ENC(plain-json)");

        await using var db = factory.CreateDbContext();
        (await db.BackupProviders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AddProviderAsync_EncryptThrows_WrapsInInvalidOperationException()
    {
        var factory = CreateFactory(nameof(AddProviderAsync_EncryptThrows_WrapsInInvalidOperationException));
        var secureConfig = Substitute.For<ISecureConfigurationService>();
        secureConfig.Encrypt(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("boom"));
        var sut = CreateSut(factory, secureConfig: secureConfig);
        var provider = new BackupProvider { Name = "X", Type = BackupProviderType.Local, Configuration = "cfg" };

        var act = async () => await sut.AddProviderAsync(provider);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ----- UpdateProviderAsync -----

    [Fact]
    public async Task UpdateProviderAsync_NormalizesKindsAndEncryptsPlainConfig()
    {
        var factory = CreateFactory(nameof(UpdateProviderAsync_NormalizesKindsAndEncryptsPlainConfig));
        var sut = CreateSut(factory);
        var provider = new BackupProvider
        {
            Name = "P",
            Type = BackupProviderType.Local,
            Configuration = "plain",
            CreatedAt = DateTime.SpecifyKind(new DateTime(2024, 1, 1), DateTimeKind.Unspecified),
            LastBackupAt = DateTime.SpecifyKind(new DateTime(2024, 6, 1), DateTimeKind.Unspecified)
        };

        var updated = await sut.UpdateProviderAsync(provider);

        updated.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        updated.LastBackupAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
        updated.Configuration.Should().Be("ENC(plain)");
    }

    [Fact]
    public async Task UpdateProviderAsync_AlreadyEncrypted_SkipsReEncryption()
    {
        var factory = CreateFactory(nameof(UpdateProviderAsync_AlreadyEncrypted_SkipsReEncryption));
        var sut = CreateSut(factory);
        var provider = new BackupProvider { Name = "P", Type = BackupProviderType.Local, Configuration = "ENC(already)" };

        var updated = await sut.UpdateProviderAsync(provider);

        updated.Configuration.Should().Be("ENC(already)");
    }

    [Fact]
    public async Task UpdateProviderAsync_EncryptThrows_WrapsInInvalidOperationException()
    {
        var factory = CreateFactory(nameof(UpdateProviderAsync_EncryptThrows_WrapsInInvalidOperationException));
        var secureConfig = Substitute.For<ISecureConfigurationService>();
        secureConfig.IsEncrypted(Arg.Any<string>()).Returns(false);
        secureConfig.Encrypt(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("boom"));
        var sut = CreateSut(factory, secureConfig: secureConfig);
        var provider = new BackupProvider { Name = "P", Type = BackupProviderType.Local, Configuration = "cfg" };

        var act = async () => await sut.UpdateProviderAsync(provider);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ----- DeleteProviderAsync -----

    [Fact]
    public async Task DeleteProviderAsync_RemovesExisting()
    {
        var factory = CreateFactory(nameof(DeleteProviderAsync_RemovesExisting));
        var provider = await SeedProviderAsync(factory, "ToDelete", BackupProviderType.Local);
        var sut = CreateSut(factory);

        await sut.DeleteProviderAsync(provider.Id);

        await using var db = factory.CreateDbContext();
        (await db.BackupProviders.AnyAsync(p => p.Id == provider.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProviderAsync_NotFound_IsNoOp()
    {
        var factory = CreateFactory(nameof(DeleteProviderAsync_NotFound_IsNoOp));
        var sut = CreateSut(factory);

        var act = async () => await sut.DeleteProviderAsync(999);

        await act.Should().NotThrowAsync();
    }

    // ----- GetHistoryAsync -----

    [Fact]
    public async Task GetHistoryAsync_FiltersByProviderOrdersDescendingAndLimits()
    {
        var factory = CreateFactory(nameof(GetHistoryAsync_FiltersByProviderOrdersDescendingAndLimits));
        var p1 = await SeedProviderAsync(factory, "P1", BackupProviderType.Local);
        var p2 = await SeedProviderAsync(factory, "P2", BackupProviderType.Local);
        await using (var db = factory.CreateDbContext())
        {
            db.BackupHistory.AddRange(
                new BackupHistory { BackupProviderId = p1.Id, FileName = "a", BackupDate = DateTime.UtcNow.AddMinutes(-3) },
                new BackupHistory { BackupProviderId = p1.Id, FileName = "b", BackupDate = DateTime.UtcNow.AddMinutes(-1) },
                new BackupHistory { BackupProviderId = p2.Id, FileName = "c", BackupDate = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var forP1 = await sut.GetHistoryAsync(p1.Id);
        forP1.Select(h => h.FileName).Should().Equal("b", "a");

        var limited = await sut.GetHistoryAsync(limit: 1);
        limited.Should().ContainSingle().Which.FileName.Should().Be("c");
    }

    // ----- TestProviderAsync -----

    [Fact]
    public async Task TestProviderAsync_ProviderNotFound_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(TestProviderAsync_ProviderNotFound_ReturnsFalse));
        var sut = CreateSut(factory);

        (await sut.TestProviderAsync(123)).Should().BeFalse();
    }

    [Fact]
    public async Task TestProviderAsync_DelegatesToUploaderAndReturnsResult()
    {
        var factory = CreateFactory(nameof(TestProviderAsync_DelegatesToUploaderAndReturnsResult));
        var provider = await SeedProviderAsync(factory, "P", BackupProviderType.Local, configuration: "ENC(cfg)");
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.TestConnectionAsync(Arg.Any<BackupProvider>()).Returns(true);
        var sut = CreateSut(factory, uploaders: new[] { uploader });

        var result = await sut.TestProviderAsync(provider.Id);

        result.Should().BeTrue();
        await uploader.Received(1).TestConnectionAsync(Arg.Is<BackupProvider>(p => p.Configuration == "cfg"));
    }

    [Fact]
    public async Task TestProviderAsync_UploaderThrows_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(TestProviderAsync_UploaderThrows_ReturnsFalse));
        var provider = await SeedProviderAsync(factory, "P", BackupProviderType.Local);
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.TestConnectionAsync(Arg.Any<BackupProvider>()).Returns<bool>(_ => throw new InvalidOperationException("down"));
        var sut = CreateSut(factory, uploaders: new[] { uploader });

        (await sut.TestProviderAsync(provider.Id)).Should().BeFalse();
    }

    // ----- CleanupOldBackupsAsync -----

    [Fact]
    public async Task CleanupOldBackupsAsync_RemovesEntriesOlderThanRetentionWindowPerType()
    {
        // Regression test for an inverted-sign bug: the Weekly/Monthly cutoffs used to be
        // computed as cutoffDate.AddDays(+28) / (+365) instead of extending the cutoff
        // further into the past. That made the Weekly cutoff *tighter* than Daily's
        // (now-2 days instead of now-30) and pushed the Monthly cutoff into the future
        // entirely - so every Monthly-retention backup, however recent, was deleted
        // unconditionally. "monthly-recent" below is the key regression assertion: it must
        // now survive, and "monthly-old" (well past the fixed now-395 cutoff) proves
        // Monthly cleanup still actually deletes genuinely stale backups.
        var factory = CreateFactory(nameof(CleanupOldBackupsAsync_RemovesEntriesOlderThanRetentionWindowPerType));
        var provider = await SeedProviderAsync(factory, "P", BackupProviderType.Local);
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.BackupHistory.AddRange(
                new BackupHistory { BackupProviderId = provider.Id, FileName = "daily-old", RetentionType = BackupRetentionType.Daily, BackupDate = now.AddDays(-40) },
                new BackupHistory { BackupProviderId = provider.Id, FileName = "daily-recent", RetentionType = BackupRetentionType.Daily, BackupDate = now.AddDays(-1) },
                new BackupHistory { BackupProviderId = provider.Id, FileName = "weekly-old", RetentionType = BackupRetentionType.Weekly, BackupDate = now.AddDays(-100) },
                new BackupHistory { BackupProviderId = provider.Id, FileName = "weekly-recent", RetentionType = BackupRetentionType.Weekly, BackupDate = now },
                new BackupHistory { BackupProviderId = provider.Id, FileName = "monthly-recent", RetentionType = BackupRetentionType.Monthly, BackupDate = now },
                new BackupHistory { BackupProviderId = provider.Id, FileName = "monthly-old", RetentionType = BackupRetentionType.Monthly, BackupDate = now.AddDays(-400) });
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        await sut.CleanupOldBackupsAsync(retentionDays: 30);

        await using var verifyDb = factory.CreateDbContext();
        var remaining = await verifyDb.BackupHistory.Select(h => h.FileName).ToListAsync();
        remaining.Should().Contain("daily-recent");
        remaining.Should().NotContain("daily-old");
        remaining.Should().Contain("weekly-recent");
        remaining.Should().NotContain("weekly-old");
        remaining.Should().Contain("monthly-recent", "a monthly backup this recent must survive - the cutoff must not land in the future");
        remaining.Should().NotContain("monthly-old");
    }

    // ----- ValidateBackupAsync -----

    [Fact]
    public async Task ValidateBackupAsync_HistoryNotFound_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(ValidateBackupAsync_HistoryNotFound_ReturnsFalse));
        var sut = CreateSut(factory);

        (await sut.ValidateBackupAsync(999)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateBackupAsync_UploaderConfirms_SetsVerifiedAndReturnsTrue()
    {
        var factory = CreateFactory(nameof(ValidateBackupAsync_UploaderConfirms_SetsVerifiedAndReturnsTrue));
        var provider = await SeedProviderAsync(factory, "P", BackupProviderType.Local);
        int historyId;
        await using (var db = factory.CreateDbContext())
        {
            var h = new BackupHistory { BackupProviderId = provider.Id, FileName = "f" };
            db.BackupHistory.Add(h);
            await db.SaveChangesAsync();
            historyId = h.Id;
        }
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.ValidateAsync(Arg.Any<BackupHistory>()).Returns(true);
        var sut = CreateSut(factory, uploaders: new[] { uploader });

        (await sut.ValidateBackupAsync(historyId)).Should().BeTrue();

        await using var verifyDb = factory.CreateDbContext();
        var reloaded = await verifyDb.BackupHistory.SingleAsync(h => h.Id == historyId);
        reloaded.IsVerified.Should().BeTrue();
        reloaded.VerifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateBackupAsync_UploaderRejects_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(ValidateBackupAsync_UploaderRejects_ReturnsFalse));
        var provider = await SeedProviderAsync(factory, "P", BackupProviderType.Local);
        int historyId;
        await using (var db = factory.CreateDbContext())
        {
            var h = new BackupHistory { BackupProviderId = provider.Id, FileName = "f" };
            db.BackupHistory.Add(h);
            await db.SaveChangesAsync();
            historyId = h.Id;
        }
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.ValidateAsync(Arg.Any<BackupHistory>()).Returns(false);
        var sut = CreateSut(factory, uploaders: new[] { uploader });

        (await sut.ValidateBackupAsync(historyId)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateBackupAsync_UploaderThrows_IsCaughtAndReturnsFalse()
    {
        var factory = CreateFactory(nameof(ValidateBackupAsync_UploaderThrows_IsCaughtAndReturnsFalse));
        var provider = await SeedProviderAsync(factory, "P", BackupProviderType.Local);
        int historyId;
        await using (var db = factory.CreateDbContext())
        {
            var h = new BackupHistory { BackupProviderId = provider.Id, FileName = "f" };
            db.BackupHistory.Add(h);
            await db.SaveChangesAsync();
            historyId = h.Id;
        }
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.ValidateAsync(Arg.Any<BackupHistory>()).Returns<bool>(_ => throw new InvalidOperationException("io error"));
        var sut = CreateSut(factory, uploaders: new[] { uploader });

        (await sut.ValidateBackupAsync(historyId)).Should().BeFalse();
    }

    // ----- DeleteBackupAsync -----

    [Fact]
    public async Task DeleteBackupAsync_NotFound_IsNoOp()
    {
        var factory = CreateFactory(nameof(DeleteBackupAsync_NotFound_IsNoOp));
        var sut = CreateSut(factory);

        var act = async () => await sut.DeleteBackupAsync(999);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteBackupAsync_Success_RemovesRowRegardlessOfUploaderOutcome()
    {
        var factory = CreateFactory(nameof(DeleteBackupAsync_Success_RemovesRowRegardlessOfUploaderOutcome));
        var provider = await SeedProviderAsync(factory, "P", BackupProviderType.Local);
        int historyId;
        await using (var db = factory.CreateDbContext())
        {
            var h = new BackupHistory { BackupProviderId = provider.Id, FileName = "f" };
            db.BackupHistory.Add(h);
            await db.SaveChangesAsync();
            historyId = h.Id;
        }
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.DeleteAsync(Arg.Any<BackupHistory>()).Returns(false); // remote delete "failed" but DB row still removed
        var sut = CreateSut(factory, uploaders: new[] { uploader });

        await sut.DeleteBackupAsync(historyId);

        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.BackupHistory.AnyAsync(h => h.Id == historyId)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteBackupAsync_UploaderThrows_RethrowsAndMarksHistoryFailedWithoutRemoving()
    {
        var factory = CreateFactory(nameof(DeleteBackupAsync_UploaderThrows_RethrowsAndMarksHistoryFailedWithoutRemoving));
        var provider = await SeedProviderAsync(factory, "P", BackupProviderType.Local);
        int historyId;
        await using (var db = factory.CreateDbContext())
        {
            var h = new BackupHistory { BackupProviderId = provider.Id, FileName = "f" };
            db.BackupHistory.Add(h);
            await db.SaveChangesAsync();
            historyId = h.Id;
        }
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.DeleteAsync(Arg.Any<BackupHistory>()).Returns<bool>(_ => throw new IOException("disk error"));
        var sut = CreateSut(factory, uploaders: new[] { uploader });

        var act = async () => await sut.DeleteBackupAsync(historyId);

        await act.Should().ThrowAsync<IOException>();

        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.BackupHistory.AnyAsync(h => h.Id == historyId)).Should().BeTrue();
    }

    // ----- CleanupBackupsByProviderSettingsAsync -----

    [Fact]
    public async Task CleanupBackupsByProviderSettingsAsync_RemovesOldEntriesAcrossAllRetentionTypes()
    {
        // Regression test: the method's XML doc said "Weekly and monthly handled
        // analogously" but the implementation only ever processed
        // BackupRetentionType.Daily - Weekly/Monthly rows were never cleaned up by this
        // method regardless of age. All three types are now processed, each with its own
        // progressively longer cutoff (same reasoning as CleanupOldBackupsAsync).
        var factory = CreateFactory(nameof(CleanupBackupsByProviderSettingsAsync_RemovesOldEntriesAcrossAllRetentionTypes));
        var provider = await SeedProviderAsync(factory, "P", BackupProviderType.Local);
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.BackupSettings.Add(new LagersystemLVHome.Domain.Models.BackupSettings { RetentionDays = 30 });
            db.BackupHistory.AddRange(
                new BackupHistory { BackupProviderId = provider.Id, FileName = "daily-old", RetentionType = BackupRetentionType.Daily, BackupDate = now.AddDays(-40) },
                new BackupHistory { BackupProviderId = provider.Id, FileName = "daily-recent", RetentionType = BackupRetentionType.Daily, BackupDate = now.AddDays(-1) },
                new BackupHistory { BackupProviderId = provider.Id, FileName = "weekly-ancient", RetentionType = BackupRetentionType.Weekly, BackupDate = now.AddDays(-400) },
                new BackupHistory { BackupProviderId = provider.Id, FileName = "weekly-recent", RetentionType = BackupRetentionType.Weekly, BackupDate = now },
                new BackupHistory { BackupProviderId = provider.Id, FileName = "monthly-ancient", RetentionType = BackupRetentionType.Monthly, BackupDate = now.AddYears(-5) },
                new BackupHistory { BackupProviderId = provider.Id, FileName = "monthly-recent", RetentionType = BackupRetentionType.Monthly, BackupDate = now });
            await db.SaveChangesAsync();
        }
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.DeleteAsync(Arg.Any<BackupHistory>()).Returns(true);
        var sut = CreateSut(factory, uploaders: new[] { uploader });

        await sut.CleanupBackupsByProviderSettingsAsync();

        await using var verifyDb = factory.CreateDbContext();
        var remaining = await verifyDb.BackupHistory.Select(h => h.FileName).ToListAsync();
        remaining.Should().BeEquivalentTo(new[] { "daily-recent", "weekly-recent", "monthly-recent" });
        await uploader.Received(1).DeleteAsync(Arg.Is<BackupHistory>(h => h.FileName == "daily-old"));
        await uploader.Received(1).DeleteAsync(Arg.Is<BackupHistory>(h => h.FileName == "weekly-ancient"));
        await uploader.Received(1).DeleteAsync(Arg.Is<BackupHistory>(h => h.FileName == "monthly-ancient"));
    }

    [Fact]
    public async Task CleanupBackupsByProviderSettingsAsync_UploaderDeleteThrows_RowIsKeptForRetry()
    {
        // Regression test: RemoveRange used to run unconditionally after the per-item
        // try/catch, so a failed remote delete still purged the DB row - "forgetting" a
        // backup that may still exist at the provider. A thrown exception now leaves the
        // row in place so it is retried on the next cleanup run.
        var factory = CreateFactory(nameof(CleanupBackupsByProviderSettingsAsync_UploaderDeleteThrows_RowIsKeptForRetry));
        var provider = await SeedProviderAsync(factory, "P", BackupProviderType.Local);
        await using (var db = factory.CreateDbContext())
        {
            db.BackupSettings.Add(new LagersystemLVHome.Domain.Models.BackupSettings { RetentionDays = 30 });
            db.BackupHistory.Add(new BackupHistory { BackupProviderId = provider.Id, FileName = "daily-old", RetentionType = BackupRetentionType.Daily, BackupDate = DateTime.UtcNow.AddDays(-40) });
            await db.SaveChangesAsync();
        }
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.DeleteAsync(Arg.Any<BackupHistory>()).Returns<bool>(_ => throw new IOException("remote unreachable"));
        var sut = CreateSut(factory, uploaders: new[] { uploader });

        var act = async () => await sut.CleanupBackupsByProviderSettingsAsync();

        await act.Should().NotThrowAsync();
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.BackupHistory.AnyAsync(h => h.FileName == "daily-old")).Should().BeTrue("a failed remote delete must not silently forget the backup in the DB");
    }

    // ----- CreateBackupAsync -----

    private static async Task SeedSettingsAsync(IDbContextFactory<InventoryDbContext> factory, LagersystemLVHome.Domain.Models.BackupSettings settings)
    {
        await using var db = factory.CreateDbContext();
        db.BackupSettings.Add(settings);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateBackupAsync_Disabled_ReturnsFailureImmediately()
    {
        var factory = CreateFactory(nameof(CreateBackupAsync_Disabled_ReturnsFailureImmediately));
        await SeedSettingsAsync(factory, new LagersystemLVHome.Domain.Models.BackupSettings { Enabled = false });
        var sut = CreateSut(factory);

        var result = await sut.CreateBackupAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Backup is disabled");
    }

    [Fact]
    public async Task CreateBackupAsync_NoEnabledProviders_JsonBackupRunsButOverallResultIsFailure()
    {
        var factory = CreateFactory(nameof(CreateBackupAsync_NoEnabledProviders_JsonBackupRunsButOverallResultIsFailure));
        await SeedSettingsAsync(factory, new LagersystemLVHome.Domain.Models.BackupSettings { Enabled = true, EncryptBackups = false, VerifyBackups = false });
        var sut = CreateSut(factory);

        var result = await sut.CreateBackupAsync();

        result.Success.Should().BeFalse();
        result.OriginalSizeBytes.Should().BeGreaterThan(0);
        result.IsCompressed.Should().BeTrue();
        result.SuccessfulProviders.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBackupAsync_SingleEnabledProvider_UploadsAndMarksOverallSuccess()
    {
        var factory = CreateFactory(nameof(CreateBackupAsync_SingleEnabledProvider_UploadsAndMarksOverallSuccess));
        await SeedSettingsAsync(factory, new LagersystemLVHome.Domain.Models.BackupSettings { Enabled = true, EncryptBackups = false, VerifyBackups = false });
        var provider = await SeedProviderAsync(factory, "Local1", BackupProviderType.Local, configuration: "");
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.UploadAsync(Arg.Any<BackupProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var sut = CreateSut(factory, uploaders: new[] { uploader });

        var result = await sut.CreateBackupAsync();

        result.Success.Should().BeTrue();
        result.SuccessfulProviders.Should().Equal("Local1");
        result.CreatedBackupHistoryIds.Should().ContainSingle();

        await using var db = factory.CreateDbContext();
        var history = await db.BackupHistory.SingleAsync();
        history.Status.Should().Be(BackupStatus.Success);
        history.RetentionType.Should().BeOneOf(BackupRetentionType.Daily, BackupRetentionType.Weekly, BackupRetentionType.Monthly);
    }

    [Fact]
    public async Task CreateBackupAsync_OneProviderThrowsAnotherSucceeds_PartialFailureButOverallSuccess()
    {
        var factory = CreateFactory(nameof(CreateBackupAsync_OneProviderThrowsAnotherSucceeds_PartialFailureButOverallSuccess));
        await SeedSettingsAsync(factory, new LagersystemLVHome.Domain.Models.BackupSettings { Enabled = true, EncryptBackups = false, VerifyBackups = false });
        await SeedProviderAsync(factory, "Good", BackupProviderType.Local, configuration: "");
        await SeedProviderAsync(factory, "Bad", BackupProviderType.NetworkShare, configuration: "");

        var goodUploader = CreateUploader(BackupProviderType.Local);
        goodUploader.UploadAsync(Arg.Any<BackupProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var badUploader = CreateUploader(BackupProviderType.NetworkShare);
        badUploader.UploadAsync(Arg.Any<BackupProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new IOException("network down"));

        var sut = CreateSut(factory, uploaders: new[] { goodUploader, badUploader });

        var result = await sut.CreateBackupAsync();

        result.Success.Should().BeTrue();
        result.SuccessfulProviders.Should().Equal("Good");
        result.FailedProviders.Should().Equal("Bad");
    }

    [Fact]
    public async Task CreateBackupAsync_DecryptConfigFails_ProviderMarkedFailedHistoryRecordsError()
    {
        var factory = CreateFactory(nameof(CreateBackupAsync_DecryptConfigFails_ProviderMarkedFailedHistoryRecordsError));
        await SeedSettingsAsync(factory, new LagersystemLVHome.Domain.Models.BackupSettings { Enabled = true, EncryptBackups = false, VerifyBackups = false });
        await SeedProviderAsync(factory, "Broken", BackupProviderType.Local, configuration: "cipher-text");
        var secureConfig = Substitute.For<ISecureConfigurationService>();
        secureConfig.Decrypt(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("cannot decrypt"));
        var uploader = CreateUploader(BackupProviderType.Local);
        var sut = CreateSut(factory, uploaders: new[] { uploader }, secureConfig: secureConfig);

        var result = await sut.CreateBackupAsync();

        result.Success.Should().BeFalse();
        result.FailedProviders.Should().Equal("Broken");
        await uploader.DidNotReceiveWithAnyArgs().UploadAsync(default!, default!, default);

        await using var db = factory.CreateDbContext();
        var history = await db.BackupHistory.SingleAsync();
        history.Status.Should().Be(BackupStatus.Failed);
        history.ErrorMessage.Should().Contain("configuration is encrypted");
    }

    [Fact]
    public async Task CreateBackupAsync_EncryptionEnabledWithPassword_ProducesDecryptableEncryptedFile()
    {
        var factory = CreateFactory(nameof(CreateBackupAsync_EncryptionEnabledWithPassword_ProducesDecryptableEncryptedFile));
        await SeedSettingsAsync(factory, new LagersystemLVHome.Domain.Models.BackupSettings
        {
            Enabled = true,
            EncryptBackups = true,
            EncryptionPassword = "s3cret!",
            VerifyBackups = false
        });
        await SeedProviderAsync(factory, "Local1", BackupProviderType.Local, configuration: "");

        byte[]? capturedBytes = null;
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.UploadAsync(Arg.Any<BackupProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                var path = ci.ArgAt<string>(1);
                capturedBytes = await File.ReadAllBytesAsync(path);
                return true;
            });
        var sut = CreateSut(factory, uploaders: new[] { uploader });

        var result = await sut.CreateBackupAsync();

        result.Success.Should().BeTrue();
        result.IsEncrypted.Should().BeTrue();
        result.FileName.Should().EndWith(".zip.enc");
        capturedBytes.Should().NotBeNull();

        // Replicate the SUT's own key derivation (SHA256 of password) + IV-prefixed AES-CBC
        // scheme to prove the encrypted bytes genuinely decrypt back to a valid ZIP.
        using var sha256 = SHA256.Create();
        var key = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes("s3cret!"));
        var iv = capturedBytes![..16];
        var cipherBody = capturedBytes[16..];

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plain = decryptor.TransformFinalBlock(cipherBody, 0, cipherBody.Length);

        // ZIP local file header signature.
        plain[0].Should().Be((byte)'P');
        plain[1].Should().Be((byte)'K');
    }

    [Fact]
    public async Task CreateBackupAsync_EncryptionEnabledButPasswordEmpty_LeavesBackupUnencrypted()
    {
        var factory = CreateFactory(nameof(CreateBackupAsync_EncryptionEnabledButPasswordEmpty_LeavesBackupUnencrypted));
        await SeedSettingsAsync(factory, new LagersystemLVHome.Domain.Models.BackupSettings
        {
            Enabled = true,
            EncryptBackups = true,
            EncryptionPassword = "",
            VerifyBackups = false
        });
        await SeedProviderAsync(factory, "Local1", BackupProviderType.Local, configuration: "");
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.UploadAsync(Arg.Any<BackupProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var sut = CreateSut(factory, uploaders: new[] { uploader });

        var result = await sut.CreateBackupAsync();

        result.Success.Should().BeTrue();
        result.IsEncrypted.Should().BeFalse();
        result.FileName.Should().EndWith(".zip");
    }

    [Fact]
    public async Task CreateBackupAsync_VerifyBackupsEnabled_ValidatesEachCreatedHistoryEntry()
    {
        var factory = CreateFactory(nameof(CreateBackupAsync_VerifyBackupsEnabled_ValidatesEachCreatedHistoryEntry));
        await SeedSettingsAsync(factory, new LagersystemLVHome.Domain.Models.BackupSettings { Enabled = true, EncryptBackups = false, VerifyBackups = true });
        await SeedProviderAsync(factory, "Local1", BackupProviderType.Local, configuration: "");
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.UploadAsync(Arg.Any<BackupProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        uploader.ValidateAsync(Arg.Any<BackupHistory>()).Returns(true);
        var sut = CreateSut(factory, uploaders: new[] { uploader });

        var result = await sut.CreateBackupAsync();

        result.Success.Should().BeTrue();
        result.ValidatedBackups.Should().Be(1);
        result.FailedValidations.Should().Be(0);
        await uploader.Received(1).ValidateAsync(Arg.Any<BackupHistory>());
    }

    [Fact]
    public async Task CreateBackupAsync_VerifyBackupsEnabled_ValidationFailure_CountsAsFailedValidation()
    {
        var factory = CreateFactory(nameof(CreateBackupAsync_VerifyBackupsEnabled_ValidationFailure_CountsAsFailedValidation));
        await SeedSettingsAsync(factory, new LagersystemLVHome.Domain.Models.BackupSettings { Enabled = true, EncryptBackups = false, VerifyBackups = true });
        await SeedProviderAsync(factory, "Local1", BackupProviderType.Local, configuration: "");
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.UploadAsync(Arg.Any<BackupProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        uploader.ValidateAsync(Arg.Any<BackupHistory>()).Returns(false);
        var sut = CreateSut(factory, uploaders: new[] { uploader });

        var result = await sut.CreateBackupAsync();

        result.ValidatedBackups.Should().Be(0);
        result.FailedValidations.Should().Be(1);
    }

    [Fact]
    public async Task CreateBackupAsync_EmailOnSuccess_SendsToEachRecipientAndSurvivesPerRecipientFailure()
    {
        var factory = CreateFactory(nameof(CreateBackupAsync_EmailOnSuccess_SendsToEachRecipientAndSurvivesPerRecipientFailure));
        await SeedSettingsAsync(factory, new LagersystemLVHome.Domain.Models.BackupSettings
        {
            Enabled = true,
            EncryptBackups = false,
            VerifyBackups = false,
            EmailOnSuccess = true,
            EmailRecipients = "good@x.local, bad@x.local"
        });
        await SeedProviderAsync(factory, "Local1", BackupProviderType.Local, configuration: "");
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.UploadAsync(Arg.Any<BackupProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var email = Substitute.For<IEmailService>();
        email.SendEmailAsync("bad@x.local", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("smtp down"));

        var sut = CreateSut(factory, uploaders: new[] { uploader }, emailService: email);

        var result = await sut.CreateBackupAsync();

        result.Success.Should().BeTrue();
        await email.Received(1).SendEmailAsync("good@x.local", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await email.Received(1).SendEmailAsync("bad@x.local", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateBackupAsync_EmailOnFailure_SendsFailureNotificationWithFailedSubject()
    {
        var factory = CreateFactory(nameof(CreateBackupAsync_EmailOnFailure_SendsFailureNotificationWithFailedSubject));
        await SeedSettingsAsync(factory, new LagersystemLVHome.Domain.Models.BackupSettings
        {
            Enabled = true,
            EncryptBackups = false,
            VerifyBackups = false,
            EmailOnFailure = true,
            EmailRecipients = "admin@x.local"
        });
        // No providers configured at all -> overall Success stays false, exercising the
        // "EmailOnFailure" branch (distinct from the EmailOnSuccess branch tested above).
        var email = Substitute.For<IEmailService>();
        var sut = CreateSut(factory, emailService: email);

        var result = await sut.CreateBackupAsync();

        result.Success.Should().BeFalse();
        await email.Received(1).SendEmailAsync(
            "admin@x.local",
            Arg.Is<string>(s => s.Contains("Fehlgeschlagen")),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateBackupAsync_VerifyBackupsAndEmailEnabled_NotificationIncludesValidationInfo()
    {
        var factory = CreateFactory(nameof(CreateBackupAsync_VerifyBackupsAndEmailEnabled_NotificationIncludesValidationInfo));
        await SeedSettingsAsync(factory, new LagersystemLVHome.Domain.Models.BackupSettings
        {
            Enabled = true,
            EncryptBackups = false,
            VerifyBackups = true,
            EmailOnSuccess = true,
            EmailRecipients = "admin@x.local"
        });
        await SeedProviderAsync(factory, "Local1", BackupProviderType.Local, configuration: "");
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.UploadAsync(Arg.Any<BackupProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        uploader.ValidateAsync(Arg.Any<BackupHistory>()).Returns(true);
        var email = Substitute.For<IEmailService>();
        var sut = CreateSut(factory, uploaders: new[] { uploader }, emailService: email);

        var result = await sut.CreateBackupAsync();

        result.Success.Should().BeTrue();
        result.ValidatedBackups.Should().Be(1);
        await email.Received(1).SendEmailAsync(
            "admin@x.local",
            Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("Validierung") && body.Contains("Erfolgreich validiert")),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateBackupAsync_NoEmailRecipientsConfigured_SendsNoEmail()
    {
        var factory = CreateFactory(nameof(CreateBackupAsync_NoEmailRecipientsConfigured_SendsNoEmail));
        await SeedSettingsAsync(factory, new LagersystemLVHome.Domain.Models.BackupSettings
        {
            Enabled = true,
            EncryptBackups = false,
            VerifyBackups = false,
            EmailOnSuccess = true,
            EmailRecipients = null
        });
        await SeedProviderAsync(factory, "Local1", BackupProviderType.Local, configuration: "");
        var uploader = CreateUploader(BackupProviderType.Local);
        uploader.UploadAsync(Arg.Any<BackupProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var email = Substitute.For<IEmailService>();
        var sut = CreateSut(factory, uploaders: new[] { uploader }, emailService: email);

        await sut.CreateBackupAsync();

        await email.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default!, default!, default, default);
    }

    [Fact]
    public async Task CreateBackupAsync_UnhandledExceptionAtStart_IsCaughtAndReturnsFailureResult()
    {
        var sut = CreateSut(new ThrowingContextFactory());

        var result = await sut.CreateBackupAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("db unavailable");
        result.EndTime.Should().NotBe(default);
    }
}
