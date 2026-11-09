using AqtCoreFW.BusinessProcess.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.BusinessProcess.Domain.Entities;

/// <summary>
/// BusinessProcess aggregate root — pairs a WorkflowDefinition with per-state / per-transition Form bindings
/// to drive intake + structured data capture across a multi-step process.
///
/// T2 entity (AuditableEntity + IPublicIdentified). RowVersion on root only (children carry no concurrency
/// token — they're managed through the aggregate).
///
/// Code is unique per tenant (slug used by API URLs / FE designer). IsActive toggles availability of new BP runs
/// without deleting historical configuration.
/// </summary>
public sealed class BusinessProcessDefinition : AuditableEntity, IAggregateRoot, ITenantScoped, IPublicIdentified
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly List<BusinessProcessStateForm> _stateForms = [];
    private readonly List<BusinessProcessTransitionForm> _transitionForms = [];

    public Guid PublicId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>FK to Workflow.WorkflowDefinition.Id (internal bigint PK).</summary>
    public long WorkflowDefinitionId { get; private set; }

    /// <summary>Optional intake FormTemplate — used for case creation before the workflow starts.</summary>
    public long? IntakeFormTemplateId { get; private set; }

    /// <summary>Pinned version of the intake form; null when IntakeFormTemplateId is null.</summary>
    public int? IntakeFormTemplateVersion { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Optimistic concurrency token — prevents silent overwrites on concurrent edits.</summary>
    public byte[] RowVersion { get; private set; } = null!;

    public IReadOnlyList<BusinessProcessStateForm> StateForms => _stateForms.AsReadOnly();
    public IReadOnlyList<BusinessProcessTransitionForm> TransitionForms => _transitionForms.AsReadOnly();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private BusinessProcessDefinition() { } // EF Core

    /// <summary>Factory — creates an inactive BusinessProcessDefinition tied to a WorkflowDefinition.</summary>
    public static BusinessProcessDefinition Create(
        IGuidGenerator gen,
        Guid tenantId,
        string code,
        string name,
        long workflowDefinitionId,
        Guid createdBy,
        string? description = null,
        long? intakeFormTemplateId = null,
        int? intakeFormTemplateVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (workflowDefinitionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(workflowDefinitionId));

        // Pinning rule: intake version must accompany intake template — both or neither.
        if ((intakeFormTemplateId.HasValue) ^ (intakeFormTemplateVersion.HasValue))
            throw new InvalidOperationException(
                "GOV_BP_010: IntakeFormTemplateId and IntakeFormTemplateVersion must be set together (or both null).");

        var bp = new BusinessProcessDefinition
        {
            PublicId = gen.NewIdMonotonic(),
            TenantId = tenantId,
            Code = code,
            Name = name,
            Description = description,
            WorkflowDefinitionId = workflowDefinitionId,
            IntakeFormTemplateId = intakeFormTemplateId,
            IntakeFormTemplateVersion = intakeFormTemplateVersion,
            IsActive = false
        };
        bp.SetAuditCreate(createdBy);
        bp.AddDomainEvent(new BusinessProcessCreatedEvent(bp.PublicId, tenantId, gen.NewIdMonotonic()));
        return bp;
    }

    /// <summary>
    /// Adds a state→form binding. Aggregate-root invariant: child must belong to the same tenant
    /// (defense-in-depth — Repo Pattern-A + DB unique index + RLS policy block at lower layers).
    /// </summary>
    public void AddStateForm(BusinessProcessStateForm stateForm)
    {
        if (stateForm.TenantId != TenantId)
            throw new InvalidOperationException(
                $"GOV_BP_002: Cannot add BusinessProcessStateForm with TenantId {stateForm.TenantId} to BP owned by tenant {TenantId}.");
        _stateForms.Add(stateForm);
    }

    /// <summary>Adds a transition→form binding. Same aggregate-root tenant invariant as AddStateForm.</summary>
    public void AddTransitionForm(BusinessProcessTransitionForm transitionForm)
    {
        if (transitionForm.TenantId != TenantId)
            throw new InvalidOperationException(
                $"GOV_BP_002: Cannot add BusinessProcessTransitionForm with TenantId {transitionForm.TenantId} to BP owned by tenant {TenantId}.");
        _transitionForms.Add(transitionForm);
    }

    /// <summary>
    /// Removes a specific state→form binding from the collection.
    /// No tenant guard — aggregate-root is already tenant-scoped via load path.
    /// </summary>
    public void RemoveStateForm(BusinessProcessStateForm stateForm) =>
        _stateForms.Remove(stateForm);

    /// <summary>
    /// Removes a specific transition→form binding from the collection.
    /// No tenant guard — aggregate-root is already tenant-scoped via load path.
    /// </summary>
    public void RemoveTransitionForm(BusinessProcessTransitionForm transitionForm) =>
        _transitionForms.Remove(transitionForm);

    /// <summary>
    /// Clears all state→form bindings atomically. Used by replace-all command (D-B-1).
    /// EF change tracking picks up orphan removal — configure cascade-delete or EF owned collection.
    /// </summary>
    public void ClearStateForms() => _stateForms.Clear();

    /// <summary>
    /// Clears all transition→form bindings atomically. Used by replace-all command (D-B-1).
    /// </summary>
    public void ClearTransitionForms() => _transitionForms.Clear();

    /// <summary>
    /// Flip IsActive false→true. Idempotent: no-op (and no event) when already active.
    /// Emits <see cref="BusinessProcessActivatedEvent"/> (IExternalDomainEvent) for outbox dispatch
    /// when the state actually changes — downstream consumers may un-pause BP-Run intake.
    /// </summary>
    public void Activate(IGuidGenerator gen, Guid modifiedBy)
    {
        if (IsActive) return;
        IsActive = true;
        SetAuditUpdate(modifiedBy);
        AddDomainEvent(new BusinessProcessActivatedEvent(PublicId, TenantId, modifiedBy, gen.NewIdMonotonic()));
    }

    /// <summary>
    /// Flip IsActive true→false. Idempotent: no-op (and no event) when already inactive.
    /// Emits <see cref="BusinessProcessDeactivatedEvent"/> (IExternalDomainEvent) for outbox dispatch
    /// when the state actually changes. In-flight WorkflowInstances continue; new BP-Run intake pauses.
    /// </summary>
    public void Deactivate(IGuidGenerator gen, Guid modifiedBy)
    {
        if (!IsActive) return;
        IsActive = false;
        SetAuditUpdate(modifiedBy);
        AddDomainEvent(new BusinessProcessDeactivatedEvent(PublicId, TenantId, modifiedBy, gen.NewIdMonotonic()));
    }

    /// <summary>Updates mutable metadata. WorkflowDefinitionId is immutable post-creation by design.</summary>
    public void UpdateMetadata(string? name, string? description, Guid modifiedBy)
    {
        if (!string.IsNullOrWhiteSpace(name)) Name = name;
        if (description is not null) Description = description;
        SetAuditUpdate(modifiedBy);
    }

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
