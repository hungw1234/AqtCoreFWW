using FluentResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;

namespace AqtCoreFW.SharedKernel.Presentation;

/// <summary>
/// Base controller providing ToApiResponse() for consistent HTTP status mapping.
/// All module controllers inherit from this.
/// Maps FluentResults errors to standard HTTP status codes + ApiResponse{T} envelope.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Resolve tenant ID from JWT claims. SystemAdmin can override via X-Tenant-Id header.
    /// </summary>
    protected Guid ResolveTenantId()
    {
        var tenantContext = HttpContext.RequestServices.GetRequiredService<ITenantContext>();

        // SystemAdmin: allow X-Tenant-Id header override for cross-tenant access
        if (tenantContext.IsSystemAdmin
            && Request.Headers.TryGetValue("X-Tenant-Id", out var headerValues)
            && Guid.TryParse(headerValues.FirstOrDefault(), out var overrideId))
            return overrideId;

        return tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Missing tenant claim");
    }

    /// <summary>
    /// Resolve current user ID from JWT claims.
    /// </summary>
    protected Guid ResolveUserId()
    {
        var currentUser = HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        return currentUser.UserId;
    }
    protected IActionResult ToApiResponse<T>(Result<T> result) => result switch
    {
        { IsSuccess: true } => Ok(ApiResponse<T>.Ok(result.Value)),
        { Errors: var e } when e.OfType<NotFoundError>().Any() => NotFound(ApiResponse<T>.Fail(e)),
        { Errors: var e } when e.OfType<ForbiddenError>().Any() => StatusCode(403, ApiResponse<T>.Fail(e)),
        { Errors: var e } when e.OfType<PreconditionFailedError>().Any() => StatusCode(412, ApiResponse<T>.Fail(e)),
        { Errors: var e } when e.OfType<ConflictError>().Any() => Conflict(ApiResponse<T>.Fail(e)),
        { Errors: var e } when e.OfType<BadRequestError>().Any() => BadRequest(ApiResponse<T>.Fail(e)),
        { Errors: var e } when e.OfType<ValidationError>().Any() => UnprocessableEntity(ApiResponse<T>.Fail(e)),
        { Errors: var e } when e.OfType<ServiceUnavailableError>().Any() => StatusCode(503, ApiResponse<T>.Fail(e)),
        _ => StatusCode(500, ApiResponse<T>.Fail(result.Errors))
    };

    protected IActionResult ToApiResponse(Result result) => result switch
    {
        { IsSuccess: true } => NoContent(),
        { Errors: var e } when e.OfType<NotFoundError>().Any() => NotFound(ApiResponse<object>.Fail(e)),
        { Errors: var e } when e.OfType<ForbiddenError>().Any() => StatusCode(403, ApiResponse<object>.Fail(e)),
        { Errors: var e } when e.OfType<PreconditionFailedError>().Any() => StatusCode(412, ApiResponse<object>.Fail(e)),
        { Errors: var e } when e.OfType<ConflictError>().Any() => Conflict(ApiResponse<object>.Fail(e)),
        { Errors: var e } when e.OfType<BadRequestError>().Any() => BadRequest(ApiResponse<object>.Fail(e)),
        { Errors: var e } when e.OfType<ValidationError>().Any() => UnprocessableEntity(ApiResponse<object>.Fail(e)),
        { Errors: var e } when e.OfType<ServiceUnavailableError>().Any() => StatusCode(503, ApiResponse<object>.Fail(e)),
        _ => StatusCode(500, ApiResponse<object>.Fail(result.Errors))
    };
}
