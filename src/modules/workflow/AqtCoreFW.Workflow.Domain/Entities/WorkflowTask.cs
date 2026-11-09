using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// A task created within a workflow instance — represents a unit of work assigned to a user.
/// Status transitions: Open → InProgress → Done | Cancelled.
/// TenantId scopes all queries for multi-tenant isolation.
/// T1 entity: non-generic AuditableEntity (long Id DB-assigned).
///   WorkflowInstanceId: long (FK to T2 WorkflowInstance.Id).
///   BranchChildStatusId: long? (FK to T1 WorkflowBranchChildStatus.Id).
///   AssigneeId, CompletedBy, DelegatedFromUserId, TenantId: Guid (FK exception list).
/// </summary>
public sealed class WorkflowTask : AuditableEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public long WorkflowInstanceId { get; private set; }

    /// <summary>
    /// Optional one-way navigation to the parent WorkflowInstance (PHR-V2 Phase 6 S4 / GATE-1 Path A).
    /// Lets EF propagate the IDENTITY-generated <c>WorkflowInstance.Id</c> to
    /// <see cref="WorkflowInstanceId"/> within a single SaveChanges when instance + task are
    /// staged in the same change-tracker scope (instance-creation first-task path).
    ///
    /// SAFE per WorkflowInstanceConfiguration remarks: this is a CHILD→PARENT navigation, not the
    /// reverse — the no-child-collection invariant on WorkflowInstance is preserved (bug 33d4973f
    /// concern only applies to parent → children collections on the rowversion-tracked side).
    /// </summary>
    public WorkflowInstance? Instance { get; private set; }

    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? AssigneeId { get; private set; }
    public WorkflowTaskStatus Status { get; private set; }
    public WorkflowTaskPriority Priority { get; private set; }
    public DateTime? DueDate { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public Guid? CompletedBy { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Links to parallel branch child status. Null for non-branch tasks.</summary>
    public long? BranchChildStatusId { get; private set; }

    /// <summary>
    /// Navigation to the linked branch child status. Setting this (rather than the scalar FK) lets EF
    /// propagate the principal's DB-generated Id into <see cref="BranchChildStatusId"/> when both rows
    /// are inserted in the same SaveChanges (the child status Id is not yet assigned at staging time).
    /// </summary>
    public WorkflowBranchChildStatus? BranchChildStatus { get; private set; }

    /// <summary>
    /// Original assignee before delegation. Non-null means the task is currently delegated.
    /// Set by DelegateTask command; cleared by ReturnDelegatedTask command.
    /// </summary>
    public Guid? DelegatedFromUserId { get; private set; }

    // ── Pool-task delta (plan 260519-0421 v2, P1) ──────────────────────────────
    // A pool task = Status Open + AssigneeId null until a pool-eligible member Claims it.

    /// <summary>
    /// EF rowversion optimistic-concurrency token — the DB race-tiebreak for Claim
    /// (concurrent winners → DbUpdateConcurrencyException → 409, RT2-5/H3).
    /// SAFE on WorkflowTask (unlike WorkflowInstance, which uses a scalar IsConcurrencyToken
    /// to dodge bug 33d4973f): WorkflowTask has NO tracked child navigation, so rowversion
    /// cannot propagate Modified state to children. See WorkflowInstanceConfiguration remarks.
    /// </summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Immutable resolved candidate-predicate snapshot (P2 serializer). Null for a concrete assignee.</summary>
    public string? CandidateRefsJson { get; private set; }

    /// <summary>When the task was claimed out of the pool. Null while unclaimed.</summary>
    public DateTimeOffset? ClaimedAt { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private WorkflowTask() { } // EF Core

    public static WorkflowTask Create(
        long workflowInstanceId,
        string title,
        string? description,
        Guid? assigneeId,
        WorkflowTaskPriority priority,
        DateTime? dueDate,
        Guid tenantId,
        Guid createdBy)
    {
        var task = new WorkflowTask
        {
            WorkflowInstanceId = workflowInstanceId,
            Title = title,
            Description = description,
            AssigneeId = assigneeId,
            Status = WorkflowTaskStatus.Open,
            Priority = priority,
            DueDate = dueDate,
            TenantId = tenantId
        };
        task.SetAuditCreate(createdBy);
        return task;
    }

    /// <summary>
    /// Instance-creation overload (PHR-V2 Phase 6 S4 / GATE-1 Path A): accepts the
    /// <see cref="WorkflowInstance"/> entity directly and sets the navigation property so EF
    /// can propagate the IDENTITY-generated <c>instance.Id</c> to <see cref="WorkflowInstanceId"/>
    /// within a single SaveChanges. Use this when the parent instance is staged but not yet
    /// flushed (e.g. <c>CreateWorkflowInstanceCommandHandler</c> /
    /// <c>InProcessWorkflowModuleClient.CreateInstanceAsync</c>).
    ///
    /// All other callers (ExecuteTransition, ParallelBranch) continue to use the
    /// <c>Create(long workflowInstanceId, …)</c> overload because the instance is already
    /// persisted and the FK is known.
    /// </summary>
    public static WorkflowTask Create(
        WorkflowInstance instance,
        string title,
        string? description,
        Guid? assigneeId,
        WorkflowTaskPriority priority,
        DateTime? dueDate,
        Guid tenantId,
        Guid createdBy)
    {
        var task = new WorkflowTask
        {
            Instance = instance,            // EF will fill WorkflowInstanceId from instance.Id post-INSERT
            Title = title,
            Description = description,
            AssigneeId = assigneeId,
            Status = WorkflowTaskStatus.Open,
            Priority = priority,
            DueDate = dueDate,
            TenantId = tenantId
        };
        task.SetAuditCreate(createdBy);
        return task;
    }

    /// <summary>Creates a task linked to a parallel branch child for automated approval tracking.</summary>
    public static WorkflowTask CreateForBranchChild(
        long workflowInstanceId,
        long branchChildStatusId,
        string title,
        string? description,
        WorkflowTaskPriority priority,
        DateTime? dueDate,
        Guid tenantId,
        Guid createdBy)
    {
        var task = Create(workflowInstanceId, title, description, null, priority, dueDate, tenantId, createdBy);
        task.BranchChildStatusId = branchChildStatusId;
        return task;
    }

    /// <summary>
    /// Creates an approval task for a parallel branch child, linking it via the navigation so EF
    /// propagates the child status's DB-generated Id into the FK during the staging SaveChanges.
    /// Use this overload when the child status is created in the same unit of work (its Id is still 0);
    /// the long overload is for callers that already hold a persisted child status Id.
    /// </summary>
    public static WorkflowTask CreateForBranchChild(
        long workflowInstanceId,
        WorkflowBranchChildStatus branchChildStatus,
        string title,
        string? description,
        WorkflowTaskPriority priority,
        DateTime? dueDate,
        Guid tenantId,
        Guid createdBy)
    {
        var task = Create(workflowInstanceId, title, description, null, priority, dueDate, tenantId, createdBy);
        task.BranchChildStatus = branchChildStatus;
        return task;
    }

    /// <summary>Assigns (or re-assigns) the task to a user.</summary>
    public void Assign(Guid userId, Guid modifiedBy)
    {
        AssigneeId = userId;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>Moves task from Open to InProgress. Idempotent if already InProgress.</summary>
    public void Start(Guid modifiedBy)
    {
        if (Status == WorkflowTaskStatus.Open)
            Status = WorkflowTaskStatus.InProgress;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>Marks task Done, records who completed it and when. Idempotent if already Done.</summary>
    public void Complete(Guid userId)
    {
        if (Status == WorkflowTaskStatus.Done) return; // idempotent
        if (Status == WorkflowTaskStatus.Cancelled)
            throw new InvalidOperationException("Cannot complete a cancelled task.");

        Status = WorkflowTaskStatus.Done;
        CompletedAt = DateTime.UtcNow;
        CompletedBy = userId;
        SetAuditUpdate(userId);
    }

    /// <summary>Cancels task with an optional reason. Idempotent if already Cancelled.</summary>
    public void Cancel(string? reason, Guid modifiedBy)
    {
        if (Status == WorkflowTaskStatus.Cancelled) return; // idempotent
        if (Status == WorkflowTaskStatus.Done)
            throw new InvalidOperationException("Cannot cancel a completed task.");

        Status = WorkflowTaskStatus.Cancelled;
        if (!string.IsNullOrWhiteSpace(reason))
            Description = string.IsNullOrEmpty(Description)
                ? $"Cancelled: {reason}"
                : $"{Description} | Cancelled: {reason}";
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>
    /// Records the original assignee and hands the task to a delegate.
    /// Only valid when the task is not already delegated (DelegatedFromUserId is null).
    /// </summary>
    public void Delegate(Guid originalAssigneeId, Guid delegateUserId, Guid modifiedBy)
    {
        if (DelegatedFromUserId.HasValue)
            throw new InvalidOperationException("Task is already delegated. Return delegation before re-delegating.");

        DelegatedFromUserId = originalAssigneeId;
        AssigneeId = delegateUserId;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>
    /// Clears delegation state, restoring the original assignee.
    /// Idempotent: no-op if task is not currently delegated (F08 guard).
    /// </summary>
    public void ReturnDelegation(Guid modifiedBy)
    {
        if (!DelegatedFromUserId.HasValue)
            return; // F08: idempotent — skip if not delegated

        AssigneeId = DelegatedFromUserId;
        DelegatedFromUserId = null;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>
    /// Stores the resolved candidate-predicate snapshot for a pool task (P2 output).
    /// Null when the task resolves to a single concrete assignee.
    /// </summary>
    public void SetCandidate(string? candidateRefsJson)
    {
        CandidateRefsJson = string.IsNullOrWhiteSpace(candidateRefsJson) ? null : candidateRefsJson;
    }

    /// <summary>
    /// Pool-eligible member claims this task, locking it to them (RT2-5).
    /// Tracked-read + invariant assert AssigneeId == null; RowVersion is the DB race-tiebreak
    /// (the EF UPDATE concurrency check turns a simultaneous loser into 409 — verified in
    /// integration). A loser must NEVER overwrite the winner's assignee.
    /// Status stays Open (user decision 2026-05-19): Claim only locks ownership;
    /// the subsequent Start() performs Open→InProgress so "claimed" and "in progress"
    /// remain distinguishable.
    /// </summary>
    public void Claim(Guid userId, Guid modifiedBy)
    {
        if (AssigneeId.HasValue)
            throw new InvalidOperationException("Task is already claimed.");
        if (Status is WorkflowTaskStatus.Done or WorkflowTaskStatus.Cancelled)
            throw new InvalidOperationException("Cannot claim a completed or cancelled task.");

        AssigneeId = userId;
        ClaimedAt = DateTimeOffset.UtcNow;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>
    /// Assignee releases a claimed pool task back to the pool. Idempotent: no-op when
    /// the task is not currently claimed (mirrors ReturnDelegation F08 guard).
    /// Candidate index is the immutable snapshot and is intentionally left untouched.
    /// </summary>
    public void Unclaim(Guid modifiedBy)
    {
        if (!AssigneeId.HasValue) return; // idempotent — already in pool

        AssigneeId = null;
        ClaimedAt = null;
        Status = WorkflowTaskStatus.Open;
        SetAuditUpdate(modifiedBy);
    }

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

public enum WorkflowTaskStatus
{
    Open = 0,
    InProgress = 1,
    Done = 2,
    Cancelled = 3
}

public enum WorkflowTaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}
