using AqtCoreFW.Organization.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Organization.Application.Queries;

// ── Query Records ────────────────────────────────────────────────────────────
// Handlers for these queries live in AqtCoreFW.Organization.Infrastructure
// (they require OrgDbContext / OrgUnitService — Infrastructure concerns).

public sealed record GetOrgTreeQuery(Guid TenantId) : IQuery<IReadOnlyList<OrgUnitDto>>;
public sealed record GetOrgUnitQuery(Guid Id, Guid TenantId) : IQuery<OrgUnitDto>;
public sealed record GetOrgUnitMembersQuery(Guid OrgUnitId, Guid TenantId)
    : IQuery<IReadOnlyList<UserOrgUnitAssignmentDto>>;
public sealed record GetStaffHistoryQuery(Guid UserId, Guid TenantId) : IQuery<StaffHistoryDto>;

public sealed record StaffHistoryDto(
    IReadOnlyList<UserOrgUnitAssignmentDto> Assignments,
    IReadOnlyList<StaffPositionHistoryDto> PositionHistory);
