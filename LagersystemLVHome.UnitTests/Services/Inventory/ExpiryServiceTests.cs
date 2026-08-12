using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Inventory;

public class ExpiryServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static (ExpiryService sut, IDbContextFactory<InventoryDbContext> factory) CreateSut(string dbName)
    {
        var factory = new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(dbName).Options);
        var notifier = Substitute.For<INotificationService>();
        return (new ExpiryService(factory, notifier, NullLogger<ExpiryService>.Instance), factory);
    }

    private static Product MakeProduct(string name, DateTime? expiry, int warehouseId = 1)
        => new()
        {
            Name = name,
            WarehouseId = warehouseId,
            CategoryId = 1,
            TrackExpiryDate = true,
            ExpiryDate = expiry,
            Quantity = 10
        };

    private static async Task SeedCategoryAsync(IDbContextFactory<InventoryDbContext> factory)
    {
        await using var db = factory.CreateDbContext();
        if (await db.Categories.AnyAsync(c => c.Id == 1)) return;
        db.Categories.Add(new Category { Id = 1, Name = "Misc" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetExpiringProductsAsync_ReturnsOnlyUpcomingWithinThreshold()
    {
        var (sut, factory) = CreateSut(nameof(GetExpiringProductsAsync_ReturnsOnlyUpcomingWithinThreshold));
        await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.AddRange(
                MakeProduct("soon", DateTime.UtcNow.AddDays(3)),
                MakeProduct("later", DateTime.UtcNow.AddDays(30)),
                MakeProduct("expired", DateTime.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
        }

        var list = await sut.GetExpiringProductsAsync(1, daysThreshold: 7);

        list.Should().ContainSingle().Which.Name.Should().Be("soon");
    }

    [Fact]
    public async Task GetExpiredProductsAsync_ReturnsPastExpiry()
    {
        var (sut, factory) = CreateSut(nameof(GetExpiredProductsAsync_ReturnsPastExpiry));
        await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.AddRange(
                MakeProduct("expired", DateTime.UtcNow.AddDays(-5)),
                MakeProduct("fresh", DateTime.UtcNow.AddDays(5)));
            await db.SaveChangesAsync();
        }

        var list = await sut.GetExpiredProductsAsync(1);

        list.Should().ContainSingle().Which.Name.Should().Be("expired");
    }

    [Fact]
    public async Task GetExpiredProductsAsync_IgnoresNonTracked()
    {
        var (sut, factory) = CreateSut(nameof(GetExpiredProductsAsync_IgnoresNonTracked));
        await using (var db = factory.CreateDbContext())
        {
            var p = MakeProduct("expired", DateTime.UtcNow.AddDays(-5));
            p.TrackExpiryDate = false;
            db.Products.Add(p);
            await db.SaveChangesAsync();
        }

        (await sut.GetExpiredProductsAsync(1)).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldTrackExpiryForCategoryAsync_RecognizesFoodKeywords()
    {
        var (sut, factory) = CreateSut(nameof(ShouldTrackExpiryForCategoryAsync_RecognizesFoodKeywords));
        await using (var db = factory.CreateDbContext())
        {
            db.Categories.AddRange(
                new Category { Id = 1, Name = "Lebensmittel" },
                new Category { Id = 2, Name = "Food & Drink" },
                new Category { Id = 3, Name = "Electronics" });
            await db.SaveChangesAsync();
        }

        (await sut.ShouldTrackExpiryForCategoryAsync(1)).Should().BeTrue();
        (await sut.ShouldTrackExpiryForCategoryAsync(2)).Should().BeTrue();
        (await sut.ShouldTrackExpiryForCategoryAsync(3)).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldTrackExpiryForCategoryAsync_UnknownCategory_ReturnsFalse()
    {
        var (sut, _) = CreateSut(nameof(ShouldTrackExpiryForCategoryAsync_UnknownCategory_ReturnsFalse));

        (await sut.ShouldTrackExpiryForCategoryAsync(999)).Should().BeFalse();
    }

    [Fact]
    public async Task GetExpiringBatchesCountAsync_CountsWithinThreshold()
    {
        var (sut, factory) = CreateSut(nameof(GetExpiringBatchesCountAsync_CountsWithinThreshold));
        await using (var db = factory.CreateDbContext())
        {
            db.ProductBatches.AddRange(
                new ProductBatch { BatchNumber = "B1", WarehouseId = 1, Quantity = 3, ExpiryDate = DateTime.UtcNow.AddDays(2) },
                new ProductBatch { BatchNumber = "B2", WarehouseId = 1, Quantity = 5, ExpiryDate = DateTime.UtcNow.AddDays(20) },
                new ProductBatch { BatchNumber = "B3", WarehouseId = 1, Quantity = 0, ExpiryDate = DateTime.UtcNow.AddDays(2) },
                new ProductBatch { BatchNumber = "B4", WarehouseId = 2, Quantity = 1, ExpiryDate = DateTime.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }

        (await sut.GetExpiringBatchesCountAsync(1, daysThreshold: 7)).Should().Be(1);
    }

    [Fact]
    public async Task MarkBatchAsDisposedAsync_UnknownBatch_ReturnsNotFound()
    {
        var (sut, _) = CreateSut(nameof(MarkBatchAsDisposedAsync_UnknownBatch_ReturnsNotFound));

        var r = await sut.MarkBatchAsDisposedAsync(999);

        r.ErrorCode.Should().Be("batch.notfound");
    }

    [Fact]
    public async Task MarkBatchAsDisposedAsync_AlreadyEmpty_ReturnsAlreadyDisposed()
    {
        var (sut, factory) = CreateSut(nameof(MarkBatchAsDisposedAsync_AlreadyEmpty_ReturnsAlreadyDisposed));
        ProductBatch batch;
        await using (var db = factory.CreateDbContext())
        {
            batch = new ProductBatch { BatchNumber = "B1", WarehouseId = 1, Quantity = 0, ProductId = 1 };
            db.ProductBatches.Add(batch);
            await db.SaveChangesAsync();
        }

        var r = await sut.MarkBatchAsDisposedAsync(batch.Id);

        r.ErrorCode.Should().Be("batch.alreadydisposed");
    }

    [Fact]
    public async Task MarkBatchAsDisposedAsync_SuccessPath_ZeroesBatchAndCreatesMovement()
    {
        var (sut, factory) = CreateSut(nameof(MarkBatchAsDisposedAsync_SuccessPath_ZeroesBatchAndCreatesMovement));
        ProductBatch batch;
        Product product;
        await using (var db = factory.CreateDbContext())
        {
            product = new Product { Name = "P", WarehouseId = 1, Quantity = 20 };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            batch = new ProductBatch
            {
                BatchNumber = "B1",
                WarehouseId = 1,
                ProductId = product.Id,
                Quantity = 5,
                ExpiryDate = DateTime.UtcNow.AddDays(-1)
            };
            db.ProductBatches.Add(batch);
            await db.SaveChangesAsync();
        }

        var r = await sut.MarkBatchAsDisposedAsync(batch.Id, notes: "expired");

        r.IsSuccess.Should().BeTrue();
        await using var verify = factory.CreateDbContext();
        (await verify.ProductBatches.FindAsync(batch.Id))!.Quantity.Should().Be(0);
        (await verify.Products.FindAsync(product.Id))!.Quantity.Should().Be(15);
        var movement = await verify.StockMovements.SingleAsync();
        movement.QuantityChange.Should().Be(-5);
        movement.Type.Should().Be(MovementType.Disposal);
        movement.Notes.Should().Be("expired");
    }

    [Fact]
    public async Task GetBatchesForProductAsync_OrdersByExpiry()
    {
        var (sut, factory) = CreateSut(nameof(GetBatchesForProductAsync_OrdersByExpiry));
        await using (var db = factory.CreateDbContext())
        {
            db.ProductBatches.AddRange(
                new ProductBatch { BatchNumber = "late", ProductId = 1, WarehouseId = 1, Quantity = 1, ExpiryDate = DateTime.UtcNow.AddDays(10) },
                new ProductBatch { BatchNumber = "soon", ProductId = 1, WarehouseId = 1, Quantity = 1, ExpiryDate = DateTime.UtcNow.AddDays(1) },
                new ProductBatch { BatchNumber = "other", ProductId = 2, WarehouseId = 1, Quantity = 1, ExpiryDate = DateTime.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }

        var list = await sut.GetBatchesForProductAsync(1);

        list.Select(b => b.BatchNumber).Should().ContainInOrder("soon", "late");
    }

    [Fact]
    public async Task GetNextExpiringBatchForProductAsync_ReturnsEarliestNonEmpty()
    {
        var (sut, factory) = CreateSut(nameof(GetNextExpiringBatchForProductAsync_ReturnsEarliestNonEmpty));
        await using (var db = factory.CreateDbContext())
        {
            db.ProductBatches.AddRange(
                new ProductBatch { BatchNumber = "empty", ProductId = 1, WarehouseId = 1, Quantity = 0, ExpiryDate = DateTime.UtcNow.AddDays(1) },
                new ProductBatch { BatchNumber = "earliest", ProductId = 1, WarehouseId = 1, Quantity = 3, ExpiryDate = DateTime.UtcNow.AddDays(2) },
                new ProductBatch { BatchNumber = "later", ProductId = 1, WarehouseId = 1, Quantity = 3, ExpiryDate = DateTime.UtcNow.AddDays(5) });
            await db.SaveChangesAsync();
        }

        var batch = await sut.GetNextExpiringBatchForProductAsync(1);

        batch!.BatchNumber.Should().Be("earliest");
    }

    // ---- Remaining batch queries ----

    [Fact]
    public async Task GetExpiringBatchesAsync_ReturnsWithinThresholdOnly()
    {
        var (sut, factory) = CreateSut(nameof(GetExpiringBatchesAsync_ReturnsWithinThresholdOnly));
        await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.Add(new Product { Id = 1, Name = "P1", WarehouseId = 1, CategoryId = 1 });
            await db.SaveChangesAsync();
            db.ProductBatches.AddRange(
                new ProductBatch { BatchNumber = "soon", ProductId = 1, WarehouseId = 1, Quantity = 2, ExpiryDate = DateTime.UtcNow.AddDays(2) },
                new ProductBatch { BatchNumber = "far", ProductId = 1, WarehouseId = 1, Quantity = 2, ExpiryDate = DateTime.UtcNow.AddDays(30) },
                new ProductBatch { BatchNumber = "expired", ProductId = 1, WarehouseId = 1, Quantity = 2, ExpiryDate = DateTime.UtcNow.AddDays(-1) },
                new ProductBatch { BatchNumber = "empty", ProductId = 1, WarehouseId = 1, Quantity = 0, ExpiryDate = DateTime.UtcNow.AddDays(2) },
                new ProductBatch { BatchNumber = "otherWarehouse", ProductId = 1, WarehouseId = 2, Quantity = 2, ExpiryDate = DateTime.UtcNow.AddDays(2) });
            await db.SaveChangesAsync();
        }

        var list = await sut.GetExpiringBatchesAsync(1, daysThreshold: 7);

        list.Should().ContainSingle().Which.BatchNumber.Should().Be("soon");
    }

    [Fact]
    public async Task GetExpiringBatchesAsync_CanceledToken_ReturnsEmptyList()
    {
        var (sut, _) = CreateSut(nameof(GetExpiringBatchesAsync_CanceledToken_ReturnsEmptyList));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        (await sut.GetExpiringBatchesAsync(1, cancellationToken: cts.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllNonEmptyBatchesWithExpiryAsync_FiltersEmptyAndNullExpiry()
    {
        var (sut, factory) = CreateSut(nameof(GetAllNonEmptyBatchesWithExpiryAsync_FiltersEmptyAndNullExpiry));
        await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.Add(new Product { Id = 1, Name = "P1", WarehouseId = 1, CategoryId = 1 });
            await db.SaveChangesAsync();
            db.ProductBatches.AddRange(
                new ProductBatch { BatchNumber = "withExpiry", ProductId = 1, WarehouseId = 1, Quantity = 2, ExpiryDate = DateTime.UtcNow.AddDays(2) },
                new ProductBatch { BatchNumber = "noExpiry", ProductId = 1, WarehouseId = 1, Quantity = 2, ExpiryDate = null },
                new ProductBatch { BatchNumber = "empty", ProductId = 1, WarehouseId = 1, Quantity = 0, ExpiryDate = DateTime.UtcNow.AddDays(2) });
            await db.SaveChangesAsync();
        }

        var list = await sut.GetAllNonEmptyBatchesWithExpiryAsync(1);

        list.Should().ContainSingle().Which.BatchNumber.Should().Be("withExpiry");
    }

    [Fact]
    public async Task GetAllNonEmptyBatchesWithExpiryAsync_CanceledToken_ReturnsEmptyList()
    {
        var (sut, _) = CreateSut(nameof(GetAllNonEmptyBatchesWithExpiryAsync_CanceledToken_ReturnsEmptyList));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        (await sut.GetAllNonEmptyBatchesWithExpiryAsync(1, cts.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetExpiredBatchesAsync_ReturnsPastExpiryNonEmptyOnly()
    {
        var (sut, factory) = CreateSut(nameof(GetExpiredBatchesAsync_ReturnsPastExpiryNonEmptyOnly));
        await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.Add(new Product { Id = 1, Name = "P1", WarehouseId = 1, CategoryId = 1 });
            await db.SaveChangesAsync();
            db.ProductBatches.AddRange(
                new ProductBatch { BatchNumber = "expired", ProductId = 1, WarehouseId = 1, Quantity = 2, ExpiryDate = DateTime.UtcNow.AddDays(-3) },
                new ProductBatch { BatchNumber = "expiredButEmpty", ProductId = 1, WarehouseId = 1, Quantity = 0, ExpiryDate = DateTime.UtcNow.AddDays(-3) },
                new ProductBatch { BatchNumber = "fresh", ProductId = 1, WarehouseId = 1, Quantity = 2, ExpiryDate = DateTime.UtcNow.AddDays(3) });
            await db.SaveChangesAsync();
        }

        var list = await sut.GetExpiredBatchesAsync(1);

        list.Should().ContainSingle().Which.BatchNumber.Should().Be("expired");
    }

    [Fact]
    public async Task GetExpiredBatchesAsync_CanceledToken_ReturnsEmptyList()
    {
        var (sut, _) = CreateSut(nameof(GetExpiredBatchesAsync_CanceledToken_ReturnsEmptyList));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        (await sut.GetExpiredBatchesAsync(1, cts.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetExpiringBatchesCountAsync_CanceledToken_ReturnsZero()
    {
        var (sut, _) = CreateSut(nameof(GetExpiringBatchesCountAsync_CanceledToken_ReturnsZero));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        (await sut.GetExpiringBatchesCountAsync(1, cancellationToken: cts.Token)).Should().Be(0);
    }

    [Fact]
    public async Task GetBatchesForProductAsync_CanceledToken_ReturnsEmptyList()
    {
        var (sut, _) = CreateSut(nameof(GetBatchesForProductAsync_CanceledToken_ReturnsEmptyList));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        (await sut.GetBatchesForProductAsync(1, cts.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetNextExpiringBatchForProductAsync_CanceledToken_ReturnsNull()
    {
        var (sut, _) = CreateSut(nameof(GetNextExpiringBatchForProductAsync_CanceledToken_ReturnsNull));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        (await sut.GetNextExpiringBatchForProductAsync(1, cts.Token)).Should().BeNull();
    }

    [Fact]
    public async Task MarkBatchAsDisposedAsync_CanceledToken_ReturnsFailure()
    {
        var (sut, _) = CreateSut(nameof(MarkBatchAsDisposedAsync_CanceledToken_ReturnsFailure));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var r = await sut.MarkBatchAsDisposedAsync(1, cancellationToken: cts.Token);

        r.ErrorCode.Should().Be("batch.disposefailed");
    }

    // ---- Exception paths on the simple query methods (pre-canceled token forces the try/catch) ----

    [Fact]
    public async Task GetExpiringProductsAsync_CanceledToken_ReturnsEmptyList()
    {
        var (sut, _) = CreateSut(nameof(GetExpiringProductsAsync_CanceledToken_ReturnsEmptyList));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        (await sut.GetExpiringProductsAsync(1, cancellationToken: cts.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetExpiredProductsAsync_CanceledToken_ReturnsEmptyList()
    {
        var (sut, _) = CreateSut(nameof(GetExpiredProductsAsync_CanceledToken_ReturnsEmptyList));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        (await sut.GetExpiredProductsAsync(1, cts.Token)).Should().BeEmpty();
    }

    // Note: ShouldTrackExpiryForCategoryAsync's catch block cannot be reliably exercised via a
    // pre-canceled CancellationToken - its internal `context.Categories.FindAsync(categoryId)`
    // call doesn't forward the token, and CreateDbContextAsync itself doesn't observe
    // cancellation for the InMemory provider (confirmed empirically: ProcessScannerMovementAsync's
    // equivalent test only throws once flow reaches a call that explicitly forwards the token,
    // e.g. SaveChangesAsync/ToListAsync/FirstOrDefaultAsync). Left undocumented-by-test; see
    // final coverage report for this specific gap.

    // ---- CheckExpiryAndNotifyAsync (drives the four private Notify* helpers) ----

    private static User MakeManager(int id, int warehouseId = 1, UserRole role = UserRole.Admin, bool isActive = true)
        => new()
        {
            Id = id,
            Username = $"u{id}",
            Email = $"u{id}@x.local",
            PasswordHash = "x",
            WarehouseId = warehouseId,
            Role = role,
            IsActive = isActive
        };

    [Fact]
    public async Task CheckExpiryAndNotifyAsync_NotifiesAdminsForExpiredAndExpiringSoonProduct()
    {
        var (_, factory) = CreateSut(nameof(CheckExpiryAndNotifyAsync_NotifiesAdminsForExpiredAndExpiringSoonProduct));
        var notifier = Substitute.For<INotificationService>();
        var svc = new ExpiryService(factory, notifier, NullLogger<ExpiryService>.Instance);
        await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.AddRange(
                MakeProduct("expired", DateTime.UtcNow.AddDays(-1)),
                MakeProduct("expiringSoon", DateTime.UtcNow.AddDays(3)));
            db.Users.AddRange(
                MakeManager(1, role: UserRole.Admin),
                MakeManager(2, role: UserRole.User), // not notified: plain user
                MakeManager(3, role: UserRole.Manager, isActive: false)); // not notified: inactive
            await db.SaveChangesAsync();
        }

        await svc.CheckExpiryAndNotifyAsync();

        await notifier.Received(1).CreateNotificationAsync(
            1, NotificationType.CriticalStock, "PRODUKT ABGELAUFEN!", Arg.Any<string>(), Arg.Any<string>(), NotificationChannel.All);
        await notifier.Received(1).CreateNotificationAsync(
            1, NotificationType.LowStock, "Produkt läuft bald ab", Arg.Any<string>(), Arg.Any<string>());
        // "any args" matching would ignore the userId we care about; pin it explicitly
        // and use Arg.Any for the rest so calls for user 1 don't accidentally match too.
        await notifier.DidNotReceive().CreateNotificationAsync(
            2, Arg.Any<NotificationType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<NotificationChannel>());
        await notifier.DidNotReceive().CreateNotificationAsync(
            3, Arg.Any<NotificationType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<NotificationChannel>());
    }

    [Fact]
    public async Task CheckExpiryAndNotifyAsync_SkipsProductNotification_WhenRecentDuplicateExists()
    {
        var (_, factory) = CreateSut(nameof(CheckExpiryAndNotifyAsync_SkipsProductNotification_WhenRecentDuplicateExists));
        var notifier = Substitute.For<INotificationService>();
        var svc = new ExpiryService(factory, notifier, NullLogger<ExpiryService>.Instance);
        await SeedCategoryAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            db.Products.Add(MakeProduct("dupe", DateTime.UtcNow.AddDays(-1)));
            db.Users.Add(MakeManager(1));
            db.Notifications.Add(new LagersystemLVHome.Domain.Models.Notification
            {
                UserId = 1,
                Type = NotificationType.CriticalStock,
                Message = "Das Produkt 'dupe' ist seit dem ... abgelaufen!",
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }

        await svc.CheckExpiryAndNotifyAsync();

        await notifier.DidNotReceiveWithAnyArgs().CreateNotificationAsync(default, default, default!, default!);
    }

    [Fact]
    public async Task CheckExpiryAndNotifyAsync_NotifiesAdminsForExpiredAndExpiringSoonBatch()
    {
        var (_, factory) = CreateSut(nameof(CheckExpiryAndNotifyAsync_NotifiesAdminsForExpiredAndExpiringSoonBatch));
        var notifier = Substitute.For<INotificationService>();
        var svc = new ExpiryService(factory, notifier, NullLogger<ExpiryService>.Instance);
        await SeedCategoryAsync(factory);
        Product product;
        await using (var db = factory.CreateDbContext())
        {
            product = new Product { Name = "BatchedProduct", WarehouseId = 1, CategoryId = 1, Quantity = 5 };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            db.ProductBatches.AddRange(
                new ProductBatch { ProductId = product.Id, BatchNumber = "B-expired", WarehouseId = 1, Quantity = 2, ExpiryDate = DateTime.UtcNow.AddDays(-2) },
                new ProductBatch { ProductId = product.Id, BatchNumber = "B-soon", WarehouseId = 1, Quantity = 2, ExpiryDate = DateTime.UtcNow.AddDays(3) });
            db.Users.Add(MakeManager(1, role: UserRole.SuperAdmin));
            await db.SaveChangesAsync();
        }

        await svc.CheckExpiryAndNotifyAsync();

        await notifier.Received(1).CreateNotificationAsync(
            1, NotificationType.CriticalStock, "CHARGE ABGELAUFEN!", Arg.Any<string>(), Arg.Any<string>(), NotificationChannel.All);
        await notifier.Received(1).CreateNotificationAsync(
            1, NotificationType.LowStock, "Charge läuft bald ab", Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CheckExpiryAndNotifyAsync_SkipsBatchNotification_WhenRecentDuplicateExists()
    {
        var (_, factory) = CreateSut(nameof(CheckExpiryAndNotifyAsync_SkipsBatchNotification_WhenRecentDuplicateExists));
        var notifier = Substitute.For<INotificationService>();
        var svc = new ExpiryService(factory, notifier, NullLogger<ExpiryService>.Instance);
        await SeedCategoryAsync(factory);
        Product product;
        await using (var db = factory.CreateDbContext())
        {
            product = new Product { Name = "BatchedProduct", WarehouseId = 1, CategoryId = 1, Quantity = 5 };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            db.ProductBatches.Add(new ProductBatch { ProductId = product.Id, BatchNumber = "B-dupe", WarehouseId = 1, Quantity = 2, ExpiryDate = DateTime.UtcNow.AddDays(-2) });
            db.Users.Add(MakeManager(1));
            db.Notifications.Add(new LagersystemLVHome.Domain.Models.Notification
            {
                UserId = 1,
                Type = NotificationType.CriticalStock,
                Message = "Charge 'B-dupe' von ...",
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }

        await svc.CheckExpiryAndNotifyAsync();

        await notifier.DidNotReceiveWithAnyArgs().CreateNotificationAsync(default, default, default!, default!);
    }

    [Fact]
    public async Task CheckExpiryAndNotifyAsync_CanceledToken_DoesNotThrow()
    {
        var (sut, _) = CreateSut(nameof(CheckExpiryAndNotifyAsync_CanceledToken_DoesNotThrow));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await sut.CheckExpiryAndNotifyAsync(cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckExpiryAndNotifyAsync_NoTrackedProductsOrBatches_CompletesWithoutNotifying()
    {
        var (_, factory) = CreateSut(nameof(CheckExpiryAndNotifyAsync_NoTrackedProductsOrBatches_CompletesWithoutNotifying));
        var notifier = Substitute.For<INotificationService>();
        var svc = new ExpiryService(factory, notifier, NullLogger<ExpiryService>.Instance);

        await svc.CheckExpiryAndNotifyAsync();

        await notifier.DidNotReceiveWithAnyArgs().CreateNotificationAsync(default, default, default!, default!);
    }
}
