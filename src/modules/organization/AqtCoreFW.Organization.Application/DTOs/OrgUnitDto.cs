namespace AqtCoreFW.Organization.Application.DTOs;

/// <summary>
/// Flat DTO for a single org unit — clients build tree from ParentId.
/// OrgUnit is T2: Id = PublicId (Guid), ParentId/SuccessorId = PublicId of parent/successor.
/// Handlers project entity.PublicId → Id, and resolve parent PublicId for ParentId/SuccessorId.
/// </summary>
public sealed record OrgUnitDto(
    Guid Id,
    Guid? ParentId,
    string Name,
    string NameEn,
    string Code,
    int Level,
    bool IsActive,
    Guid TenantId,
    Guid? SuccessorId,
    int ChildCount = 0,
    int StaffCount = 0);

/// <summary>Staff assignment DTO — includes user name/email for display.
/// Non-positional record so Dapper can materialize via parameterless ctor + property setters
/// (positional records with nullable props fail Dapper's signature matcher — see memory
/// feedback-dapper-no-positional-records).
/// T1 entities: Id and OrgUnitId are long (DB-assigned surrogate keys).</summary>
public sealed record UserOrgUnitAssignmentDto
{
    public long Id { get; init; }
    public Guid UserId { get; init; }
    // arch:suppress R_T2FK OrgUnit: T1 child entity holds long FK to T2 parent (UserOrgUnitAssignment)
    public long OrgUnitId { get; init; }
    public string RoleInOrg { get; init; } = string.Empty;
    public DateTimeOffset ValidFrom { get; init; }
    public DateTimeOffset? ValidTo { get; init; }
    public bool IsActive { get; init; }
    public string? FullName { get; init; }
    public string? Email { get; init; }
}

/// <summary>Position history DTO. T1 entity: Id and OrgUnitId are long.</summary>
public sealed record StaffPositionHistoryDto(
    long Id,
    Guid UserId,
    // arch:suppress R_T2FK OrgUnit: T1 child entity holds long FK to T2 parent (StaffPositionHistory)
    long OrgUnitId,
    string PositionTitle,
    DateTimeOffset StartDate,
    DateTimeOffset? EndDate);
