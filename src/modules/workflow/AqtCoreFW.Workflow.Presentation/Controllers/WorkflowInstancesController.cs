using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using AqtCoreFW.Workflow.Application.Commands.BatchExecuteTransition;
using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowInstance;
using AqtCoreFW.Workflow.Application.Commands.ExecuteTransition;
using AqtCoreFW.Workflow.Application.Commands.ResolveBranchChild;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.ExportInstanceAudit;
using AqtCoreFW.Workflow.Application.Queries.GetAvailableTransitions;
using AqtCoreFW.Workflow.Application.Queries.GetBranchStatus;
using AqtCoreFW.Workflow.Application.Queries.GetMyProcessedInstances;
using AqtCoreFW.Workflow.Application.Queries.GetWorkflowInstance;
using AqtCoreFW.Workflow.Application.Queries.GetWorkflowInstancesSummary;
using AqtCoreFW.Workflow.Application.Queries.ListWorkflowInstances;
using AqtCoreFW.Workflow.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Workflow.Presentation.Controllers;

/// <summary>
/// Workflow Instances REST API.
/// One instance per business entity lifecycle (e.g. Case approval flow).
/// Tenant ID and user ID are resolved from JWT claims, not request body.
/// </summary>
/// <remarks>
/// 4-A-A INVARIANT: explicit Policy= required on every action — class-level [Authorize] is NOT a security gate.
/// Phase-04-A follow-up 2026-05-22: Create + 4 reads (GetById/GetAvailableTransitions/ExportAudit/GetBranchStatus)
/// elevated from naked [Authorize] to explicit policies (Manage for Create, View for reads) — same bug class
/// as 4-A WorkflowTasksController.Reassign.
/// </remarks>
[Route("api/v1/workflow/instances")]
[Authorize]
public sealed class WorkflowInstancesController(ISender mediator) : ApiControllerBase
{
    /// <summary>
    /// Create a new workflow instance bound to a business entity.
    /// Instance starts at the definition's initial state.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Workflow.Instance.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateWorkflowInstanceRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new CreateWorkflowInstanceCommand(
                request.EntityType,
                request.EntityId,
                ResolveTenantId(),
                ResolveUserId(),
                request.DefinitionId,
                request.SlaHours,
                request.EntitySubType),
            cancellationToken));

    /// <summary>
    /// Cursor-paginated list of workflow instances for the tenant.
    /// First page: omit cursor. Subsequent pages: pass nextCursor from previous response.
    /// pageSize clamped server-side to [1, 100] regardless of what client sends.
    /// Returns 400 if cursor token is malformed (GOV_WORKFLOW_040).
    /// </summary>
    [HttpGet("")]
    [Authorize(Policy = PermCodes.Workflow.Instance.View)]
    [EnableRateLimiting("read-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? entityType = null,
        [FromQuery] long? stateId = null, // Phase-4b B6a: long (T1 WorkflowState.Id bigint PK)
        [FromQuery] InstanceStatus? status = null,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ListWorkflowInstancesQuery(
                ResolveTenantId(),
                cursor,
                pageSize,
                new InstanceFilter(entityType, stateId, status)),
            cancellationToken));

    /// <summary>
    /// Returns KPI summary counts (active, completedToday, slaBreachRate, total) for the tenant.
    /// Runs a separate query from the list to allow independent caching and reuse.
    /// Accepts the same filter parameters as the list endpoint for consistent scoping.
    /// </summary>
    [HttpGet("summary")]
    [Authorize(Policy = PermCodes.Workflow.Instance.View)]
    [EnableRateLimiting("read-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetSummaryAsync(
        [FromQuery] string? entityType = null,
        [FromQuery] long? stateId = null, // Phase-4b B6a: long (T1 WorkflowState.Id bigint PK)
        [FromQuery] InstanceStatus? status = null,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetWorkflowInstancesSummaryQuery(
                ResolveTenantId(),
                new InstanceFilter(entityType, stateId, status)),
            cancellationToken));

    /// <summary>
    /// Cursor-paginated list of workflow instances that the authenticated caller has
    /// transitioned at least once ("dossiers I have processed").
    /// Sort: most-recently-created instance first (InstanceId DESC).
    /// First page: omit cursor. Subsequent pages: pass nextCursor from previous response.
    /// pageSize clamped server-side to [1, 100].
    /// Returns 400 if cursor token is malformed (GOV_WORKFLOW_040).
    /// Requires BUSINESSPROCESS.BP.VIEW — the caller must be a BP participant.
    /// </summary>
    [HttpGet("my-processed")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [EnableRateLimiting("read-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetMyProcessedAsync(
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetMyProcessedInstancesQuery(
                ResolveUserId(),
                ResolveTenantId(),
                cursor,
                pageSize),
            cancellationToken));

    /// <summary>Get a workflow instance with full transition history.</summary>
    // Gated on Bp.View (not Instance.View): this is the data source for the BP run-detail page,
    // which the task inbox links to. BP participants hold Bp.View + Task.Process but not the
    // admin-tier Instance.View; sibling endpoints (my-processed, tasks/by-instance) already gate
    // BP-participant instance reads on Bp.View. Tenant-scoped in the handler.
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetWorkflowInstanceQuery(id, ResolveTenantId()),
            cancellationToken));

    /// <summary>
    /// Execute a workflow transition on the instance.
    /// Uses optimistic concurrency — returns 409 on concurrent modification.
    /// Phase-4b B6a: id=Guid (T2 WorkflowInstance.PublicId); TransitionId=long (T1 WorkflowTransition.Id).
    /// </summary>
    [HttpPost("{id:guid}/transitions")]
    [Authorize(Policy = PermCodes.Workflow.Task.Process)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> ExecuteTransition(
        Guid id,
        [FromBody] ExecuteTransitionRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ExecuteTransitionCommand(
                id,
                request.TransitionId,
                ResolveUserId(),
                request.Comment,
                request.Metadata,
                NextHandlerUserId: request.NextHandlerUserId), // Slice 10a: thread override (DDTO-DEFAULT)
            cancellationToken));

    /// <summary>
    /// Get transitions available from the instance's current state,
    /// filtered to those the authenticated user is permitted to execute.
    /// </summary>
    // Gated on Bp.View (mirrors GetById): the BP run-detail page fetches available transitions
    // for the inbox-linked run; BP participants hold Bp.View, not the admin-tier Instance.View.
    [HttpGet("{id:guid}/available-transitions")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetAvailableTransitions(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetAvailableTransitionsQuery(id, ResolveTenantId(), ResolveUserId()),
            cancellationToken));

    /// <summary>
    /// Resolve a single child within an active parallel branch on the instance.
    /// When the join condition is met the instance auto-transitions to the continuation state.
    /// Phase-4b B6a: id=Guid (T2 WorkflowInstance.PublicId); childStatusId=long (T1 WorkflowBranchChildStatus.Id).
    /// </summary>
    [HttpPost("{id:guid}/branch-children/{childStatusId:long}/resolve")]
    [Authorize(Policy = PermCodes.Workflow.Task.Process)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> ResolveBranchChild(
        Guid id,
        long childStatusId,
        [FromBody] ResolveBranchChildRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ResolveBranchChildCommand(
                id,
                childStatusId,
                (BranchResolutionType)request.ResolutionType,
                ResolveUserId(),
                request.Comment,
                ResolveTenantId()),
            cancellationToken));

    /// <summary>
    /// Execute the same transition on multiple instances with partial-failure semantics.
    /// Each item is processed independently; response reports per-item success/failure.
    /// Max 50 items per batch (validator-enforced). Stricter rate limit than single transition.
    /// </summary>
    [HttpPost("batch-transition")]
    [Authorize(Policy = PermCodes.Workflow.Task.Process)]
    [EnableRateLimiting("batch-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> BatchTransition(
        [FromBody] BatchTransitionRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new BatchExecuteTransitionCommand(
                request.Items
                    .Select(i => new BatchTransitionItem(i.InstanceId, i.TransitionId))
                    .ToList(),
                ResolveUserId(),
                request.Comment),
            cancellationToken));

    /// <summary>
    /// Export instance audit trail in JSON or CSV format.
    /// Returns file download with correct content type and filename.
    /// Tenant-scoped â€" cross-tenant returns 404.
    /// PDF format is placeholder â€" returns 422 until composer ships.
    /// </summary>
    [HttpGet("{id:guid}/audit-export")]
    [Authorize(Policy = PermCodes.Workflow.Instance.View)]
    [EnableRateLimiting("read-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> ExportAudit(
        Guid id,
        [FromQuery] AuditExportFormat format = AuditExportFormat.Json,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new ExportInstanceAuditQuery(id, ResolveTenantId(), format),
            cancellationToken);

        if (result.IsFailed)
            return ToApiResponse(result);

        var r = result.Value;
        return File(r.Data, r.ContentType, r.FileName);
    }

    /// <summary>Returns all parallel branch statuses (with child statuses) for the instance.</summary>
    [HttpGet("{id:guid}/branch-status")]
    [Authorize(Policy = PermCodes.Workflow.Instance.View)]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetBranchStatus(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetBranchStatusQuery(id, ResolveTenantId()),
            cancellationToken));
}

// Request body records â€" no entity types (GOV_SEC_004 mass-assignment prevention)
// TenantId and user identity fields removed â€" resolved from JWT via base class
public sealed record CreateWorkflowInstanceRequest(
    string EntityType,
    Guid EntityId,
    Guid? DefinitionId = null,
    int? SlaHours = null,
    string? EntitySubType = null);

public sealed record ExecuteTransitionRequest(
    // arch:accept-long T1 WorkflowTransition: aggregate-internal id, FE round-trips opaque
    long TransitionId, // Phase-4b B6a: long (T1 WorkflowTransition.Id bigint PK)
    string? Comment,
    Dictionary<string, object?>? Metadata,
    // Slice 10a DDTO-DEFAULT: optional assignor override — null means use normal resolver.
    Guid? NextHandlerUserId = null);

public sealed record ResolveBranchChildRequest(
    int ResolutionType,
    string? Comment);

public sealed record BatchTransitionRequest(
    IReadOnlyList<BatchTransitionRequestItem> Items,
    string? Comment);

public sealed record BatchTransitionRequestItem(
    Guid InstanceId,       // T2 WorkflowInstance.PublicId
    // arch:accept-long T1 WorkflowTransition: aggregate-internal id, FE round-trips opaque
    long TransitionId);    // Phase-4b B6a: long (T1 WorkflowTransition.Id bigint PK)
