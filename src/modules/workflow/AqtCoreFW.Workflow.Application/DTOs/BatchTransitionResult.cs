namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>Batch transition outcome — partial-failure semantics: each item independent.</summary>
public sealed record BatchTransitionResult(
    int TotalCount,
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<BatchTransitionItemResult> Items);

/// <summary>Per-item result within a batch transition.</summary>
public sealed record BatchTransitionItemResult(
    Guid InstanceId,
    // arch:accept-long T1 WorkflowTransition: aggregate-internal id, FE round-trips opaque
    long TransitionId,
    bool Success,
    string? Error,
    WorkflowInstanceDto? ResultInstance);
