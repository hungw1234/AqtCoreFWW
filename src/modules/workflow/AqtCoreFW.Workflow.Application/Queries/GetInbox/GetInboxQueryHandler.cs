using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetInbox;

/// <summary>
/// Handler for <see cref="GetInboxQuery"/>.
///
/// Resolver: ByPermission (default) + Role + Group + OrgUnit merged read-side.
/// - Fetches caller's effective permission codes via IIdentityModuleClient (Redis-cached ≤10 min).
/// - Fetches direct role names + direct group codes (LIVE, no cache).
/// - Fetches active temporal org unit codes (LIVE) via IOrgUnitModuleClient (RT2-15 Phase 04).
/// - Delegates EXISTS-over-CandidateIndex query to IWorkflowTaskRepository.GetInboxAsync.
///
/// P4 Slice 4-C; Role + Group support added by T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION + EXT-GROUP;
/// OrgUnit support added by RT2-15 Phase 04.
/// </summary>
public sealed class GetInboxQueryHandler(
    IWorkflowTaskRepository repository,
    IIdentityModuleClient identityClient,
    IOrgUnitModuleClient orgUnitClient)
    : IRequestHandler<GetInboxQuery, Result<IReadOnlyList<WorkflowTaskDto>>>
{
    // The only resolver mode currently implemented.
    private const string ByPermissionMode = "ByPermission";

    public async Task<Result<IReadOnlyList<WorkflowTaskDto>>> Handle(
        GetInboxQuery request,
        CancellationToken cancellationToken)
    {
        // NotImplementedMode guard (F6): any explicit mode other than ByPermission is rejected.
        // null = default = ByPermission (MVP path).
        if (request.ResolverMode is not null &&
            !string.Equals(request.ResolverMode, ByPermissionMode, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail(WorkflowErrors.NotImplementedMode(request.ResolverMode));
        }

        // Fetch caller's permission codes — Redis-cached, ≤1 round-trip per 10 min.
        // Returns HashSet<string> (IReadOnlySet<string>). Empty set = no pool tasks visible.
        var permCodes = await identityClient.GetEffectivePermissionCodesAsync(
            request.UserId, cancellationToken);

        // Fetch caller's direct role codes — LIVE (no-cache) per
        // T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION Phase 01. Excludes group-inherited roles
        // (PQ-1 A direct-only). Empty set = no Role-pool tasks visible.
        var roleCodes = await identityClient.GetUserRoleCodesAsync(
            request.UserId, request.TenantId, cancellationToken);

        // Fetch caller's direct group codes — LIVE (no-cache) per
        // T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP. Includes only groups the user has direct
        // UserGroupMembership in; group active + user active + tenant-scoped.
        // Empty set = no Group-pool tasks visible.
        var groupCodes = await identityClient.GetUserGroupCodesAsync(
            request.UserId, request.TenantId, cancellationToken);

        // Fetch caller's active temporal org unit codes — LIVE (no-cache) per RT2-15 Phase 04.
        // Temporal filter: IsActive=1 AND ValidFrom<=Now AND (ValidTo IS NULL OR ValidTo>Now).
        // Empty set = no OrgUnit-pool tasks visible.
        var orgUnitCodes = await orgUnitClient.GetUserOrgUnitCodesAsync(
            request.UserId, request.TenantId, cancellationToken);

        // EXISTS-over-nav-prop query: assigned-to-me OR pool-eligible.
        // Pool predicate: PrincipalType ∈ {Permission, Role, Group, OrgUnit}.
        // No entity materialization — projects WorkflowTaskDto at DB level.
        var tasks = await repository.GetInboxAsync(
            request.UserId, permCodes, roleCodes, groupCodes, orgUnitCodes, request.TenantId, cancellationToken);

        return Result.Ok(tasks);
    }
}
