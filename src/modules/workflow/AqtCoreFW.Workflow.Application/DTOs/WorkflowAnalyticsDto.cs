namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// Period grouping for analytics time-series queries.
/// Mapped to hardcoded SQL fragments — NEVER interpolate user strings to prevent SQL injection.
/// </summary>
public enum GroupByPeriod { Day, Week, Month }

/// <summary>
/// Count of workflow instances per state for a given definition.
/// Phase-4b B4c: T1 WorkflowState — StateId is long (bigint identity PK).
/// </summary>
public sealed class StateDistributionDto
{
    // arch:accept-long T1 WorkflowState: aggregate-internal id, analytics consumers treat as opaque
    public long StateId { get; set; }
    public string StateName { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Percentage { get; set; }
}

/// <summary>
/// Average and median time (minutes) spent in each state.
/// Phase-4b B4c: T1 WorkflowState — StateId is long (bigint identity PK).
/// </summary>
public sealed class StateProcessingTimeDto
{
    // arch:accept-long T1 WorkflowState: aggregate-internal id, analytics consumers treat as opaque
    public long StateId { get; set; }
    public string StateName { get; set; } = string.Empty;
    public double AvgMinutes { get; set; }
    public double MedianMinutes { get; set; }
    public int SampleCount { get; set; }
}

/// <summary>
/// Transition count per user per time period.
/// When IsOthers=true this row aggregates all users outside the top-N and UserId is Guid.Empty.
/// </summary>
public sealed class UserThroughputDto
{
    public Guid UserId { get; set; }
    public string Period { get; set; } = string.Empty;
    public int TransitionCount { get; set; }
    /// <summary>True when this row represents the aggregated "Others" bucket (UserId == Guid.Empty).</summary>
    public bool IsOthers { get; set; }
}

/// <summary>SLA on-time compliance rate per time period.</summary>
public sealed class SlaComplianceTrendDto
{
    public string Period { get; set; } = string.Empty;
    public int Total { get; set; }
    public int OnTime { get; set; }
    public decimal CompliancePercent { get; set; }
}

/// <summary>
/// Instances that have been in a state longer than a threshold.
/// Phase-4b B4c: T2 WorkflowInstance — InstanceId is Guid (PublicId).
///   T2 WorkflowDefinition — DefinitionId is Guid (PublicId).
/// </summary>
public sealed class AgingInstanceDto
{
    public Guid InstanceId { get; set; }   // WorkflowInstance.PublicId
    public Guid DefinitionId { get; set; } // WorkflowDefinition.PublicId
    public string StateName { get; set; } = string.Empty;
    public double HoursInState { get; set; }
    public DateTimeOffset EnteredAt { get; set; }
}

/// <summary>
/// States with high average dwell time (bottleneck candidates).
/// Phase-4b B4c: T1 WorkflowState — StateId is long (bigint identity PK).
/// </summary>
public sealed class BottleneckDto
{
    // arch:accept-long T1 WorkflowState: aggregate-internal id, analytics consumers treat as opaque
    public long StateId { get; set; }
    public string StateName { get; set; } = string.Empty;
    public double AvgDwellMinutes { get; set; }
    public int ActiveCount { get; set; }
}
