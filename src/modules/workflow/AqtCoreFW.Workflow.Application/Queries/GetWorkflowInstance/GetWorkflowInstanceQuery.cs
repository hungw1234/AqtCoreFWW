using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetWorkflowInstance;

/// <summary>Returns a WorkflowInstance with its full transition history.</summary>
public sealed record GetWorkflowInstanceQuery(Guid Id, Guid TenantId)
    : IRequest<Result<WorkflowInstanceDto>>;
