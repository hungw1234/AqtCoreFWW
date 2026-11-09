using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using AqtCoreFW.Workflow.Application.Commands.CloneWorkflowDefinition;
using AqtCoreFW.Workflow.Application.Commands.CreateNotificationConfig;
using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowDefinition;
using AqtCoreFW.Workflow.Application.Commands.DeleteNotificationConfig;
using AqtCoreFW.Workflow.Application.Commands.DeleteWorkflowDefinition;
using AqtCoreFW.Workflow.Application.Commands.SaveDefinitionGraph;
using AqtCoreFW.Workflow.Application.Commands.ToggleDefinitionActive;
using AqtCoreFW.Workflow.Application.Commands.UpdateWorkflowDefinition;
using AqtCoreFW.Workflow.Application.Queries.GetNotificationConfigs;
using AqtCoreFW.Workflow.Application.Queries.GetWorkflowDefinition;
using AqtCoreFW.Workflow.Application.Queries.GetWorkflowDefinitions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Workflow.Presentation.Controllers;

/// <summary>
/// Workflow Definitions REST API.
/// Definitions are tenant-scoped; management requires Admin role.
/// </summary>
[Route("api/v1/workflow/definitions")]
[Authorize(Policy = PermCodes.Workflow.Def.View)]
public sealed class WorkflowDefinitionsController(ISender mediator) : ApiControllerBase
{
    /// <summary>List workflow definitions for a tenant (paged).</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] bool? isActive,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new GetWorkflowDefinitionsQuery(ResolveTenantId(), isActive, search, page, pageSize),
            cancellationToken));
    }

    /// <summary>Get a single workflow definition with states and transitions.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetWorkflowDefinitionQuery(id, ResolveTenantId()),
            cancellationToken));

    /// <summary>
    /// Create a new workflow definition with states and transitions.
    /// Requires Admin role â€" definitions are system-managed configuration.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Workflow.Def.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateWorkflowDefinitionCommand command,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(command, cancellationToken));

    /// <summary>Update workflow definition metadata (name, description).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermCodes.Workflow.Def.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateWorkflowDefinitionRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new UpdateWorkflowDefinitionCommand(id, request.Name, request.Description, ResolveTenantId(), ResolveUserId()),
            cancellationToken));

    /// <summary>Soft-delete a workflow definition. Fails if active instances exist.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermCodes.Workflow.Def.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new DeleteWorkflowDefinitionCommand(id, ResolveTenantId(), ResolveUserId()),
            cancellationToken));

    /// <summary>Replace all states and transitions on a definition (visual designer save).</summary>
    [HttpPut("{id:guid}/graph")]
    [Authorize(Policy = PermCodes.Workflow.Def.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> SaveGraph(
        Guid id,
        [FromBody] SaveDefinitionGraphRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new SaveDefinitionGraphCommand(
                id, ResolveTenantId(), ResolveUserId(),
                request.States, request.Transitions)
            {
                Branches = request.Branches,
            },
            cancellationToken));

    /// <summary>Activate a workflow definition (makes it available for instance creation).</summary>
    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = PermCodes.Workflow.Def.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Activate(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ToggleDefinitionActiveCommand(id, ResolveTenantId(), ResolveUserId(), true),
            cancellationToken));

    /// <summary>Deactivate a workflow definition.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = PermCodes.Workflow.Def.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Deactivate(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ToggleDefinitionActiveCommand(id, ResolveTenantId(), ResolveUserId(), false),
            cancellationToken));

    /// <summary>Clone a workflow definition as a new version.</summary>
    [HttpPost("{id:guid}/clone")]
    [Authorize(Policy = PermCodes.Workflow.Def.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Clone(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new CloneWorkflowDefinitionCommand(id, ResolveTenantId(), ResolveUserId()),
            cancellationToken));

    /// <summary>Get notification configs for a workflow definition.</summary>
    [HttpGet("{id:guid}/notification-configs")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetNotificationConfigs(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetNotificationConfigsQuery(id, ResolveTenantId()),
            cancellationToken));

    /// <summary>Create a notification config for a workflow definition.</summary>
    [HttpPost("{id:guid}/notification-configs")]
    [Authorize(Policy = PermCodes.Workflow.Def.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> CreateNotificationConfig(
        Guid id,
        [FromBody] CreateNotificationConfigRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new CreateNotificationConfigCommand(
                id,
                request.ActionName,
                request.Channel,
                request.RecipientType,
                request.RecipientValue,
                request.Subject,
                request.BodyTemplate,
                ResolveTenantId(),
                ResolveUserId()),
            cancellationToken));

    /// <summary>Delete a notification config. configId is internal long Id (T1 entity, no PublicId).</summary>
    [HttpDelete("{id:guid}/notification-configs/{configId:long}")]
    [Authorize(Policy = PermCodes.Workflow.Def.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteNotificationConfig(
        Guid id,
        long configId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new DeleteNotificationConfigCommand(configId, ResolveTenantId()),
            cancellationToken));
}

/// <summary>Request body for updating workflow definition metadata.</summary>
public sealed record UpdateWorkflowDefinitionRequest(
    string Name,
    string Description);

/// <summary>Request body for creating a notification config on a workflow definition.</summary>
public sealed record CreateNotificationConfigRequest(
    string ActionName,
    string Channel,
    string RecipientType,
    string RecipientValue,
    string Subject,
    string BodyTemplate);

/// <summary>Request body for replacing all states + transitions on a definition.</summary>
public sealed record SaveDefinitionGraphRequest(
    IReadOnlyList<GraphStateInput> States,
    IReadOnlyList<GraphTransitionInput> Transitions)
{
    /// <summary>Additive-nullable parallel-branch payload; omitted by old clients → no branch rows touched.</summary>
    public IReadOnlyList<GraphBranchInput> Branches { get; init; } = [];
}
