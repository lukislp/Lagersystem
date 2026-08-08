using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace LagersystemLVHome.UnitTests.Services.Security;

/// <summary>
/// Covers <see cref="AuditService"/>: the thin wrapper that resolves the current user from
/// <see cref="IHttpContextAccessor"/>, delegates hash-chained persistence to a freshly
/// constructed <see cref="TamperProofAuditService"/> (see <c>TamperProofAuditServiceTests</c>
/// for that hashing logic itself), fires-and-forgets a gamification counter update, and
/// exposes the various <c>Log*Async</c> convenience wrappers plus read-side queries.
/// </summary>
public class AuditServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static IHttpContextAccessor CreateAccessor(HttpContext? context)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);
        return accessor;
    }

    private static HttpContext CreateAnonymousContext(string? forwardedFor = null, string? remoteIp = "203.0.113.5")
    {
        var ctx = new DefaultHttpContext();
        if (remoteIp != null)
            ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
        if (forwardedFor != null)
            ctx.Request.Headers["X-Forwarded-For"] = forwardedFor;
        return ctx;
    }

    private static HttpContext CreateAuthenticatedContext(int userId, string? remoteIp = "203.0.113.5")
    {
        var ctx = CreateAnonymousContext(remoteIp: remoteIp);
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], authenticationType: "TestAuth");
        ctx.User = new ClaimsPrincipal(identity);
        return ctx;
    }

    private static AuditService Build(
        IDbContextFactory<InventoryDbContext> factory, IHttpContextAccessor accessor, IGamificationService? gamification = null)
        => new(factory, accessor, NullLogger<AuditService>.Instance, NullLoggerFactory.Instance,
               gamification ?? Substitute.For<IGamificationService>());

    private static async Task<User> SeedUserAsync(IDbContextFactory<InventoryDbContext> factory, int id = 1, bool active = true, bool deleted = false)
    {
        await using var db = factory.CreateDbContext();
        if (!await db.Warehouses.AnyAsync(w => w.Id == 1))
            db.Warehouses.Add(new Warehouse { Id = 1, Name = "WH", Code = "T", IsActive = true });
        var user = new User
        {
            Id = id,
            Username = $"u{id}",
            Email = $"u{id}@test.local",
            DisplayName = $"u{id}",
            PasswordHash = "x",
            IsActive = active,
            IsDeleted = deleted,
            WarehouseId = 1
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // ---- LogAsync core behaviour --------------------------------------------------------

    [Fact]
    public async Task LogAsync_AnonymousContext_PersistsLogWithNullUser()
    {
        var factory = CreateFactory(nameof(LogAsync_AnonymousContext_PersistsLogWithNullUser));
        var sut = Build(factory, CreateAccessor(CreateAnonymousContext()));

        await sut.LogAsync("SOME_ACTION", "Widget", 42);

        await using var db = factory.CreateDbContext();
        var log = await db.AuditLogs.SingleAsync();
        log.Action.Should().Be("SOME_ACTION");
        // BUG: AuditService.LogAsync passes `entity` through to TamperProofAuditService as
        // `entityType`, which only persists it to AuditLog.EntityType - AuditLog.Entity (the
        // field GetEntityLogsAsync actually filters on) is left at its default "" for every
        // row written through the normal logging path. See
        // GetEntityLogsAsync_RealisticallyLoggedRow_NeverMatchesBecauseEntityFieldIsNeverSet
        // below for the resulting broken query behaviour.
        log.EntityType.Should().Be("Widget");
        log.Entity.Should().BeEmpty("known bug: AuditLog.Entity is never populated by AuditService.LogAsync");
        log.EntityId.Should().Be(42);
        log.UserId.Should().BeNull("user id 0 (no logged-in user) must be stored as NULL, not 0");
    }

    [Fact]
    public async Task LogAsync_AuthenticatedUser_ResolvesUserIdFromClaimsAndPersists()
    {
        var factory = CreateFactory(nameof(LogAsync_AuthenticatedUser_ResolvesUserIdFromClaimsAndPersists));
        var user = await SeedUserAsync(factory, id: 7);
        var sut = Build(factory, CreateAccessor(CreateAuthenticatedContext(user.Id)));

        await sut.LogAsync("PROFILE_UPDATED", "User", user.Id);

        await using var db = factory.CreateDbContext();
        var log = await db.AuditLogs.SingleAsync();
        log.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task LogAsync_AuthenticatedButInactiveUser_TreatsAsAnonymous()
    {
        var factory = CreateFactory(nameof(LogAsync_AuthenticatedButInactiveUser_TreatsAsAnonymous));
        var user = await SeedUserAsync(factory, id: 8, active: false);
        var sut = Build(factory, CreateAccessor(CreateAuthenticatedContext(user.Id)));

        await sut.LogAsync("SOME_ACTION", "User", user.Id);

        await using var db = factory.CreateDbContext();
        (await db.AuditLogs.SingleAsync()).UserId.Should().BeNull();
    }

    [Fact]
    public async Task LogAsync_SerializesDetailsAsJsonChanges()
    {
        var factory = CreateFactory(nameof(LogAsync_SerializesDetailsAsJsonChanges));
        var sut = Build(factory, CreateAccessor(CreateAnonymousContext()));

        await sut.LogAsync("PRODUCT_UPDATED", "Product", 1, new { Name = "Widget", Qty = 5 });

        await using var db = factory.CreateDbContext();
        var log = await db.AuditLogs.SingleAsync();
        log.Changes.Should().Contain("Widget").And.Contain("5");
    }

    [Fact]
    public async Task LogAsync_NullDetails_StoresNullChanges()
    {
        var factory = CreateFactory(nameof(LogAsync_NullDetails_StoresNullChanges));
        var sut = Build(factory, CreateAccessor(CreateAnonymousContext()));

        await sut.LogAsync("LOGOUT", "User", 1);

        await using var db = factory.CreateDbContext();
        (await db.AuditLogs.SingleAsync()).Changes.Should().BeNull();
    }

    [Fact]
    public async Task LogAsync_ReadsIpFromXForwardedForHeaderBeforeRemoteAddress()
    {
        var factory = CreateFactory(nameof(LogAsync_ReadsIpFromXForwardedForHeaderBeforeRemoteAddress));
        var sut = Build(factory, CreateAccessor(CreateAnonymousContext(forwardedFor: "198.51.100.7, 10.0.0.1", remoteIp: "10.0.0.99")));

        await sut.LogAsync("SOME_ACTION", "Widget");

        await using var db = factory.CreateDbContext();
        (await db.AuditLogs.SingleAsync()).IpAddress.Should().Be("198.51.100.7");
    }

    [Fact]
    public async Task LogAsync_WithoutHttpContext_UsesNullIpAndStillPersists()
    {
        var factory = CreateFactory(nameof(LogAsync_WithoutHttpContext_UsesNullIpAndStillPersists));
        var sut = Build(factory, CreateAccessor(null));

        await sut.LogAsync("SOME_ACTION", "Widget");

        await using var db = factory.CreateDbContext();
        var log = await db.AuditLogs.SingleAsync();
        log.IpAddress.Should().BeNull();
    }

    /// <summary>
    /// SECURITY BUG: <see cref="AuditService.LogAsync"/> accepts a <c>severity</c> parameter
    /// and every <c>Log*Async</c> convenience wrapper (e.g. <c>LogUserDeletedAsync</c>,
    /// account-lockout logging, GDPR deletion logging, ...) passes a meaningful, non-default
    /// severity into it - but <c>TamperProofAuditService.CreateTamperProofAuditLogAsync</c>,
    /// which actually persists the row, has NO severity parameter at all. The severity the
    /// caller specified is silently discarded and every audit log is stored with
    /// <see cref="AuditSeverity.Info"/> regardless of what was requested.
    /// <para/>
    /// Impact: <see cref="AuditService.GetSecurityEventsAsync"/> filters on
    /// <c>Severity &gt;= Warning OR Action.Contains("FAILED"/"REJECTED"/"DELETED")</c>. Since
    /// Severity is always Info, any Warning/Critical event whose action string doesn't happen
    /// to contain one of those three keywords (e.g. "LOGIN_BLOCKED", "ACCOUNT_LOCKED",
    /// "IP_ACCESS_DENIED", "2FA_LOCKED_DUE_TO_FAILED_ATTEMPTS" - wait, that one *does* match
    /// "FAILED" - but "LOGIN_BLOCKED", "ACCOUNT_LOCKED", "GDPR_ACCOUNT_DELETION" do not
    /// reliably contain those substrings) silently never surfaces on the security-events
    /// dashboard. This is a fail-open gap in security monitoring, not just a cosmetic issue.
    /// This test documents the current (broken) persisted behaviour so a future fix is
    /// caught by a red test instead of silently passing.
    /// </summary>
    [Fact]
    public async Task LogAsync_Severity_KnownBug_IsSilentlyDiscardedAndAlwaysPersistedAsInfo()
    {
        var factory = CreateFactory(nameof(LogAsync_Severity_KnownBug_IsSilentlyDiscardedAndAlwaysPersistedAsInfo));
        var sut = Build(factory, CreateAccessor(CreateAnonymousContext()));

        await sut.LogAsync("PRODUCT_DELETED", "Product", 1, null, AuditSeverity.Critical);

        await using var db = factory.CreateDbContext();
        (await db.AuditLogs.SingleAsync()).Severity.Should().Be(
            AuditSeverity.Info,
            "known bug: TamperProofAuditService.CreateTamperProofAuditLogAsync has no severity " +
            "parameter, so the severity requested by every AuditService caller is discarded");
    }

    /// <summary>
    /// LogAsync must never let an audit failure surface to the caller - a broken context
    /// factory must be swallowed (logged, not thrown) so that audit logging can never take
    /// down the calling business operation.
    /// </summary>
    [Fact]
    public async Task LogAsync_WhenContextFactoryThrows_SwallowsExceptionAndDoesNotThrow()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db down")));
        var sut = Build(throwingFactory, CreateAccessor(CreateAnonymousContext()));

        var act = async () => await sut.LogAsync("SOME_ACTION", "Widget");

        await act.Should().NotThrowAsync("audit errors must not crash the calling operation");
    }

    // ---- Gamification side-effect (fire-and-forget) --------------------------------------

    [Fact]
    public async Task LogAsync_AuthenticatedUser_RecordsGamificationAction()
    {
        var factory = CreateFactory(nameof(LogAsync_AuthenticatedUser_RecordsGamificationAction));
        var user = await SeedUserAsync(factory, id: 3);
        var gamification = Substitute.For<IGamificationService>();
        var tcs = new TaskCompletionSource();
        gamification.RecordActionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { tcs.TrySetResult(); return Task.CompletedTask; });
        var sut = Build(factory, CreateAccessor(CreateAuthenticatedContext(user.Id)), gamification);

        await sut.LogAsync("STOCK_MOVEMENT", "Product", 1);
        await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));

        await gamification.Received(1).RecordActionAsync(user.Id, "STOCK_MOVEMENT", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogAsync_AnonymousUserLoginEvent_FallsBackToEntityIdForGamification()
    {
        // During login the user isn't in HttpContext yet, so LogAsync falls back to
        // entityId when entity == "User" so the gamification counter still attributes
        // correctly to the logging-in user.
        var factory = CreateFactory(nameof(LogAsync_AnonymousUserLoginEvent_FallsBackToEntityIdForGamification));
        var gamification = Substitute.For<IGamificationService>();
        var tcs = new TaskCompletionSource();
        gamification.RecordActionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { tcs.TrySetResult(); return Task.CompletedTask; });
        var sut = Build(factory, CreateAccessor(CreateAnonymousContext()), gamification);

        await sut.LogAsync("LOGIN_SUCCESS", "User", 55);
        await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));

        await gamification.Received(1).RecordActionAsync(55, "LOGIN_SUCCESS", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogAsync_AnonymousNonUserEvent_DoesNotRecordGamification()
    {
        var factory = CreateFactory(nameof(LogAsync_AnonymousNonUserEvent_DoesNotRecordGamification));
        var gamification = Substitute.For<IGamificationService>();
        var sut = Build(factory, CreateAccessor(CreateAnonymousContext()), gamification);

        await sut.LogAsync("DATA_EXPORT", "Product", null);
        await Task.Delay(200); // give the fire-and-forget task a chance to run if it (incorrectly) fired

        await gamification.DidNotReceiveWithAnyArgs().RecordActionAsync(default, default!, default, default);
    }

    [Fact]
    public async Task LogAsync_GamificationThrows_DoesNotPropagateOrCrash()
    {
        var factory = CreateFactory(nameof(LogAsync_GamificationThrows_DoesNotPropagateOrCrash));
        var user = await SeedUserAsync(factory, id: 4);
        var gamification = Substitute.For<IGamificationService>();
        gamification.RecordActionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("gamification down")));
        var sut = Build(factory, CreateAccessor(CreateAuthenticatedContext(user.Id)), gamification);

        var act = async () => await sut.LogAsync("SOME_ACTION", "Product", 1);

        await act.Should().NotThrowAsync();
    }

    // ---- Convenience Log*Async wrappers ---------------------------------------------------

    [Theory]
    [InlineData(true, "LOGIN_SUCCESS")]
    [InlineData(false, "LOGIN_FAILED")]
    public async Task LogLoginAsync_MapsSuccessToAction(bool success, string expectedAction)
    {
        var factory = CreateFactory($"{nameof(LogLoginAsync_MapsSuccessToAction)}_{success}");
        var sut = Build(factory, CreateAccessor(CreateAnonymousContext()));

        await sut.LogLoginAsync(1, success, reason: success ? null : "bad password");

        await using var db = factory.CreateDbContext();
        var log = await db.AuditLogs.SingleAsync();
        log.Action.Should().Be(expectedAction);
        // NOTE: not asserting Severity here - see the "known bug" severity test above;
        // LOGIN_FAILED is requested with AuditSeverity.Warning but is always persisted as Info.
        log.EntityType.Should().Be("User", "EntityType is the field TamperProofAuditService actually persists (see Entity/EntityType bug above)");
        log.EntityId.Should().Be(1);
    }

    [Fact]
    public async Task LogImportAsync_WithErrors_RequestsWarningSeverity_ButItIsDiscardedByTheKnownBug()
    {
        var factory = CreateFactory(nameof(LogImportAsync_WithErrors_RequestsWarningSeverity_ButItIsDiscardedByTheKnownBug));
        var sut = Build(factory, CreateAccessor(CreateAnonymousContext()));

        await sut.LogImportAsync("CSV", "Product", 10, 8, 2);

        await using var db = factory.CreateDbContext();
        // Would be AuditSeverity.Warning if the severity-persistence bug documented above were fixed.
        (await db.AuditLogs.SingleAsync()).Severity.Should().Be(AuditSeverity.Info);
    }

    [Fact]
    public async Task LogImportAsync_WithoutErrors_UsesInfoSeverity()
    {
        var factory = CreateFactory(nameof(LogImportAsync_WithoutErrors_UsesInfoSeverity));
        var sut = Build(factory, CreateAccessor(CreateAnonymousContext()));

        await sut.LogImportAsync("CSV", "Product", 10, 10, 0);

        await using var db = factory.CreateDbContext();
        (await db.AuditLogs.SingleAsync()).Severity.Should().Be(AuditSeverity.Info);
    }

    [Fact]
    public async Task LogGdprAccountDeletionAsync_PersistsActionAndReason()
    {
        var factory = CreateFactory(nameof(LogGdprAccountDeletionAsync_PersistsActionAndReason));
        var sut = Build(factory, CreateAccessor(CreateAnonymousContext()));

        await sut.LogGdprAccountDeletionAsync(1, "user requested erasure");

        await using var db = factory.CreateDbContext();
        var log = await db.AuditLogs.SingleAsync();
        log.Action.Should().Be("GDPR_ACCOUNT_DELETION");
        // Requested as AuditSeverity.Warning by LogGdprAccountDeletionAsync, but silently
        // discarded - see the dedicated severity-bug test above.
        log.Changes.Should().Contain("user requested erasure");
    }

    // ---- Query methods ---------------------------------------------------------------------

    [Fact]
    public async Task GetRecentLogsAsync_ReturnsNewestFirstLimitedByCount()
    {
        var factory = CreateFactory(nameof(GetRecentLogsAsync_ReturnsNewestFirstLimitedByCount));
        await using (var db = factory.CreateDbContext())
        {
            for (var i = 0; i < 5; i++)
                db.AuditLogs.Add(new AuditLog { Action = $"A{i}", Entity = "X", Timestamp = DateTime.UtcNow.AddMinutes(-i) });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory, CreateAccessor(null));

        var logs = await sut.GetRecentLogsAsync(count: 3);

        logs.Should().HaveCount(3);
        logs[0].Action.Should().Be("A0", "most recent (smallest negative offset) must be first");
    }

    [Fact]
    public async Task GetUserLogsAsync_FiltersByUserId()
    {
        var factory = CreateFactory(nameof(GetUserLogsAsync_FiltersByUserId));
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLogs.Add(new AuditLog { Action = "A", Entity = "X", UserId = 1, Timestamp = DateTime.UtcNow });
            db.AuditLogs.Add(new AuditLog { Action = "B", Entity = "X", UserId = 2, Timestamp = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory, CreateAccessor(null));

        var logs = await sut.GetUserLogsAsync(1);

        logs.Should().ContainSingle().Which.UserId.Should().Be(1);
    }

    [Fact]
    public async Task GetEntityLogsAsync_FiltersByEntityAndEntityId()
    {
        // This verifies the query logic itself using rows that have Entity set directly.
        // It does NOT reflect what happens for logs written through the normal
        // AuditService.LogAsync path - see the companion "known bug" test below, which shows
        // that path never populates Entity at all, so this query is unreachable in practice.
        var factory = CreateFactory(nameof(GetEntityLogsAsync_FiltersByEntityAndEntityId));
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLogs.Add(new AuditLog { Action = "A", Entity = "Product", EntityId = 1, Timestamp = DateTime.UtcNow });
            db.AuditLogs.Add(new AuditLog { Action = "B", Entity = "Product", EntityId = 2, Timestamp = DateTime.UtcNow });
            db.AuditLogs.Add(new AuditLog { Action = "C", Entity = "Category", EntityId = 1, Timestamp = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory, CreateAccessor(null));

        var logs = await sut.GetEntityLogsAsync("Product", 1);

        logs.Should().ContainSingle().Which.Action.Should().Be("A");
    }

    /// <summary>
    /// BUG: demonstrates that <see cref="AuditService.GetEntityLogsAsync"/> is unreachable for
    /// any row created through the normal <see cref="AuditService.LogAsync"/> path. LogAsync
    /// hands the caller's <c>entity</c> string to TamperProofAuditService as <c>entityType</c>,
    /// which persists only <see cref="AuditLog.EntityType"/> - <see cref="AuditLog.Entity"/>
    /// (the field this query filters on) is left at its default empty string. This means the
    /// per-entity audit history feature (e.g. "show all audit events for Product #42") returns
    /// nothing for real-world data, even though matching rows exist in the table.
    /// </summary>
    [Fact]
    public async Task GetEntityLogsAsync_RealisticallyLoggedRow_NeverMatchesBecauseEntityFieldIsNeverSet()
    {
        var factory = CreateFactory(nameof(GetEntityLogsAsync_RealisticallyLoggedRow_NeverMatchesBecauseEntityFieldIsNeverSet));
        var sut = Build(factory, CreateAccessor(CreateAnonymousContext()));

        await sut.LogProductCreatedAsync(productId: 42, productName: "Widget");

        await using var verify = factory.CreateDbContext();
        (await verify.AuditLogs.CountAsync()).Should().Be(1, "the row must genuinely have been written");

        var logs = await sut.GetEntityLogsAsync("Product", 42);

        logs.Should().BeEmpty("known bug: GetEntityLogsAsync filters on AuditLog.Entity, which LogAsync never populates");
    }

    [Fact]
    public async Task GetActionStatisticsAsync_GroupsByActionAndRespectsDateRange()
    {
        var factory = CreateFactory(nameof(GetActionStatisticsAsync_GroupsByActionAndRespectsDateRange));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLogs.Add(new AuditLog { Action = "LOGIN", Entity = "User", Timestamp = now.AddDays(-10) });
            db.AuditLogs.Add(new AuditLog { Action = "LOGIN", Entity = "User", Timestamp = now.AddDays(-1) });
            db.AuditLogs.Add(new AuditLog { Action = "LOGOUT", Entity = "User", Timestamp = now.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory, CreateAccessor(null));

        var stats = await sut.GetActionStatisticsAsync(from: now.AddDays(-2));

        stats.Should().ContainKey("LOGIN").WhoseValue.Should().Be(1, "the 10-day-old LOGIN falls outside the 'from' filter");
        stats.Should().ContainKey("LOGOUT").WhoseValue.Should().Be(1);
    }

    [Theory]
    [InlineData("LOGIN_FAILED", AuditSeverity.Info)]
    [InlineData("USER_REJECTED", AuditSeverity.Info)]
    [InlineData("PRODUCT_DELETED", AuditSeverity.Info)]
    [InlineData("NORMAL_ACTION", AuditSeverity.Warning)]
    public async Task GetSecurityEventsAsync_IncludesWarningsAndKeywordMatchesOnly(string action, AuditSeverity severity)
    {
        // Every InlineData row here is expected to be INCLUDED (either its Action contains
        // a flagged keyword, or its Severity is >= Warning) except the mismatched combination
        // used below in the companion "excluded" test.
        var factory = CreateFactory($"{nameof(GetSecurityEventsAsync_IncludesWarningsAndKeywordMatchesOnly)}_{action}_{severity}");
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLogs.Add(new AuditLog { Action = action, Entity = "X", Severity = severity, Timestamp = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory, CreateAccessor(null));

        var events = await sut.GetSecurityEventsAsync();

        events.Should().ContainSingle();
    }

    [Fact]
    public async Task GetSecurityEventsAsync_ExcludesInfoSeverityWithoutFlaggedKeyword()
    {
        var factory = CreateFactory(nameof(GetSecurityEventsAsync_ExcludesInfoSeverityWithoutFlaggedKeyword));
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLogs.Add(new AuditLog { Action = "PRODUCT_CREATED", Entity = "Product", Severity = AuditSeverity.Info, Timestamp = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory, CreateAccessor(null));

        (await sut.GetSecurityEventsAsync()).Should().BeEmpty();
    }

    // ---- VerifyIntegrityAsync ------------------------------------------------------------

    [Fact]
    public async Task VerifyIntegrityAsync_CleanChain_ReturnsValid()
    {
        var factory = CreateFactory(nameof(VerifyIntegrityAsync_CleanChain_ReturnsValid));
        var sut = Build(factory, CreateAccessor(CreateAnonymousContext()));
        await sut.LogAsync("A", "X");
        await sut.LogAsync("B", "X");

        var result = await sut.VerifyIntegrityAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyIntegrityAsync_TamperedEntry_DetectsMismatch()
    {
        var factory = CreateFactory(nameof(VerifyIntegrityAsync_TamperedEntry_DetectsMismatch));
        var sut = Build(factory, CreateAccessor(CreateAnonymousContext()));
        await sut.LogAsync("A", "X");

        await using (var db = factory.CreateDbContext())
        {
            var log = await db.AuditLogs.SingleAsync();
            log.Action = "TAMPERED";
            await db.SaveChangesAsync();
        }

        var result = await sut.VerifyIntegrityAsync();

        result.IsValid.Should().BeFalse();
        result.InvalidLogs.Should().ContainSingle();
    }

    [Fact]
    public async Task VerifyIntegrityAsync_WhenContextFactoryThrows_ReturnsInvalidResultInsteadOfThrowing()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InventoryDbContext>(new InvalidOperationException("db down")));
        var sut = Build(throwingFactory, CreateAccessor(null));

        var result = await sut.VerifyIntegrityAsync();

        result.IsValid.Should().BeFalse("a verification failure must fail closed, not silently report success");
        result.InvalidLogs.Should().ContainSingle().Which.Reason.Should().Contain("db down");
    }
}
