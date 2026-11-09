using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// DB-defined workflow definition — admins manage states and transitions at runtime.
/// Each definition belongs to a tenant. Versioned via DefinitionKey + Version.
/// DefinitionKey is stable across versions; Id is unique per version.
/// Invariant: must have exactly one state with IsInitial=true before activation.
/// T2 entity: AuditableEntity + IPublicIdentified (PublicId = v7 monotonic Guid).
///   TenantId: Guid (FK exception).
/// </summary>
public sealed class WorkflowDefinition : AuditableEntity, IPublicIdentified, IAggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly List<WorkflowState> _states = [];
    private readonly List<WorkflowTransition> _transitions = [];

    public Guid PublicId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>
    /// The entity type this definition validates against (e.g., "Generic").
    /// Used to resolve the enum of allowed state names via IEntityStatusEnumRegistry.
    /// "Generic" is the default — the only contributor registered out of the box.
    /// </summary>
    public string EntityType { get; private set; } = "Generic";

    /// <summary>Stable identifier across versions (e.g., "cap-phep-xay-dung").</summary>
    public string DefinitionKey { get; private set; } = string.Empty;

    /// <summary>Incrementing version number. Starts at 1.</summary>
    public int Version { get; private set; } = 1;

    /// <summary>True for the latest version of a DefinitionKey per tenant. Only one per key+tenant.</summary>
    public bool IsLatest { get; private set; } = true;

    public IReadOnlyList<WorkflowState> States => _states.AsReadOnly();
    public IReadOnlyList<WorkflowTransition> Transitions => _transitions.AsReadOnly();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private WorkflowDefinition() { } // EF Core

    public static WorkflowDefinition Create(
        IGuidGenerator gen,
        string name,
        string description,
        Guid tenantId,
        Guid createdBy,
        string? definitionKey = null,
        string entityType = "Generic")
    {
        var def = new WorkflowDefinition
        {
            PublicId = gen.NewIdMonotonic(),
            Name = name,
            Description = description,
            TenantId = tenantId,
            IsActive = false,
            DefinitionKey = definitionKey ?? Slugify(name),
            Version = 1,
            IsLatest = true,
            EntityType = entityType
        };
        def.SetAuditCreate(createdBy);
        return def;
    }

    /// <summary>
    /// Deep-clone this definition as a new version. Copies states only (NOT transitions).
    /// Transition FK wiring requires DB-assigned state IDs (SQL Server IDENTITY) and is
    /// performed by CloneWorkflowDefinitionCommandHandler in Phase 2 after AddAsync.
    /// Caller must set IsLatest=false on the source before or after cloning.
    /// </summary>
    /// <param name="nextVersion">
    /// The version number for the clone. Must be the max existing version for this key + 1, computed
    /// across ALL rows INCLUDING soft-deleted ones — the (TenantId, DefinitionKey, Version) unique
    /// index is not filtered by IsDeleted, so a soft-deleted version still occupies its number. Passing
    /// a bare <c>Version + 1</c> collides with a soft-deleted sibling of that version and fails the insert.
    /// </param>
    public WorkflowDefinition Clone(IGuidGenerator gen, Guid createdBy, int nextVersion)
    {
        var clone = new WorkflowDefinition
        {
            PublicId = gen.NewIdMonotonic(),
            Name = Name,
            Description = Description,
            TenantId = TenantId,
            IsActive = false,
            DefinitionKey = DefinitionKey,
            Version = nextVersion,
            IsLatest = true,
            EntityType = EntityType
        };
        clone.SetAuditCreate(createdBy);

        // Deep-copy STATES ONLY. Transition + AutoTransitionOnTimeoutId FK wiring is deferred to
        // CloneWorkflowDefinitionCommandHandler's two-phase save.
        //
        // BUG2-CLONE fix 2026-05-18: the previous in-memory transition copy resolved
        // FromStateId/ToStateId from `newState.Id`, which is 0 here — the SQL Server IDENTITY is
        // assigned only on SaveChanges (the clone is not yet persisted). Every cloned transition
        // was therefore written with FromStateId=0/ToStateId=0 (silent data corruption — see
        // WorkflowDefinitions Id=13). Resolving FKs after persistence by stable state Name mirrors
        // the proven two-phase pattern in CreateWorkflowDefinitionCommandHandler / ReplaceGraphAsync.
        // AutoTransitionOnTimeoutId is intentionally null here and remapped post-save in the handler.
        foreach (var s in _states)
        {
            clone.AddState(WorkflowState.Create(
                clone.Id, s.Name, s.DisplayNameVi, s.DisplayNameEn,
                s.IsInitial, s.IsFinal, s.Color, s.SortOrder,
                s.SlaHours, autoTransitionOnTimeoutId: null,
                positionX: s.PositionX, positionY: s.PositionY));
        }

        return clone;
    }

    /// <summary>Mark this definition as no longer the latest version.</summary>
    public void MarkNotLatest(Guid modifiedBy)
    {
        IsLatest = false;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>
    /// Promote this definition back to latest. Used when the current latest version is deleted and the
    /// highest surviving version must reclaim the flag, so a key never ends up with zero latest rows.
    /// </summary>
    public void MarkLatest(Guid modifiedBy)
    {
        IsLatest = true;
        SetAuditUpdate(modifiedBy);
    }

    public void AddState(WorkflowState state) => _states.Add(state);
    public void AddTransition(WorkflowTransition transition) => _transitions.Add(transition);

    /// <summary>Replace all states and transitions at once (for graph save / visual designer).</summary>
    public void ReplaceGraph(
        List<WorkflowState> newStates,
        List<WorkflowTransition> newTransitions,
        Guid modifiedBy)
    {
        _states.Clear();
        _transitions.Clear();
        _states.AddRange(newStates);
        _transitions.AddRange(newTransitions);
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>Phase 1 of two-phase graph save: clear existing states/transitions.</summary>
    public void ClearGraph(Guid modifiedBy)
    {
        _states.Clear();
        _transitions.Clear();
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>Phase 2 of two-phase graph save: add new states/transitions.</summary>
    public void AddGraphEntities(List<WorkflowState> states, List<WorkflowTransition> transitions)
    {
        _states.AddRange(states);
        _transitions.AddRange(transitions);
    }

    public void Activate(Guid modifiedBy)
    {
        IsActive = true;
        SetAuditUpdate(modifiedBy);
    }

    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetAuditUpdate(modifiedBy);
    }

    public void Update(string name, string description, Guid modifiedBy)
    {
        Name = name;
        Description = description;
        SetAuditUpdate(modifiedBy);
    }

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Convert name to URL-safe slug for DefinitionKey.</summary>
    private static string Slugify(string input) =>
        System.Text.RegularExpressions.Regex
            .Replace(input.ToLowerInvariant().Trim(), @"[^a-z0-9\-]", "-")
            .Trim('-');
}
