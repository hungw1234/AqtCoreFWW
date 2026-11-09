using AqtCoreFW.Workflow.Application.Queries.ListWorkflowInstances;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetWorkflowInstancesSummary;

/// <summary>
/// Returns KPI summary counts for the workflow instances dashboard.
/// Runs a separate query from the list so it can be independently cached later.
/// Uses the same InstanceFilter as the list query for consistent scoping.
/// </summary>
public sealed record GetWorkflowInstancesSummaryQuery(
    Guid TenantId,
    InstanceFilter Filter) : IRequest<Result<WorkflowInstanceSummaryDto>>;
