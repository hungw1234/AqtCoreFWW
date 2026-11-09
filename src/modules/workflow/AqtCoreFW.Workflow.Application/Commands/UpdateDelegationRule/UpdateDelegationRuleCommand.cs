using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.UpdateDelegationRule;

/// <summary>
/// Updates the delegate user and/or time window of an existing delegation rule.
/// Phase-4b B4c: RuleId is long (T1 DelegationRule.Id bigint PK).
/// </summary>
public sealed record UpdateDelegationRuleCommand(
    // arch:suppress R_T2FK Rule: T1 internal-only entity workflow.DelegationRule (regex collision with T2 rules.Rule)
    long RuleId,
    Guid DelegateUserId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    Guid ModifiedBy,
    Guid TenantId) : IRequest<Result<DelegationRuleDto>>;
