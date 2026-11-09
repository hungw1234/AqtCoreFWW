using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateEscalationRule;

/// <summary>
/// Phase-07 PR-D2: injected IWorkflowDefinitionRepository to resolve T2 PublicId → internal long FK.
/// WorkflowDefinitionId in command is Guid (PublicId AK); handler resolves to long before domain Create.
/// </summary>
public sealed class CreateEscalationRuleCommandHandler(
    IRepository<EscalationRule, long> repository,
    IWorkflowDefinitionRepository definitionRepository)
    : IRequestHandler<CreateEscalationRuleCommand, Result<EscalationRuleDto>>
{
    public async Task<Result<EscalationRuleDto>> Handle(
        CreateEscalationRuleCommand request,
        CancellationToken cancellationToken)
    {
        // Resolve T2 WorkflowDefinition PublicId (Guid) → internal long Id for FK storage
        var defResult = await definitionRepository.GetByIdAsync(
            request.WorkflowDefinitionId, cancellationToken);
        if (defResult.IsFailed)
            return Result.Fail(new NotFoundError(
                $"WorkflowDefinition '{request.WorkflowDefinitionId}' not found."));

        var rule = EscalationRule.Create(
            defResult.Value.Id,
            request.StateId,
            request.EscalationAfterMinutes,
            request.EscalateTo,
            request.EscalateToValue,
            request.NotifyVia,
            request.TenantId,
            request.CreatedBy);

        await repository.AddAsync(rule, cancellationToken);

        return Result.Ok(MapToDto(rule, request.WorkflowDefinitionId));
    }

    /// <summary>
    /// Maps domain entity to DTO.
    /// workflowDefinitionPublicId is passed by the caller (already resolved from command) —
    /// avoids an extra repo round-trip since entity only stores the internal long FK.
    /// </summary>
    internal static EscalationRuleDto MapToDto(EscalationRule r, Guid workflowDefinitionPublicId) => new(
        r.Id, workflowDefinitionPublicId, r.StateId,
        r.EscalationAfterMinutes, r.EscalateTo, r.EscalateToValue,
        r.NotifyVia, r.IsActive, r.TenantId);
}
