using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.DeleteAssignmentRule;

/// <summary>Deactivates an assignment rule (soft-delete via IsActive=false).</summary>
public sealed record DeleteAssignmentRuleCommand(
    long Id,
    Guid TenantId,
    Guid DeletedBy)
    : IRequest<Result>;
