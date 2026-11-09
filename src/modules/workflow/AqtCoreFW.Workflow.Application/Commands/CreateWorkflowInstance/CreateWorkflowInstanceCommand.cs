using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateWorkflowInstance;

/// <summary>
/// Starts a new WorkflowInstance from a given definition, bound to a business entity.
/// SlaHours: if provided, sets DueAt = UtcNow + SlaHours for SLA breach tracking.
/// DefinitionId: if null/empty, auto-resolved via IWorkflowAssignmentResolver using EntityType + EntitySubType.
/// EntitySubType: optional sub-type used for 4-level specificity resolution.
/// </summary>
public sealed record CreateWorkflowInstanceCommand(
    string EntityType,
    Guid EntityId,
    Guid TenantId,
    Guid CreatedBy,
    Guid? DefinitionId = null,
    int? SlaHours = null,
    string? EntitySubType = null)
    : IRequest<Result<WorkflowInstanceDto>>;
