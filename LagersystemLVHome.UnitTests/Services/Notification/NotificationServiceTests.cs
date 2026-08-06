using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace LagersystemLVHome.UnitTests.Services.Notification;

// The enclosing namespace segment "Notification" shadows the domain entity of the same name
// (the parent namespace's nested-namespace member wins over a using-alias declared outside
// this namespace body), so it must be aliased here, inside the namespace body, for
// unqualified use below.
using Notification = LagersystemLVHome.Domain.Models.Notification;

public class NotificationServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    // Used to exercise the outer try/catch blocks: CreateDbContextAsync always throws.
    private sealed class ThrowingContextFactory : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => throw new InvalidOperationException("db unavailable");

        public Task<InventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("db unavailable");
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static NotificationService Build(
        IDbContextFactory<InventoryDbContext> factory,
        IEmailService? email = null,
        ITeamsService? teams = null,
        NotificationChannels? channels = null,
        INotificationEventService? events = null)
        => new(
            factory,
            email ?? Substitute.For<IEmailService>(),
            teams ?? Substitute.For<ITeamsService>(),
            Options.Create(channels ?? new NotificationChannels()),
            NullLogger<NotificationService>.Instance,
            events ?? Substitute.For<INotificationEventService>());

    private static Warehouse MakeWarehouse(int id = 1) => new()
    {
        Id = id,
        Name = $"WH{id}",
        Address = "addr",
        Code = $"W{id}",
        IsActive = true
    };

    private static User MakeUser(
        int id,
        int warehouseId = 1,
        UserRole role = UserRole.User,
        bool isActive = true,
        string? email = null) => new()
        {
            Id = id,
            Username = $"u{id}",
            Email = email ?? $"u{id}@test.local",
            DisplayName = $"User {id}",
            PasswordHash = "x",
            WarehouseId = warehouseId,
            Role = role,
            IsActive = isActive
        };

    private static Product MakeProduct(int id = 1, int warehouseId = 1, int quantity = 1, string name = "Widget") => new()
    {
        Id = id,
        Name = name,
        WarehouseId = warehouseId,
        Quantity = quantity,
        Price = 1
    };

    // ==================== CreateNotificationAsync (public, 6-arg) ====================

    [Fact]
    public async Task CreateNotificationAsync_UserNotFound_DoesNothing()
    {
        var factory = CreateFactory(nameof(CreateNotificationAsync_UserNotFound_DoesNothing));
        var sut = Build(factory);

        await sut.CreateNotificationAsync(999, NotificationType.Info, "T", "M");

        await using var db = factory.CreateDbContext();
        (await db.Notifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateNotificationAsync_InAppChannel_SavesNotificationAndCreatesDefaultSettings()
    {
        var factory = CreateFactory(nameof(CreateNotificationAsync_InAppChannel_SavesNotificationAndCreatesDefaultSettings));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        await sut.CreateNotificationAsync(1, NotificationType.Info, "Title", "Message", "/x", NotificationChannel.InApp);

        await using var verifyDb = factory.CreateDbContext();
        var notification = await verifyDb.Notifications.SingleAsync();
        notification.Title.Should().Be("Title");
        notification.UserId.Should().Be(1);
        notification.IsRead.Should().BeFalse();
        (await verifyDb.UserNotificationSettings.CountAsync()).Should().Be(1, "GetUserSettingsAsync should create default settings");
    }

    [Fact]
    public async Task CreateNotificationAsync_InAppDisabledForType_DoesNotSaveNotification()
    {
        var factory = CreateFactory(nameof(CreateNotificationAsync_InAppDisabledForType_DoesNotSaveNotification));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, InAppLowStock = false });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        await sut.CreateNotificationAsync(1, NotificationType.LowStock, "Title", "Message", channel: NotificationChannel.InApp);

        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.Notifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateNotificationAsync_EmailChannel_SendsEmailWhenEnabled()
    {
        var factory = CreateFactory(nameof(CreateNotificationAsync_EmailChannel_SendsEmailWhenEnabled));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, email: "target@test.local"));
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, EmailLowStock = true });
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var sut = Build(factory, email: email);

        await sut.CreateNotificationAsync(1, NotificationType.LowStock, "Title", "Message", channel: NotificationChannel.Email);

        await email.Received(1).SendEmailAsync("target@test.local", "Title", "Message");
    }

    [Fact]
    public async Task CreateNotificationAsync_EmailDisabledForType_DoesNotSendEmail()
    {
        var factory = CreateFactory(nameof(CreateNotificationAsync_EmailDisabledForType_DoesNotSendEmail));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, EmailLowStock = false });
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var sut = Build(factory, email: email);

        await sut.CreateNotificationAsync(1, NotificationType.LowStock, "Title", "Message", channel: NotificationChannel.Email);

        await email.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default!, default!);
    }

    [Fact]
    public async Task CreateNotificationAsync_EmailThrows_IsSwallowedAndDoesNotPropagate()
    {
        var factory = CreateFactory(nameof(CreateNotificationAsync_EmailThrows_IsSwallowedAndDoesNotPropagate));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, EmailLowStock = true, InAppLowStock = true });
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        email.SendEmailAsync(default!, default!, default!).ThrowsForAnyArgs(new InvalidOperationException("smtp down"));
        var sut = Build(factory, email: email);

        var act = () => sut.CreateNotificationAsync(1, NotificationType.LowStock, "Title", "Message", channel: NotificationChannel.All);

        await act.Should().NotThrowAsync();
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.Notifications.CountAsync()).Should().Be(1, "in-app notification should still be saved despite email failure");
    }

    [Fact]
    public async Task CreateNotificationAsync_PushChannel_LogsSkippedAndDoesNotThrow()
    {
        var factory = CreateFactory(nameof(CreateNotificationAsync_PushChannel_LogsSkippedAndDoesNotThrow));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, PushLowStock = true });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var act = () => sut.CreateNotificationAsync(1, NotificationType.LowStock, "Title", "Message", channel: NotificationChannel.Push);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateNotificationAsync_PushDisabledForType_DoesNotThrow()
    {
        var factory = CreateFactory(nameof(CreateNotificationAsync_PushDisabledForType_DoesNotThrow));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, PushLowStock = false });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var act = () => sut.CreateNotificationAsync(1, NotificationType.LowStock, "Title", "Message", channel: NotificationChannel.Push);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(NotificationType.CriticalStock)]
    [InlineData(NotificationType.NewUser)]
    [InlineData(NotificationType.SecurityAlert)]
    [InlineData(NotificationType.SystemUpdate)]
    [InlineData(NotificationType.Info)]
    public async Task CreateNotificationAsync_AllChannel_RespectsShouldSendSwitchesForEachType(NotificationType type)
    {
        var factory = CreateFactory($"{nameof(CreateNotificationAsync_AllChannel_RespectsShouldSendSwitchesForEachType)}_{type}");
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var sut = Build(factory, email: email);

        var act = () => sut.CreateNotificationAsync(1, type, "Title", "Message");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateNotificationAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.CreateNotificationAsync(1, NotificationType.Info, "T", "M");

        await act.Should().NotThrowAsync();
    }

    // ==================== CreateLowStockNotificationAsync ====================

    [Fact]
    public async Task CreateLowStockNotificationAsync_QuantityInLowRange_CreatesNotificationForEligibleUsers()
    {
        var factory = CreateFactory(nameof(CreateLowStockNotificationAsync_QuantityInLowRange_CreatesNotificationForEligibleUsers));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.Admin));
            db.Users.Add(MakeUser(2, role: UserRole.User)); // wrong role: excluded
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, LowStockThreshold = 10, CriticalStockThreshold = 5 });
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 2, LowStockThreshold = 10, CriticalStockThreshold = 5 });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var product = MakeProduct(quantity: 7);

        await sut.CreateLowStockNotificationAsync(product);

        await using var verifyDb = factory.CreateDbContext();
        var notifications = await verifyDb.Notifications.ToListAsync();
        notifications.Should().ContainSingle().Which.UserId.Should().Be(1);
        notifications[0].Type.Should().Be(NotificationType.LowStock);
    }

    [Fact]
    public async Task CreateLowStockNotificationAsync_QuantityBelowCriticalThreshold_DoesNotCreateLowStockNotification()
    {
        var factory = CreateFactory(nameof(CreateLowStockNotificationAsync_QuantityBelowCriticalThreshold_DoesNotCreateLowStockNotification));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.Admin));
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, LowStockThreshold = 10, CriticalStockThreshold = 5 });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var product = MakeProduct(quantity: 2); // below critical threshold -> not "low stock" range

        await sut.CreateLowStockNotificationAsync(product);

        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.Notifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateLowStockNotificationAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());
        var product = MakeProduct();

        var act = () => sut.CreateLowStockNotificationAsync(product);

        await act.Should().NotThrowAsync();
    }

    // ==================== CreateCriticalStockNotificationAsync ====================

    [Fact]
    public async Task CreateCriticalStockNotificationAsync_QuantityAtOrBelowThreshold_CreatesNotification()
    {
        var factory = CreateFactory(nameof(CreateCriticalStockNotificationAsync_QuantityAtOrBelowThreshold_CreatesNotification));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.SuperAdmin));
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, CriticalStockThreshold = 5 });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var product = MakeProduct(quantity: 3);

        await sut.CreateCriticalStockNotificationAsync(product);

        await using var verifyDb = factory.CreateDbContext();
        var notification = await verifyDb.Notifications.SingleAsync();
        notification.Type.Should().Be(NotificationType.CriticalStock);
    }

    [Fact]
    public async Task CreateCriticalStockNotificationAsync_QuantityAboveThreshold_DoesNotCreateNotification()
    {
        var factory = CreateFactory(nameof(CreateCriticalStockNotificationAsync_QuantityAboveThreshold_DoesNotCreateNotification));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.SuperAdmin));
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, CriticalStockThreshold = 5 });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var product = MakeProduct(quantity: 20);

        await sut.CreateCriticalStockNotificationAsync(product);

        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.Notifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateCriticalStockNotificationAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());
        var product = MakeProduct();

        var act = () => sut.CreateCriticalStockNotificationAsync(product);

        await act.Should().NotThrowAsync();
    }

    // ==================== CreateNewUserNotificationAsync ====================

    [Fact]
    public async Task CreateNewUserNotificationAsync_NotifiesActiveAdminsInSameWarehouse_ExcludingNewUser()
    {
        var factory = CreateFactory(nameof(CreateNewUserNotificationAsync_NotifiesActiveAdminsInSameWarehouse_ExcludingNewUser));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.Admin));
            db.Users.Add(MakeUser(2, role: UserRole.SuperAdmin));
            db.Users.Add(MakeUser(3, role: UserRole.Admin, isActive: false)); // inactive: excluded
            db.Users.Add(MakeUser(4, role: UserRole.User)); // wrong role: excluded
            var newUser = MakeUser(5, role: UserRole.User);
            db.Users.Add(newUser);
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        await using var lookupDb = factory.CreateDbContext();
        var createdNewUser = await lookupDb.Users.FirstAsync(u => u.Id == 5);

        await sut.CreateNewUserNotificationAsync(createdNewUser);

        await using var verifyDb = factory.CreateDbContext();
        var notifications = await verifyDb.Notifications.ToListAsync();
        notifications.Select(n => n.UserId).Should().BeEquivalentTo(new[] { 1, 2 });
        notifications.Should().OnlyContain(n => n.Type == NotificationType.NewUser);
    }

    [Fact]
    public async Task CreateNewUserNotificationAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.CreateNewUserNotificationAsync(MakeUser(1));

        await act.Should().NotThrowAsync();
    }

    // ==================== CreateSecurityAlertAsync ====================

    [Fact]
    public async Task CreateSecurityAlertAsync_CreatesNotificationForUser()
    {
        var factory = CreateFactory(nameof(CreateSecurityAlertAsync_CreatesNotificationForUser));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        await sut.CreateSecurityAlertAsync(1, "Suspicious login detected");

        await using var verifyDb = factory.CreateDbContext();
        var notification = await verifyDb.Notifications.SingleAsync();
        notification.Type.Should().Be(NotificationType.SecurityAlert);
        notification.Message.Should().Be("Suspicious login detected");
    }

    // NOTE: CreateSecurityAlertAsync's try/catch wraps only a call to the private
    // CreateNotificationAsync(int, ...) overload, which itself catches and swallows every
    // exception internally and never rethrows (see NotificationService.cs lines 42-122).
    // As a result CreateSecurityAlertAsync's own catch block (LogSecurityAlertCreateError)
    // is unreachable dead code under normal operation - there is no way to make the inner
    // call throw from outside the class to exercise it. Flagged as a suspected bug/dead
    // code below in the final report; intentionally left uncovered here.

    // ==================== GetUserNotificationsAsync ====================

    [Fact]
    public async Task GetUserNotificationsAsync_UserNotFound_ReturnsEmptyList()
    {
        var factory = CreateFactory(nameof(GetUserNotificationsAsync_UserNotFound_ReturnsEmptyList));
        var sut = Build(factory);

        var result = await sut.GetUserNotificationsAsync(999);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserNotificationsAsync_RegularUser_ExcludesSecurityAlertsAndOtherWarehouses()
    {
        var factory = CreateFactory(nameof(GetUserNotificationsAsync_RegularUser_ExcludesSecurityAlertsAndOtherWarehouses));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Warehouses.Add(MakeWarehouse(2));
            db.Users.Add(MakeUser(1, warehouseId: 1, role: UserRole.User));
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, Title = "A", WarehouseId = 1, CreatedAt = DateTime.UtcNow });
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.SecurityAlert, Title = "B", WarehouseId = 1, CreatedAt = DateTime.UtcNow });
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, Title = "C", WarehouseId = 2, CreatedAt = DateTime.UtcNow });
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, Title = "D", WarehouseId = null, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var result = await sut.GetUserNotificationsAsync(1);

        result.Select(n => n.Title).Should().BeEquivalentTo(new[] { "A", "D" });
    }

    [Fact]
    public async Task GetUserNotificationsAsync_SuperAdmin_SeesEverything()
    {
        var factory = CreateFactory(nameof(GetUserNotificationsAsync_SuperAdmin_SeesEverything));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Warehouses.Add(MakeWarehouse(2));
            db.Users.Add(MakeUser(1, warehouseId: 1, role: UserRole.SuperAdmin));
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.SecurityAlert, Title = "A", WarehouseId = 2, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var result = await sut.GetUserNotificationsAsync(1);

        result.Should().ContainSingle().Which.Title.Should().Be("A");
    }

    [Fact]
    public async Task GetUserNotificationsAsync_UnreadOnly_FiltersReadNotifications()
    {
        var factory = CreateFactory(nameof(GetUserNotificationsAsync_UnreadOnly_FiltersReadNotifications));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, Title = "Read", IsRead = true, CreatedAt = DateTime.UtcNow });
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, Title = "Unread", IsRead = false, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var result = await sut.GetUserNotificationsAsync(1, unreadOnly: true);

        result.Should().ContainSingle().Which.Title.Should().Be("Unread");
    }

    [Fact]
    public async Task GetUserNotificationsAsync_RespectsLimit()
    {
        var factory = CreateFactory(nameof(GetUserNotificationsAsync_RespectsLimit));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            for (var i = 0; i < 5; i++)
            {
                db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, Title = $"N{i}", CreatedAt = DateTime.UtcNow.AddMinutes(i) });
            }
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var result = await sut.GetUserNotificationsAsync(1, limit: 2);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetUserNotificationsAsync_OuterException_ReturnsEmptyList()
    {
        var sut = Build(new ThrowingContextFactory());

        var result = await sut.GetUserNotificationsAsync(1);

        result.Should().BeEmpty();
    }

    // ==================== GetUnreadCountAsync ====================

    [Fact]
    public async Task GetUnreadCountAsync_UserNotFound_ReturnsZero()
    {
        var factory = CreateFactory(nameof(GetUnreadCountAsync_UserNotFound_ReturnsZero));
        var sut = Build(factory);

        var result = await sut.GetUnreadCountAsync(999);

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetUnreadCountAsync_RegularUser_ExcludesSecurityAlertsAndCountsUnreadOnly()
    {
        var factory = CreateFactory(nameof(GetUnreadCountAsync_RegularUser_ExcludesSecurityAlertsAndCountsUnreadOnly));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.User));
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, IsRead = false, WarehouseId = 1, CreatedAt = DateTime.UtcNow });
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, IsRead = true, WarehouseId = 1, CreatedAt = DateTime.UtcNow });
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.SecurityAlert, IsRead = false, WarehouseId = 1, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var result = await sut.GetUnreadCountAsync(1);

        result.Should().Be(1);
    }

    [Fact]
    public async Task GetUnreadCountAsync_SuperAdmin_CountsEverythingUnread()
    {
        var factory = CreateFactory(nameof(GetUnreadCountAsync_SuperAdmin_CountsEverythingUnread));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.SuperAdmin));
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.SecurityAlert, IsRead = false, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var result = await sut.GetUnreadCountAsync(1);

        result.Should().Be(1);
    }

    [Fact]
    public async Task GetUnreadCountAsync_OuterException_ReturnsZero()
    {
        var sut = Build(new ThrowingContextFactory());

        var result = await sut.GetUnreadCountAsync(1);

        result.Should().Be(0);
    }

    // ==================== MarkAsReadAsync ====================

    [Fact]
    public async Task MarkAsReadAsync_UnreadNotification_MarksReadAndRaisesEvent()
    {
        var factory = CreateFactory(nameof(MarkAsReadAsync_UnreadNotification_MarksReadAndRaisesEvent));
        int notificationId;
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            var notification = new Notification { UserId = 1, Type = NotificationType.Info, Title = "T", IsRead = false, CreatedAt = DateTime.UtcNow };
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
            notificationId = notification.Id;
        }
        var events = Substitute.For<INotificationEventService>();
        var sut = Build(factory, events: events);

        await sut.MarkAsReadAsync(notificationId);

        await using var verifyDb = factory.CreateDbContext();
        var updated = await verifyDb.Notifications.FindAsync(notificationId);
        updated!.IsRead.Should().BeTrue();
        updated.ReadAt.Should().NotBeNull();
        events.Received(1).NotifyChanged();
    }

    [Fact]
    public async Task MarkAsReadAsync_AlreadyRead_DoesNotRaiseEvent()
    {
        var factory = CreateFactory(nameof(MarkAsReadAsync_AlreadyRead_DoesNotRaiseEvent));
        int notificationId;
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            var notification = new Notification { UserId = 1, Type = NotificationType.Info, Title = "T", IsRead = true, CreatedAt = DateTime.UtcNow };
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
            notificationId = notification.Id;
        }
        var events = Substitute.For<INotificationEventService>();
        var sut = Build(factory, events: events);

        await sut.MarkAsReadAsync(notificationId);

        events.DidNotReceive().NotifyChanged();
    }

    [Fact]
    public async Task MarkAsReadAsync_NotificationNotFound_DoesNothing()
    {
        var factory = CreateFactory(nameof(MarkAsReadAsync_NotificationNotFound_DoesNothing));
        var events = Substitute.For<INotificationEventService>();
        var sut = Build(factory, events: events);

        var act = () => sut.MarkAsReadAsync(999);

        await act.Should().NotThrowAsync();
        events.DidNotReceive().NotifyChanged();
    }

    [Fact]
    public async Task MarkAsReadAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.MarkAsReadAsync(1);

        await act.Should().NotThrowAsync();
    }

    // ==================== MarkAllAsReadAsync ====================

    [Fact]
    public async Task MarkAllAsReadAsync_MarksAllUnreadAndRaisesEvent()
    {
        var factory = CreateFactory(nameof(MarkAllAsReadAsync_MarksAllUnreadAndRaisesEvent));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, IsRead = false, CreatedAt = DateTime.UtcNow });
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, IsRead = false, CreatedAt = DateTime.UtcNow });
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, IsRead = true, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var events = Substitute.For<INotificationEventService>();
        var sut = Build(factory, events: events);

        await sut.MarkAllAsReadAsync(1);

        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.Notifications.CountAsync(n => !n.IsRead)).Should().Be(0);
        events.Received(1).NotifyChanged();
    }

    [Fact]
    public async Task MarkAllAsReadAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.MarkAllAsReadAsync(1);

        await act.Should().NotThrowAsync();
    }

    // ==================== DeleteNotificationAsync ====================

    [Fact]
    public async Task DeleteNotificationAsync_Found_RemovesIt()
    {
        var factory = CreateFactory(nameof(DeleteNotificationAsync_Found_RemovesIt));
        int notificationId;
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            var notification = new Notification { UserId = 1, Type = NotificationType.Info, Title = "T", CreatedAt = DateTime.UtcNow };
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
            notificationId = notification.Id;
        }
        var sut = Build(factory);

        await sut.DeleteNotificationAsync(notificationId);

        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.Notifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteNotificationAsync_NotFound_DoesNothing()
    {
        var factory = CreateFactory(nameof(DeleteNotificationAsync_NotFound_DoesNothing));
        var sut = Build(factory);

        var act = () => sut.DeleteNotificationAsync(999);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteNotificationAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.DeleteNotificationAsync(1);

        await act.Should().NotThrowAsync();
    }

    // ==================== DeleteOldNotificationsAsync ====================

    [Fact]
    public async Task DeleteOldNotificationsAsync_RemovesOldReadNotifications_KeepsRecentOrUnread()
    {
        var factory = CreateFactory(nameof(DeleteOldNotificationsAsync_RemovesOldReadNotifications_KeepsRecentOrUnread));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1));
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, Title = "Old-Read", IsRead = true, CreatedAt = DateTime.UtcNow.AddDays(-40) });
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, Title = "Old-Unread", IsRead = false, CreatedAt = DateTime.UtcNow.AddDays(-40) });
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, Title = "Recent-Read", IsRead = true, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        await sut.DeleteOldNotificationsAsync(30);

        await using var verifyDb = factory.CreateDbContext();
        var remaining = await verifyDb.Notifications.Select(n => n.Title).ToListAsync();
        remaining.Should().BeEquivalentTo(new[] { "Old-Unread", "Recent-Read" });
    }

    [Fact]
    public async Task DeleteOldNotificationsAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.DeleteOldNotificationsAsync();

        await act.Should().NotThrowAsync();
    }

    // ==================== GetUserSettingsAsync / UpdateUserSettingsAsync ====================

    [Fact]
    public async Task GetUserSettingsAsync_NoExistingSettings_CreatesDefaults()
    {
        var factory = CreateFactory(nameof(GetUserSettingsAsync_NoExistingSettings_CreatesDefaults));
        var sut = Build(factory);

        var settings = await sut.GetUserSettingsAsync(1);

        settings.LowStockThreshold.Should().Be(10);
        settings.CriticalStockThreshold.Should().Be(5);
        settings.EmailLowStock.Should().BeTrue();

        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.UserNotificationSettings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetUserSettingsAsync_ExistingSettings_ReturnsThem()
    {
        var factory = CreateFactory(nameof(GetUserSettingsAsync_ExistingSettings_ReturnsThem));
        await using (var db = factory.CreateDbContext())
        {
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, LowStockThreshold = 99 });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        var settings = await sut.GetUserSettingsAsync(1);

        settings.LowStockThreshold.Should().Be(99);
    }

    [Fact]
    public async Task GetUserSettingsAsync_OuterException_ReturnsFallbackSettings()
    {
        var sut = Build(new ThrowingContextFactory());

        var settings = await sut.GetUserSettingsAsync(42);

        settings.UserId.Should().Be(42);
    }

    [Fact]
    public async Task UpdateUserSettingsAsync_UpdatesTimestampAndPersists()
    {
        var factory = CreateFactory(nameof(UpdateUserSettingsAsync_UpdatesTimestampAndPersists));
        await using (var db = factory.CreateDbContext())
        {
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, LowStockThreshold = 10 });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        UserNotificationSettings toUpdate;
        await using (var db = factory.CreateDbContext())
        {
            toUpdate = await db.UserNotificationSettings.SingleAsync(s => s.UserId == 1);
        }
        toUpdate.LowStockThreshold = 25;

        await sut.UpdateUserSettingsAsync(toUpdate);

        await using var verifyDb = factory.CreateDbContext();
        var updated = await verifyDb.UserNotificationSettings.SingleAsync(s => s.UserId == 1);
        updated.LowStockThreshold.Should().Be(25);
    }

    [Fact]
    public async Task UpdateUserSettingsAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.UpdateUserSettingsAsync(new UserNotificationSettings { UserId = 1 });

        await act.Should().NotThrowAsync();
    }

    // ==================== CheckLowStockAndNotifyAsync ====================

    [Fact]
    public async Task CheckLowStockAndNotifyAsync_CriticalQuantity_CreatesCriticalStockNotification()
    {
        var factory = CreateFactory(nameof(CheckLowStockAndNotifyAsync_CriticalQuantity_CreatesCriticalStockNotification));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.Admin));
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, LowStockThreshold = 10, CriticalStockThreshold = 5 });
            db.Products.Add(MakeProduct(quantity: 3, name: "CriticalWidget"));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        await sut.CheckLowStockAndNotifyAsync();

        await using var verifyDb = factory.CreateDbContext();
        var notification = await verifyDb.Notifications.SingleAsync();
        notification.Type.Should().Be(NotificationType.CriticalStock);
    }

    [Fact]
    public async Task CheckLowStockAndNotifyAsync_LowQuantity_CreatesLowStockNotification()
    {
        var factory = CreateFactory(nameof(CheckLowStockAndNotifyAsync_LowQuantity_CreatesLowStockNotification));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.Admin));
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, LowStockThreshold = 10, CriticalStockThreshold = 5 });
            db.Products.Add(MakeProduct(quantity: 8, name: "LowWidget"));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        await sut.CheckLowStockAndNotifyAsync();

        await using var verifyDb = factory.CreateDbContext();
        var notification = await verifyDb.Notifications.SingleAsync();
        notification.Type.Should().Be(NotificationType.LowStock);
    }

    [Fact]
    public async Task CheckLowStockAndNotifyAsync_SufficientQuantity_CreatesNoNotification()
    {
        var factory = CreateFactory(nameof(CheckLowStockAndNotifyAsync_SufficientQuantity_CreatesNoNotification));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Products.Add(MakeProduct(quantity: 500, name: "PlentyWidget"));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        await sut.CheckLowStockAndNotifyAsync();

        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.Notifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CheckLowStockAndNotifyAsync_RecentNotificationExists_SkipsDuplicate()
    {
        var factory = CreateFactory(nameof(CheckLowStockAndNotifyAsync_RecentNotificationExists_SkipsDuplicate));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.Admin));
            db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = 1, LowStockThreshold = 10, CriticalStockThreshold = 5 });
            db.Products.Add(MakeProduct(quantity: 3, name: "DupWidget"));
            db.Notifications.Add(new Notification
            {
                UserId = 1,
                Type = NotificationType.CriticalStock,
                Title = "Existing",
                ActionUrl = "/products?search=DupWidget",
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);

        await sut.CheckLowStockAndNotifyAsync();

        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.Notifications.CountAsync()).Should().Be(1, "no duplicate notification should be created within 24h");
    }

    [Fact]
    public async Task CheckLowStockAndNotifyAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.CheckLowStockAndNotifyAsync();

        await act.Should().NotThrowAsync();
    }

    // ==================== SendDailyDigestAsync ====================

    [Fact]
    public async Task SendDailyDigestAsync_UserWithUnreadNotifications_SendsDigestEmail()
    {
        var factory = CreateFactory(nameof(SendDailyDigestAsync_UserWithUnreadNotifications_SendsDigestEmail));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, email: "digest@test.local"));
            // DigestTime must match the current hour for the user to be picked up.
            db.UserNotificationSettings.Add(new UserNotificationSettings
            {
                UserId = 1,
                DailyDigest = true,
                DigestTime = new TimeSpan(DateTime.Now.Hour, 0, 0)
            });
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, Title = "N1", Message = "M1", IsRead = false, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var sut = Build(factory, email: email);

        await sut.SendDailyDigestAsync();

        await email.Received(1).SendEmailAsync(
            "digest@test.local",
            Arg.Any<string>(),
            Arg.Is<string>(m => m.Contains("N1")));
    }

    [Fact]
    public async Task SendDailyDigestAsync_UserWithNoUnreadNotifications_SkipsEmail()
    {
        var factory = CreateFactory(nameof(SendDailyDigestAsync_UserWithNoUnreadNotifications_SkipsEmail));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, email: "digest@test.local"));
            db.UserNotificationSettings.Add(new UserNotificationSettings
            {
                UserId = 1,
                DailyDigest = true,
                DigestTime = new TimeSpan(DateTime.Now.Hour, 0, 0)
            });
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var sut = Build(factory, email: email);

        await sut.SendDailyDigestAsync();

        await email.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default!, default!);
    }

    [Fact]
    public async Task SendDailyDigestAsync_DigestTimeDoesNotMatchCurrentHour_UserIsSkipped()
    {
        var factory = CreateFactory(nameof(SendDailyDigestAsync_DigestTimeDoesNotMatchCurrentHour_UserIsSkipped));
        var otherHour = (DateTime.Now.Hour + 1) % 24;
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, email: "digest@test.local"));
            db.UserNotificationSettings.Add(new UserNotificationSettings
            {
                UserId = 1,
                DailyDigest = true,
                DigestTime = new TimeSpan(otherHour, 0, 0)
            });
            db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, Title = "N1", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var sut = Build(factory, email: email);

        await sut.SendDailyDigestAsync();

        await email.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default!, default!);
    }

    [Fact]
    public async Task SendDailyDigestAsync_MoreThanTenUnread_TruncatesDigestToTen()
    {
        var factory = CreateFactory(nameof(SendDailyDigestAsync_MoreThanTenUnread_TruncatesDigestToTen));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, email: "digest@test.local"));
            db.UserNotificationSettings.Add(new UserNotificationSettings
            {
                UserId = 1,
                DailyDigest = true,
                DigestTime = new TimeSpan(DateTime.Now.Hour, 0, 0)
            });
            for (var i = 0; i < 15; i++)
            {
                db.Notifications.Add(new Notification { UserId = 1, Type = NotificationType.Info, Title = $"N{i}", IsRead = false, CreatedAt = DateTime.UtcNow });
            }
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var sut = Build(factory, email: email);

        await sut.SendDailyDigestAsync();

        await email.Received(1).SendEmailAsync(
            "digest@test.local",
            Arg.Any<string>(),
            Arg.Is<string>(m => m.Contains("15 ungelesene")));
    }

    [Fact]
    public async Task SendDailyDigestAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.SendDailyDigestAsync();

        await act.Should().NotThrowAsync();
    }

    // ==================== Push notification stubs ====================

    [Fact]
    public async Task SendPushNotificationAsync_NotConfigured_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(SendPushNotificationAsync_NotConfigured_ReturnsFalse));
        var sut = Build(factory);

        var result = await sut.SendPushNotificationAsync(1, "Title", "Body");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RequestPushPermissionAsync_NotConfigured_ReturnsTrue()
    {
        var factory = CreateFactory(nameof(RequestPushPermissionAsync_NotConfigured_ReturnsTrue));
        var sut = Build(factory);

        var result = await sut.RequestPushPermissionAsync(1, "subscription-data");

        result.Should().BeTrue();
    }

    // ==================== SendLowStockAlertAsync (multi-channel) ====================

    [Fact]
    public async Task SendLowStockAlertAsync_AllChannelsEnabled_NotifiesEmailAndTeams()
    {
        var factory = CreateFactory(nameof(SendLowStockAlertAsync_AllChannelsEnabled_NotifiesEmailAndTeams));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.SuperAdmin, email: "admin@test.local"));
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var teams = Substitute.For<ITeamsService>();
        var channels = new NotificationChannels();
        channels.LowStockAlerts.InApp = true;
        channels.LowStockAlerts.Email = true;
        channels.LowStockAlerts.Teams = true;
        var sut = Build(factory, email: email, teams: teams, channels: channels);

        await sut.SendLowStockAlertAsync("Widget", 2, 10, "Main Warehouse");

        await email.Received(1).SendEmailAsync(
            "admin@test.local",
            Arg.Is<string>(s => s.Contains("Niedriger Bestand")),
            Arg.Is<string>(b => b.Contains("Widget") && b.Contains("Main Warehouse")),
            isHtml: true);
        await teams.Received(1).SendLowStockAlertAsync("Widget", 2, 10, "Main Warehouse");
    }

    [Fact]
    public async Task SendLowStockAlertAsync_AllChannelsDisabled_NotifiesNothing()
    {
        var factory = CreateFactory(nameof(SendLowStockAlertAsync_AllChannelsDisabled_NotifiesNothing));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.SuperAdmin, email: "admin@test.local"));
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var teams = Substitute.For<ITeamsService>();
        var channels = new NotificationChannels();
        channels.LowStockAlerts.InApp = false;
        channels.LowStockAlerts.Email = false;
        channels.LowStockAlerts.Teams = false;
        var sut = Build(factory, email: email, teams: teams, channels: channels);

        await sut.SendLowStockAlertAsync("Widget", 2, 10);

        await email.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default!, default!, isHtml: default);
        await teams.DidNotReceiveWithAnyArgs().SendLowStockAlertAsync(default!, default, default);
    }

    [Fact]
    public async Task SendLowStockAlertAsync_EmailWithoutWarehouseName_OmitsWarehouseLine()
    {
        var factory = CreateFactory(nameof(SendLowStockAlertAsync_EmailWithoutWarehouseName_OmitsWarehouseLine));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.Admin, email: "admin@test.local"));
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var channels = new NotificationChannels();
        channels.LowStockAlerts.InApp = false;
        channels.LowStockAlerts.Email = true;
        channels.LowStockAlerts.Teams = false;
        var sut = Build(factory, email: email, channels: channels);

        await sut.SendLowStockAlertAsync("Widget", 2, 10);

        await email.Received(1).SendEmailAsync(
            "admin@test.local",
            Arg.Any<string>(),
            Arg.Is<string>(b => !b.Contains("Lager:")),
            isHtml: true);
    }

    [Fact]
    public async Task SendLowStockAlertAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.SendLowStockAlertAsync("Widget", 2, 10);

        await act.Should().NotThrowAsync();
    }

    // ==================== SendExpiryAlertAsync (multi-channel) ====================

    [Fact]
    public async Task SendExpiryAlertAsync_AllChannelsEnabled_NotifiesEmailAndTeams()
    {
        var factory = CreateFactory(nameof(SendExpiryAlertAsync_AllChannelsEnabled_NotifiesEmailAndTeams));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.Admin, email: "admin@test.local"));
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var teams = Substitute.For<ITeamsService>();
        var channels = new NotificationChannels();
        channels.ExpiryAlerts.InApp = true;
        channels.ExpiryAlerts.Email = true;
        channels.ExpiryAlerts.Teams = true;
        var sut = Build(factory, email: email, teams: teams, channels: channels);
        var expiry = DateTime.UtcNow.AddDays(3);

        await sut.SendExpiryAlertAsync("Milk", expiry, 5, "Fridge A");

        await email.Received(1).SendEmailAsync(
            "admin@test.local",
            Arg.Is<string>(s => s.Contains("MHD-Warnung")),
            Arg.Is<string>(b => b.Contains("Milk") && b.Contains("Fridge A")),
            isHtml: true);
        await teams.Received(1).SendExpiryAlertAsync("Milk", expiry, 5, "Fridge A");
    }

    [Fact]
    public async Task SendExpiryAlertAsync_EmailWithoutLocation_OmitsLocationLine()
    {
        var factory = CreateFactory(nameof(SendExpiryAlertAsync_EmailWithoutLocation_OmitsLocationLine));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.Admin, email: "admin@test.local"));
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var channels = new NotificationChannels();
        channels.ExpiryAlerts.InApp = false;
        channels.ExpiryAlerts.Email = true;
        channels.ExpiryAlerts.Teams = false;
        var sut = Build(factory, email: email, channels: channels);

        await sut.SendExpiryAlertAsync("Milk", DateTime.UtcNow.AddDays(1), 5);

        await email.Received(1).SendEmailAsync(
            "admin@test.local",
            Arg.Any<string>(),
            Arg.Is<string>(b => !b.Contains("Lagerort:")),
            isHtml: true);
    }

    [Fact]
    public async Task SendExpiryAlertAsync_AllChannelsDisabled_NotifiesNothing()
    {
        var factory = CreateFactory(nameof(SendExpiryAlertAsync_AllChannelsDisabled_NotifiesNothing));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var teams = Substitute.For<ITeamsService>();
        var channels = new NotificationChannels();
        channels.ExpiryAlerts.InApp = false;
        channels.ExpiryAlerts.Email = false;
        channels.ExpiryAlerts.Teams = false;
        var sut = Build(factory, email: email, teams: teams, channels: channels);

        await sut.SendExpiryAlertAsync("Milk", DateTime.UtcNow.AddDays(1), 5);

        await email.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default!, default!, isHtml: default);
        await teams.DidNotReceiveWithAnyArgs().SendExpiryAlertAsync(default!, default, default, default);
    }

    [Fact]
    public async Task SendExpiryAlertAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.SendExpiryAlertAsync("Milk", DateTime.UtcNow.AddDays(1), 5);

        await act.Should().NotThrowAsync();
    }

    // ==================== SendSecurityAlertAsync (multi-channel) ====================

    [Fact]
    public async Task SendSecurityAlertAsync_AllChannelsEnabled_NotifiesSuperAdminsAndTeams()
    {
        var factory = CreateFactory(nameof(SendSecurityAlertAsync_AllChannelsEnabled_NotifiesSuperAdminsAndTeams));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.SuperAdmin, email: "super@test.local"));
            db.Users.Add(MakeUser(2, role: UserRole.Admin, email: "admin@test.local")); // not a super admin: excluded from email
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var teams = Substitute.For<ITeamsService>();
        var channels = new NotificationChannels();
        channels.SecurityAlerts.InApp = true;
        channels.SecurityAlerts.Email = true;
        channels.SecurityAlerts.Teams = true;
        var sut = Build(factory, email: email, teams: teams, channels: channels);

        await sut.SendSecurityAlertAsync("Breach Detected", "Unauthorized access attempt", "high");

        await email.Received(1).SendEmailAsync(
            "super@test.local",
            Arg.Is<string>(s => s.Contains("Breach Detected")),
            Arg.Is<string>(b => b.Contains("Unauthorized access attempt") && b.Contains("high")),
            isHtml: true);
        await teams.Received(1).SendSystemAlertAsync("Breach Detected", "Unauthorized access attempt", "high");
    }

    [Fact]
    public async Task SendSecurityAlertAsync_AllChannelsDisabled_NotifiesNothing()
    {
        var factory = CreateFactory(nameof(SendSecurityAlertAsync_AllChannelsDisabled_NotifiesNothing));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            await db.SaveChangesAsync();
        }
        var email = Substitute.For<IEmailService>();
        var teams = Substitute.For<ITeamsService>();
        var channels = new NotificationChannels();
        channels.SecurityAlerts.InApp = false;
        channels.SecurityAlerts.Email = false;
        channels.SecurityAlerts.Teams = false;
        var sut = Build(factory, email: email, teams: teams, channels: channels);

        await sut.SendSecurityAlertAsync("Title", "Message", "low");

        await email.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default!, default!, isHtml: default);
        await teams.DidNotReceiveWithAnyArgs().SendSystemAlertAsync(default!, default!, default!);
    }

    [Fact]
    public async Task SendSecurityAlertAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.SendSecurityAlertAsync("Title", "Message", "high");

        await act.Should().NotThrowAsync();
    }

    // ==================== SendSystemAlertAsync (multi-channel) ====================

    [Fact]
    public async Task SendSystemAlertAsync_AllChannelsEnabled_NotifiesInAppAndTeams()
    {
        var factory = CreateFactory(nameof(SendSystemAlertAsync_AllChannelsEnabled_NotifiesInAppAndTeams));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse());
            db.Users.Add(MakeUser(1, role: UserRole.SuperAdmin));
            await db.SaveChangesAsync();
        }
        var teams = Substitute.For<ITeamsService>();
        var channels = new NotificationChannels();
        channels.SystemAlerts.InApp = true;
        channels.SystemAlerts.Teams = true;
        var sut = Build(factory, teams: teams, channels: channels);

        await sut.SendSystemAlertAsync("Maintenance", "System will restart", "warning");

        await teams.Received(1).SendSystemAlertAsync("Maintenance", "System will restart", "warning");
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.Notifications.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SendSystemAlertAsync_AllChannelsDisabled_NotifiesNothing()
    {
        var factory = CreateFactory(nameof(SendSystemAlertAsync_AllChannelsDisabled_NotifiesNothing));
        var teams = Substitute.For<ITeamsService>();
        var channels = new NotificationChannels();
        channels.SystemAlerts.InApp = false;
        channels.SystemAlerts.Teams = false;
        var sut = Build(factory, teams: teams, channels: channels);

        await sut.SendSystemAlertAsync("Title", "Message", "info");

        await teams.DidNotReceiveWithAnyArgs().SendSystemAlertAsync(default!, default!, default!);
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.Notifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SendSystemAlertAsync_OuterException_IsSwallowed()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.SendSystemAlertAsync("Title", "Message", "info");

        await act.Should().NotThrowAsync();
    }
}
