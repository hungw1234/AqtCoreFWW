using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using AqtCoreFW.Workflow.Application.Commands.ApproveVersionMigration;
using AqtCoreFW.Workflow.Application.Commands.CancelVersionMigration;
using AqtCoreFW.Workflow.Application.Commands.CreateVersionMigration;
using AqtCoreFW.Workflow.Application.Commands.ExecuteVersionMigration;
using AqtCoreFW.Workflow.Application.Commands.ValidateMigrationMapping;
using AqtCoreFW.Workflow.Application.Queries.GetMigrationItems;
using AqtCoreFW.Workflow.Application.Queries.GetMigrationStatus;
using AqtCoreFW.Workflow.Application.Queries.GetMigrationsByDefinition;
using AqtCoreFW.Workflow.Application.Queries.GetVersionHistory;
using AqtCoreFW.Workflow.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Workflow.Presentation.Controllers;

/// <summary>
/// Workflow Version Migration REST API â€" admin escape hatch for migrating running instances
/// between definition versions with explicit state mapping and 4-eyes approval.
/// All actor IDs and tenant ID resolved from JWT, never from request body.
/// Permission split: CREATE â‰  APPROVE (4-eyes GOV requirement).
/// </summary>
[Route("api/v1/workflow")]
[Authorize]
public sealed class WorkflowMigrationsController(ISender mediator) : ApiControllerBase
{
    // ---- Version History -------------------------------------------------------

    /// <summary>All versions of a definition key with active instance counts per version.</summary>
    [HttpGet("definitions/{key}/versions")]
    [Authorize(Policy = PermCodes.Workflow.Migration.Read)]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetVersionHistory(
        string key,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new GetVersionHistoryQuery(key, ResolveTenantId()), ct));

    // ---- Validate (dry-run) ----------------------------------------------------

    /// <summary>Dry-run validate state mappings without creating a migration.</summary>
    [HttpPost("migrations/validate")]
    [Authorize(Policy = PermCodes.Workflow.Migration.Create)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Validate(
        [FromBody] ValidateMappingRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new ValidateMigrationMappingCommand(
                request.SourceDefinitionId,
                request.TargetDefinitionId,
                request.StateMappings,
                ResolveTenantId(),
                request.AllowDowngrade), ct));

    // ---- Create ----------------------------------------------------------------

    /// <summary>Create a migration (PendingApproval). Creator must not self-approve.</summary>
    [HttpPost("migrations")]
    [Authorize(Policy = PermCodes.Workflow.Migration.Create)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateMigrationRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new CreateVersionMigrationCommand(
                request.SourceDefinitionId,
                request.TargetDefinitionId,
                request.StateMappings,
                ResolveTenantId(),
                ResolveUserId(),
                request.AllowDowngrade), ct));

    // ---- Approve ---------------------------------------------------------------

    /// <summary>Approve a PendingApproval migration. Approver must differ from creator (4-eyes).</summary>
    /// <remarks>Phase-4b B6a: id is long (T1 WorkflowVersionMigration.Id bigint PK).</remarks>
    [HttpPost("migrations/{id:long}/approve")]
    [Authorize(Policy = PermCodes.Workflow.Migration.Approve)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Approve(
        long id,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new ApproveVersionMigrationCommand(id, ResolveUserId(), ResolveTenantId()), ct));

    // ---- Execute ---------------------------------------------------------------

    /// <summary>Execute an Approved migration (enqueues Hangfire job, returns 202-style Ok + jobId).</summary>
    /// <remarks>Phase-4b B6a: id is long (T1 WorkflowVersionMigration.Id bigint PK).</remarks>
    [HttpPost("migrations/{id:long}/execute")]
    [Authorize(Policy = PermCodes.Workflow.Migration.Execute)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Execute(
        long id,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new ExecuteVersionMigrationCommand(id, ResolveTenantId()), ct));

    // ---- Cancel ----------------------------------------------------------------

    /// <summary>Cancel a migration. InProgress → sets CancelRequested flag (worker halts next batch).</summary>
    /// <remarks>Phase-4b B6a: id is long (T1 WorkflowVersionMigration.Id bigint PK).</remarks>
    [HttpPost("migrations/{id:long}/cancel")]
    [Authorize(Policy = PermCodes.Workflow.Migration.Cancel)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Cancel(
        long id,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new CancelVersionMigrationCommand(id, ResolveTenantId()), ct));

    // ---- Status ----------------------------------------------------------------

    /// <summary>Get migration status including live Hangfire job state.</summary>
    /// <remarks>Phase-4b B6a: id is long (T1 WorkflowVersionMigration.Id bigint PK).</remarks>
    [HttpGet("migrations/{id:long}")]
    [Authorize(Policy = PermCodes.Workflow.Migration.Read)]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetStatus(
        long id,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new GetMigrationStatusQuery(id), ct));

    // ---- Items (paged) ---------------------------------------------------------

    /// <summary>Paged per-instance migration results. PageSize capped at 200.</summary>
    /// <remarks>Phase-4b B6a: id is long (T1 WorkflowVersionMigration.Id bigint PK).</remarks>
    [HttpGet("migrations/{id:long}/items")]
    [Authorize(Policy = PermCodes.Workflow.Migration.Read)]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetItems(
        long id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] MigrationItemStatus? status = null,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new GetMigrationItemsQuery(id, page, pageSize, status), ct));

    // ---- Migrations by Definition ----------------------------------------------

    /// <summary>List all migrations for a definition key (source or target side).</summary>
    [HttpGet("definitions/{key}/migrations")]
    [Authorize(Policy = PermCodes.Workflow.Migration.Read)]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetMigrationsByDefinition(
        string key,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new GetMigrationsByDefinitionQuery(key, ResolveTenantId()), ct));
}

// Request body records â€" actor IDs and tenant ID always resolved from JWT

public sealed record ValidateMappingRequest(
    Guid SourceDefinitionId,
    Guid TargetDefinitionId,
    IReadOnlyList<StateMappingEntry> StateMappings,
    bool AllowDowngrade = false);

public sealed record CreateMigrationRequest(
    Guid SourceDefinitionId,
    Guid TargetDefinitionId,
    IReadOnlyList<StateMappingEntry> StateMappings,
    bool AllowDowngrade = false);
