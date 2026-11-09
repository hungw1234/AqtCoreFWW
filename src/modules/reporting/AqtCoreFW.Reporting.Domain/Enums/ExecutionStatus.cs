namespace AqtCoreFW.Reporting.Domain.Enums;

/// <summary>Status of a report execution lifecycle.</summary>
public enum ExecutionStatus
{
    Queued = 0,
    Running = 1,
    Done = 2,
    Failed = 3
}
