using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateDelegationRule;

/// <summary>Creates a time-bounded delegation rule for a user within a tenant.</summary>
public sealed record CreateDelegationRuleCommand(
    Guid DelegatorUserId,
    Guid DelegateUserId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    Guid TenantId,
    Guid CreatedBy) : IRequest<Result<DelegationRuleDto>>;
