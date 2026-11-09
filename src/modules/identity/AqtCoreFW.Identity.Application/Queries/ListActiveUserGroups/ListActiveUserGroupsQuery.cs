using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.ListActiveUserGroups;

/// <summary>
/// Picker query — returns active user groups for a tenant, optionally filtered to active-only.
/// Used by the Workflow assignment editor ByGroup multiselect (RT2-15 Phase 02).
/// Returns lightweight DTO (no Description / CreatedAt — callers don't need admin detail).
/// </summary>
public sealed record ListActiveUserGroupsQuery(Guid TenantId, bool ActiveOnly = true)
    : IQuery<IReadOnlyList<UserGroupSummaryDto>>;

/// <summary>
/// Lightweight DTO for the Workflow ByGroup clause picker.
/// Intentionally omits Description and timestamps — callers only need Code + Name + IsActive.
/// </summary>
public sealed record UserGroupSummaryDto(Guid PublicId, string Code, string Name, bool IsActive);
