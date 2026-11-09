using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.GetGroupById;

/// <summary>Get a single group with members and role assignments.</summary>
public sealed record GetGroupByIdQuery(Guid GroupId) : IQuery<GroupDetailDto>;

public sealed record GroupDetailDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    Guid? TenantId,
    DateTime CreatedAtUtc,
    int MemberCount,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<GroupMemberDto> Members);

public sealed record GroupMemberDto(Guid UserId, string FullName, string Email);
