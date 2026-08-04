namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Encapsulates the first-run bootstrap: create the initial
/// <see cref="Domain.Models.Warehouse"/>, the SuperAdmin user and the default
/// product categories. Runs only when no users exist yet.
/// </summary>
public interface ISetupService
{
    /// <summary>
    /// Returns <c>true</c> when at least one user exists (setup has already
    /// been completed or is in progress).
    /// </summary>
    Task<bool> IsInitialSetupCompletedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the warehouse, the SuperAdmin and the default categories as a
    /// single atomic operation. Fails if a user already exists (idempotent
    /// guard against duplicate setup runs).
    /// </summary>
    Task<Result> CompleteInitialSetupAsync(
        InitialSetupRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>All data required to bootstrap an empty LagerSystem instance.</summary>
public sealed record InitialSetupRequest(
    string WarehouseName,
    string WarehouseCode,
    string? WarehouseAddress,
    int MaxUsers,
    string Username,
    string Email,
    string DisplayName,
    string Password);
