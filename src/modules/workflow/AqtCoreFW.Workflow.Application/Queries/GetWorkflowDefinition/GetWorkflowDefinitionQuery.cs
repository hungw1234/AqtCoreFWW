using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetWorkflowDefinition;

/// <summary>Returns a single WorkflowDefinition with its States and Transitions.</summary>
public sealed record GetWorkflowDefinitionQuery(Guid Id, Guid TenantId)
    : IRequest<Result<WorkflowDefinitionDto>>;
