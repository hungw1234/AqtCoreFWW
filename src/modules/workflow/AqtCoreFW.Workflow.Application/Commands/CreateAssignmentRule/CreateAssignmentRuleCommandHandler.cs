using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateAssignmentRule;

/// <summary>
/// Phase-4b B6a: added IWorkflowDefinitionRepository to resolve T2 PublicId → internal long FK.
/// WorkflowDefinitionId in command is now Guid (PublicId); handler resolves to long before Create.
/// </summary>
public sealed class CreateAssignmentRuleCommandHandler(
    IRepository<WorkflowAssignmentRule, long> repository,
    IWorkflowDefinitionRepository definitionRepository,
    ICacheService cache)
    : IRequestHandler<CreateAssignmentRuleCommand, Result<WorkflowAssignmentRuleDto>>
{
    public async Task<Result<WorkflowAssignmentRuleDto>> Handle(
        CreateAssignmentRuleCommand request,
        CancellationToken cancellationToken)
    {
        // Resolve T2 WorkflowDefinition PublicId (Guid) → internal long Id for FK storage
        var defResult = await definitionRepository.GetByIdAsync(
            request.WorkflowDefinitionId, cancellationToken);
        if (defResult.IsFailed)
            return Result.Fail(new NotFoundError(
                $"WorkflowDefinition '{request.WorkflowDefinitionId}' not found."));

        var rule = WorkflowAssignmentRule.Create(
            request.TenantId,
            defResult.Value.Id,
            request.EntityType,
            request.EntitySubType,
            request.Priority,
            request.Description,
            request.CreatedBy,
            request.EffectiveFrom,
            request.EffectiveUntil);

        await repository.AddAsync(rule, cancellationToken);

        // Invalidate all cached resolution entries for this tenant — new rule may change resolution
        await cache.RemoveByPrefixAsync($"wf:assignment:{request.TenantId}:", cancellationToken);

        return Result.Ok(MapToDto(rule, defResult.Value.PublicId));
    }

    /// <summary>
    /// Maps domain entity to DTO.
    /// workflowDefinitionPublicId passed by caller (already resolved from command) —
    /// entity only stores internal long FK, not the PublicId.
    /// </summary>
    internal static WorkflowAssignmentRuleDto MapToDto(WorkflowAssignmentRule r, Guid workflowDefinitionPublicId) => new(
        r.Id,
        r.TenantId,
        workflowDefinitionPublicId,
        r.EntityType,
        r.EntitySubType,
        (int)r.Specificity,
        r.Priority,
        r.IsActive,
        r.Description,
        r.EffectiveFrom,
        r.EffectiveUntil);
}
