using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.ResolveWorkflowAssignment;

/// <summary>Resolves the best-matching WorkflowDefinitionId for a tenant + entity scope.</summary>
public sealed record ResolveWorkflowAssignmentQuery(
    Guid TenantId,
    string EntityType,
    string? EntitySubType = null)
    : IRequest<Result<Guid>>;
