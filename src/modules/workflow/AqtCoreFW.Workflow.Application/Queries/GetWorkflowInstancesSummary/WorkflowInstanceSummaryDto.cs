namespace AqtCoreFW.Workflow.Application.Queries.GetWorkflowInstancesSummary;

/// <summary>
/// KPI summary for the workflow instances dashboard.
/// All counts are tenant-scoped.
/// SlaBreachRate is a percentage [0.0, 100.0] — null if no instances with DueAt exist.
/// </summary>
public sealed record WorkflowInstanceSummaryDto(
    int Active,
    int CompletedToday,
    decimal? SlaBreachRate,
    int Total);
