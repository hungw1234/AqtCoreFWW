using AqtCoreFW.Workflow.Application.Services;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.ResolveWorkflowAssignment;

/// <summary>Delegates to IWorkflowAssignmentResolver (cache-aside + Dapper).</summary>
public sealed class ResolveWorkflowAssignmentQueryHandler(IWorkflowAssignmentResolver resolver)
    : IRequestHandler<ResolveWorkflowAssignmentQuery, Result<Guid>>
{
    public Task<Result<Guid>> Handle(
        ResolveWorkflowAssignmentQuery request,
        CancellationToken cancellationToken) =>
        resolver.ResolveAsync(request.TenantId, request.EntityType, request.EntitySubType, cancellationToken);
}
