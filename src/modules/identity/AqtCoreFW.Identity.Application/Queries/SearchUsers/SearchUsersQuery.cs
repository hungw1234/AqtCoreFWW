using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.SearchUsers;

/// <summary>
/// Search users in tenant — minimal fields (Id, FullName, DepartmentCode), no PII (Email).
/// Used by chat module to pick conversation participants.
/// Caller responsibility: pass tenant + current user IDs from JWT (ResolveTenantId/ResolveUserId).
/// </summary>
public sealed record SearchUsersQuery(string? Q, int PageSize, Guid TenantId, Guid CurrentUserId)
    : IQuery<IReadOnlyList<UserSearchResultDto>>;

/// <summary>Minimal user info for search results — no email (PII protection).</summary>
public sealed record UserSearchResultDto(Guid Id, string FullName, string? DepartmentCode);
