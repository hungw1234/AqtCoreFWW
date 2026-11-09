using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetEscalationRules;

/// <summary>Returns escalation rules for a workflow definition within a tenant.</summary>
public sealed record GetEscalationRulesQuery(
    Guid WorkflowDefinitionId,
    Guid TenantId,
    bool? IsActive = null) : IRequest<Result<IReadOnlyList<EscalationRuleDto>>>;
