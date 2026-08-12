using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LagersystemLVHome.Application.Services;

public interface IGdprService
{
    Task<bool> GiveConsentAsync(int userId, bool marketingConsent = false, CancellationToken cancellationToken = default);
    Task<UserDataExport> ExportUserDataAsync(int userId, CancellationToken cancellationToken = default);
    Task<bool> DeleteUserAccountAsync(int userId, string reason, bool hardDelete = false, CancellationToken cancellationToken = default);
    Task<bool> AnonymizeUserDataAsync(int userId, CancellationToken cancellationToken = default);
    Task<List<User>> GetInactiveUsersAsync(int daysInactive = 365, CancellationToken cancellationToken = default);
}
