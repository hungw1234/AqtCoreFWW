namespace AqtCoreFW.BusinessProcess.Application.Abstractions;

/// <summary>
/// P4 Slice 2C: read-side Dapper helpers for BP runtime handlers.
/// All queries are same-schema on <c>businessprocess.*</c> only (no JOIN onto <c>forms.*</c> or
/// <c>workflow.*</c> — <see cref="CrossSchemaSqlScanner"/> arch test enforces). PublicId resolution
/// of FormTemplate snapshots is delegated to <c>IFormTemplateQueryClient</c> (Slice 2B precedent).
/// Pattern-A tenant guard happens in the handler (pre-DB), not in the implementation.
/// </summary>
public interface IBusinessProcessRuntimeQueries
{
    /// <summary>
    /// Returns the runtime context (WorkflowDefinition FK, intake form pin, active flag) for a BP definition.
    /// Returns null when the BP is not found OR not in the caller's tenant (no existence leak).
    /// </summary>
    Task<BpRuntimeContext?> GetRuntimeContextAsync(
        Guid bpPublicId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the FormTemplate bindings configured for a (BP, workflowState) pair, ordered by
    /// <c>DisplayOrder</c>. Empty list when none configured (valid — state has no forms).
    /// </summary>
    Task<IReadOnlyList<BpStateFormBinding>> GetStateFormTemplateIdsAsync(
        Guid bpPublicId,
        long workflowStateId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the FormTemplate bindings configured for a (BP, workflowTransition) pair, ordered by
    /// <c>DisplayOrder</c>. Empty list when none configured (valid — transition has no forms).
    /// </summary>
    Task<IReadOnlyList<BpTransitionFormBinding>> GetTransitionFormTemplateIdsAsync(
        Guid bpPublicId,
        long workflowTransitionId,
        Guid tenantId,
        CancellationToken ct = default);
}

/// <summary>Runtime context row read by <see cref="IBusinessProcessRuntimeQueries.GetRuntimeContextAsync"/>.</summary>
public sealed record BpRuntimeContext(
    long BusinessProcessDefinitionId,
    long WorkflowDefinitionId,
    long? IntakeFormTemplateId,
    int? IntakeFormVersion,
    bool IsActive);

/// <summary>State-form binding row — internal FormTemplate FK + pinned version + IsRequired flag.</summary>
public sealed record BpStateFormBinding(
    long FormTemplateId,
    int FormTemplateVersion,
    bool IsRequired);

/// <summary>Transition-form binding row — internal FormTemplate FK + pinned version + IsRequired flag.</summary>
public sealed record BpTransitionFormBinding(
    long FormTemplateId,
    int FormTemplateVersion,
    bool IsRequired);
