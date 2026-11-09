using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Events;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.Workflow.Domain.Events;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Tracks the current workflow state for a single business object (e.g. Case, Application).
/// EntityType + EntityId identify the linked business object.
/// Concurrency handled at two layers — see WorkflowInstanceConfiguration for details:
/// app-layer FromStateId check (sequential races) + EF concurrency token on CurrentStateId
/// (simultaneous races → StateConflict GOV_WFL_013).
/// CurrentStateEnteredAt used by SLA breach checker to measure time-in-state.
///
/// T2 entity: AuditableEntity + IPublicIdentified (PublicId = v7 monotonic Guid).
///   DefinitionId: long (FK to T2 WorkflowDefinition.Id).
///   CurrentStateId: long (FK to T1 WorkflowState.Id).
///   OriginalDefinitionId: long? (FK to T2 WorkflowDefinition.Id — migration tracking).
///   LastMigrationId: long? (FK to T1 WorkflowVersionMigration.Id).
///   EntityId: Guid (polymorphic external ref — FK exception).
///   TenantId: Guid (FK exception).
/// </summary>
public sealed class WorkflowInstance : AuditableEntity, IPublicIdentified, IAggregateRoot
{
    // Literal the former WorkflowTransitionedFormSubmissionHandler hard-coded — now the guard on
    // RaiseFinalizationEvent below (only entity type routed to Forms today).
    private const string FormSubmissionEntityType = "FormSubmission";

    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly List<WorkflowInstanceHistory> _history = [];

    public Guid PublicId { get; private set; }
    public long DefinitionId { get; private set; }
    public long CurrentStateId { get; private set; }
    public string EntityType { get; private set; } = string.Empty;    // "Generic", "Application"
    public Guid EntityId { get; private set; }

    /// <summary>
    /// Optional per-run identity. NULL ⇒ this row is subject to one-active-per-entity
    /// (the unique active-entity index includes it). Non-null ⇒ the row is exempt from that
    /// index, permitting N concurrent runs per definition (BusinessProcess runs mint a fresh id).
    /// </summary>
    public Guid? DossierId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CurrentStateEnteredAt { get; private set; }
    public DateTimeOffset? DueAt { get; private set; }              // SLA deadline (null = no SLA)
    public DateTimeOffset? SlaBreachNotifiedAt { get; private set; }
    public Guid TenantId { get; private set; }

    // T5-10: Version migration tracking
    /// <summary>Immediate predecessor definition on last migration hop. Overwritten on each hop.</summary>
    public long? OriginalDefinitionId { get; private set; }

    /// <summary>FK to last WorkflowVersionMigration that touched this instance.</summary>
    public long? LastMigrationId { get; private set; }

    public IReadOnlyList<WorkflowInstanceHistory> History => _history.AsReadOnly();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private WorkflowInstance() { } // EF Core

    public static WorkflowInstance Create(
        IGuidGenerator gen,
        long definitionId,
        long initialStateId,
        string entityType,
        Guid entityId,
        Guid tenantId,
        Guid createdBy,
        int? slaHours = null,
        Guid? dossierId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var instance = new WorkflowInstance
        {
            PublicId = gen.NewIdMonotonic(),
            DefinitionId = definitionId,
            CurrentStateId = initialStateId,
            EntityType = entityType,
            EntityId = entityId,
            TenantId = tenantId,
            StartedAt = now,
            CurrentStateEnteredAt = now,
            DueAt = slaHours.HasValue ? now.AddHours(slaHours.Value) : null,
            // No `?? entityId` default: non-supplying callers leave DossierId NULL so their rows
            // stay IN the unique active-entity index (one-active-per-entity preserved).
            DossierId = dossierId
        };
        instance.SetAuditCreate(createdBy);
        return instance;
    }

    /// <summary>
    /// Moves instance to a new state. Records history. Raises WorkflowTransitionedEvent.
    /// Returns Result.Fail if current state doesn't match transition FromState.
    /// Event uses PublicId (T2 AK) for InstancePublicId; ToStateId is long (T1, no AK).
    /// </summary>
    public void ApplyTransition(
        IGuidGenerator gen,
        WorkflowTransition transition,
        bool isFinalState,
        Guid actorId,
        string? comment)
    {
        var now = DateTimeOffset.UtcNow;
        var history = WorkflowInstanceHistory.Create(
            gen, Id, CurrentStateId, transition.ToStateId,
            transition.ActionName, actorId, comment, now);
        _history.Add(history);

        CurrentStateId = transition.ToStateId;
        CurrentStateEnteredAt = now;

        if (isFinalState)
            CompletedAt = now;

        SetAuditUpdate(actorId);

        AddDomainEvent(new WorkflowTransitionedEvent(PublicId, transition.ToStateId, actorId, gen.NewIdMonotonic()));
    }

    /// <summary>
    /// Raises the outbox-routed finalization integration event when this instance completes a
    /// FormSubmission workflow. MUST be called AFTER an <see cref="ApplyTransition"/> that reaches a
    /// final state, by EVERY finalizing path — today the interactive transition handler
    /// (ExecuteTransitionCommandHandler) and the SLA auto-timeout job (SlaBreachCheckerJob) — so it
    /// matches the path-agnostic coverage of the removed in-process WorkflowTransitionedFormSubmissionHandler.
    /// The OutboxInterceptor captures it in the same SaveChanges (atomic with the transition write).
    /// Guarded to FormSubmission entities — the only consumer today; other entity types raise nothing
    /// (YAGNI, no outbox noise).
    /// </summary>
    public void RaiseFinalizationEvent(string finalStateName, Guid transitionedBy, IGuidGenerator gen)
    {
        if (EntityType != FormSubmissionEntityType) return;

        AddDomainEvent(new WorkflowInstanceFinalizedIntegrationEvent(
            PublicId, EntityType, EntityId, finalStateName, transitionedBy, TenantId,
            gen.NewIdMonotonic(), DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// System-triggered transition when a parallel branch join condition is satisfied.
    /// Records history with action name "parallel-branch-join".
    /// Event uses PublicId for InstancePublicId; continuationStateId is long (T1, no AK).
    /// When the continuation is a final state, stamps <see cref="CompletedAt"/> — mirroring
    /// <see cref="ApplyTransition"/> — so a join-to-final instance is marked terminal, not left
    /// looking active with a null completion timestamp. The finalization integration event is raised
    /// separately by the caller (<see cref="RaiseFinalizationEvent"/>), which holds the continuation
    /// state's name — matching the direct-transition path (ExecuteTransitionCommandHandler / SlaBreachCheckerJob).
    /// </summary>
    public void ApplyParallelJoinTransition(IGuidGenerator gen, long continuationStateId, bool isFinalState, Guid actorId)
    {
        var now = DateTimeOffset.UtcNow;
        var history = WorkflowInstanceHistory.Create(
            gen, Id, CurrentStateId, continuationStateId,
            "parallel-branch-join", actorId, "Parallel branch join completed.", now);
        _history.Add(history);

        CurrentStateId = continuationStateId;
        CurrentStateEnteredAt = now;

        if (isFinalState)
            CompletedAt = now;

        SetAuditUpdate(actorId);

        AddDomainEvent(new WorkflowTransitionedEvent(PublicId, continuationStateId, actorId, gen.NewIdMonotonic()));
    }

    public void MarkSlaBreachNotified()
    {
        SlaBreachNotifiedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records a version migration hop on this instance.
    /// Updates DefinitionId, CurrentStateId, OriginalDefinitionId, LastMigrationId,
    /// and appends a "version-migration" history entry.
    /// OriginalDefinitionId is overwritten on each hop — full chain available via MigrationItems table.
    /// </summary>
    public void RecordMigration(
        IGuidGenerator gen,
        long sourceDefinitionId,
        long migrationId,
        long newStateId,
        long newDefinitionId,
        Guid actorId)
    {
        if (migrationId <= 0)
            throw new ArgumentException("migrationId must be positive.", nameof(migrationId));

        var now = DateTimeOffset.UtcNow;
        var history = WorkflowInstanceHistory.Create(
            gen, Id, CurrentStateId, newStateId,
            "version-migration", actorId, null, now);
        _history.Add(history);

        OriginalDefinitionId = sourceDefinitionId;
        LastMigrationId = migrationId;
        CurrentStateId = newStateId;
        DefinitionId = newDefinitionId;
        CurrentStateEnteredAt = now;
        SetAuditUpdate(actorId);
    }

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
