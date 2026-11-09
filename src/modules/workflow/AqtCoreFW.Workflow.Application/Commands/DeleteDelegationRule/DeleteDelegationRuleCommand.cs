using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.DeleteDelegationRule;

/// <summary>
/// Soft-deletes a delegation rule by setting IsActive = false.
/// Phase-4b B4c: RuleId is long (T1 DelegationRule.Id bigint PK).
/// </summary>
public sealed record DeleteDelegationRuleCommand(
    // arch:suppress R_T2FK Rule: T1 internal-only entity workflow.DelegationRule (regex collision with T2 rules.Rule)
    long RuleId,
    Guid ModifiedBy,
    Guid TenantId) : IRequest<Result>;
