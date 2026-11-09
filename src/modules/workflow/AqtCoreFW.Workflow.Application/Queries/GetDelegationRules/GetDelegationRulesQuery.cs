using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetDelegationRules;

/// <summary>
/// Returns delegation rules for a tenant, optionally filtered by delegator user.
/// Uses Dapper read-side — bypasses EF for query performance.
/// </summary>
public sealed record GetDelegationRulesQuery(
    Guid TenantId,
    Guid? DelegatorUserId = null) : IRequest<Result<IReadOnlyList<DelegationRuleDto>>>;
