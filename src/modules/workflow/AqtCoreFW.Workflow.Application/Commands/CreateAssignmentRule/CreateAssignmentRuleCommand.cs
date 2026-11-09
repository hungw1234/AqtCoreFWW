using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateAssignmentRule;

/// <summary>
/// Creates a new workflow assignment rule for a tenant + entity scope.
/// Phase-4b B6a: WorkflowDefinitionId changed long→Guid (T2 WorkflowDefinition.PublicId);
/// handler resolves internal long FK via IWorkflowDefinitionRepository.GetByPublicIdAsync.
/// </summary>
public sealed record CreateAssignmentRuleCommand(
    Guid TenantId,
    Guid WorkflowDefinitionId,
    string? EntityType,
    string? EntitySubType,
    int Priority,
    string? Description,
    Guid CreatedBy,
    DateTimeOffset? EffectiveFrom = null,
    DateTimeOffset? EffectiveUntil = null)
    : IRequest<Result<WorkflowAssignmentRuleDto>>;
