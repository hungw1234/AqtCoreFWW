using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.ListDelegations;

/// <summary>
/// Query active or historical delegations. M-05: server-paginated.
/// Authorization scope: a non-privileged caller (IsPrivileged=false) sees only delegations they are a
/// party to (ActorId = delegator or delegate); the optional DelegatorId/DelegateId filters apply only
/// for a privileged caller (Admin/SystemAdmin), whose view is additionally tenant-bound in the handler.
/// </summary>
public sealed record ListDelegationsQuery(
    Guid? DelegatorId,
    Guid? DelegateId,
    bool? ActiveOnly,
    Guid ActorId,
    bool IsPrivileged,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<DelegationDto>>>;

/// <summary>
/// Read model returned by ListDelegationsQuery.
/// DelegatorName / DelegateName are Layer-2 (Display) enrichment from IIdentityModuleClient — null when
/// the user is missing/cross-tenant; FE renders a "(Đã xoá)" placeholder in that case.
/// </summary>
public sealed record DelegationDto(
    Guid Id,
    Guid DelegatorId,
    Guid DelegateId,
    DateTime ValidFrom,
    DateTime ValidTo,
    string? Reason,
    bool IsRevoked,
    DateTime? RevokedAt,
    // Lifecycle status name (PendingApproval | Active | Expired | Revoked). The FE renders a distinct
    // badge per status and only offers the approve action on PendingApproval rows — a delegation created
    // on behalf of someone else stays PendingApproval until the delegator or a SystemAdmin approves it.
    string Status,
    string? DelegatorName,
    string? DelegateName);
