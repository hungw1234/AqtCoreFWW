using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateEscalationRule;

/// <summary>
/// Creates an escalation rule for a workflow definition (optionally scoped to a state).
/// Phase-07 PR-D2: WorkflowDefinitionId changed long→Guid (T2 WorkflowDefinition.PublicId AK);
/// handler resolves internal long FK via IWorkflowDefinitionRepository.GetByIdAsync(Guid).
/// </summary>
public sealed record CreateEscalationRuleCommand(
    Guid WorkflowDefinitionId,
    long? StateId,
    int EscalationAfterMinutes,
    EscalationTarget EscalateTo,
    string EscalateToValue,
    string NotifyVia,
    Guid TenantId,
    Guid CreatedBy) : IRequest<Result<EscalationRuleDto>>;
