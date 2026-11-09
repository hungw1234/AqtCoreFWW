using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetInbox;

/// <summary>
/// Returns the caller's inbox: open/in-progress tasks directly assigned to the caller
/// OR belonging to a pool the caller can claim (EXISTS on WorkflowTaskCandidateIndex).
///
/// UserId and TenantId are always resolved from JWT claims (controller, never request body).
/// PermissionCodes is populated by the handler via IIdentityModuleClient before delegating
/// to IWorkflowTaskRepository.GetInboxAsync.
///
/// ResolverMode: optional; null (default) means ByPermission (MVP). Any non-null value
/// that is not "ByPermission" returns NotImplementedMode (GOV_WFL_018 / HTTP 422).
/// Reserved for forward-compat when Role/Group/OrgUnit modes are implemented (RT2-15).
/// P4 Slice 4-C.
/// </summary>
public sealed record GetInboxQuery(
    Guid UserId,
    Guid TenantId,
    /// <summary>
    /// Resolver mode override. Null = default ByPermission (MVP).
    /// Reserved for future Role/Group/OrgUnit modes (RT2-15 deferred).
    /// </summary>
    string? ResolverMode = null) : IRequest<Result<IReadOnlyList<WorkflowTaskDto>>>;
