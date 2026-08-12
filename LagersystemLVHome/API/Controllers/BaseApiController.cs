using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.API.DTOs;

namespace LagersystemLVHome.API.Controllers;

/// <summary>
/// Base controller for all API controllers with shared functionality.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = "ApiKey")]
[Route("api/[controller]")]
[Produces("application/json")]
public abstract class BaseApiController : ControllerBase
{
    protected int CurrentUserId
    {
        get
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            return int.Parse(userIdClaim?.Value ?? "0");
        }
    }

    protected int CurrentWarehouseId
    {
        get
        {
            var warehouseIdClaim = User.FindFirst("WarehouseId");
            return int.Parse(warehouseIdClaim?.Value ?? "0");
        }
    }

    protected string CurrentUsername => User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";

    protected UserRole CurrentUserRole
    {
        get
        {
            var roleClaim = User.FindFirst(ClaimTypes.Role);
            return Enum.Parse<UserRole>(roleClaim?.Value ?? "User");
        }
    }

    protected bool IsAdmin => CurrentUserRole >= UserRole.Admin;

    protected bool IsSuperAdmin => CurrentUserRole == UserRole.SuperAdmin;

    protected ActionResult<ApiResponse<T>> Success<T>(T data, string? message = null)
    {
        return Ok(ApiResponse<T>.SuccessResult(data, message));
    }

    protected ActionResult<ApiResponse<T>> Error<T>(string error, int statusCode = 400)
    {
        return StatusCode(statusCode, ApiResponse<T>.ErrorResult(error));
    }

    protected ActionResult<ApiResponse<T>> NotFound<T>(string message = "Resource not found")
    {
        return NotFound(ApiResponse<T>.ErrorResult(message));
    }

    protected ActionResult<ApiResponse<T>> Unauthorized<T>(string message = "Unauthorized access")
    {
        return Unauthorized(ApiResponse<T>.ErrorResult(message));
    }

    protected ActionResult<ApiResponse<T>> Forbidden<T>(string message = "Access forbidden")
    {
        return StatusCode(403, ApiResponse<T>.ErrorResult(message));
    }

    protected ActionResult<PaginatedResponse<T>> Paginated<T>(
        List<T> data,
        int page,
        int pageSize,
        int totalCount)
    {
        var response = new PaginatedResponse<T>
        {
            Success = true,
            Data = data,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Ok(response);
    }
}
