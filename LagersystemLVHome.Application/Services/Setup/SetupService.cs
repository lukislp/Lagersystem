using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

/// <inheritdoc cref="ISetupService"/>
public sealed class SetupService : ISetupService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly CategorySeederService _categorySeeder;
    private readonly ILogger<SetupService> _logger;

    public SetupService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        CategorySeederService categorySeeder,
        ILogger<SetupService> logger)
    {
        _contextFactory = contextFactory;
        _categorySeeder = categorySeeder;
        _logger = logger;
    }

    public async Task<bool> IsInitialSetupCompletedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AnyAsync(cancellationToken);
    }

    public async Task<Result> CompleteInitialSetupAsync(
        InitialSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Idempotency guard.
            if (await db.Users.AnyAsync(cancellationToken))
            {
                return Result.Failure("setup.alreadycomplete", "System has already been set up");
            }

            var now = DateTime.UtcNow;

            var warehouse = new Warehouse
            {
                Name = request.WarehouseName,
                Code = request.WarehouseCode,
                Address = request.WarehouseAddress ?? string.Empty,
                Description = "Hauptwarehouse - Erstellt bei Ersteinrichtung",
                MaxUsers = request.MaxUsers,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.Warehouses.Add(warehouse);
            await db.SaveChangesAsync(cancellationToken);

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var superAdmin = new User
            {
                Username = request.Username,
                Email = request.Email,
                DisplayName = request.DisplayName,
                PasswordHash = passwordHash,
                Role = UserRole.SuperAdmin,
                WarehouseId = warehouse.Id,
                ApprovalStatus = UserApprovalStatus.Approved,
                ApprovedAt = now,
                IsActive = true,
                CreatedAt = now,
                LastLoginAt = now
            };

            db.Users.Add(superAdmin);
            await db.SaveChangesAsync(cancellationToken);

            // Seed 33 default categories for the new warehouse.
            await _categorySeeder.SeedCategoriesAsync();

            // Final sanity check.
            var userCount = await db.Users.CountAsync(cancellationToken);
            if (userCount == 0)
            {
                return Result.Failure("setup.persistfailed", "User could not be persisted");
            }

            _logger.LogInformation(
                "Initial setup completed: warehouse {WarehouseId} '{WarehouseName}', SuperAdmin {UserId}",
                warehouse.Id, warehouse.Name, superAdmin.Id);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial setup failed for warehouse {WarehouseName}", request.WarehouseName);
            return Result.Failure("setup.failed", ex.Message);
        }
    }
}
