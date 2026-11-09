using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetAssignmentRules;

/// <summary>Returns all assignment rules for a tenant, ordered by Specificity DESC, Priority DESC.</summary>
public sealed record GetAssignmentRulesQuery(Guid TenantId)
    : IRequest<Result<IReadOnlyList<WorkflowAssignmentRuleDto>>>;
