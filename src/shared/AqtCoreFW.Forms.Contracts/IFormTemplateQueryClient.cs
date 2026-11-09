namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module interface for read-only FormTemplate snapshot lookups by internal bigint FK.
/// First consumer: BusinessProcess module (BP MVP P4 Slice 2B) — resolves intake form pinned by
/// <c>BusinessProcessDefinition.IntakeFormTemplateId + IntakeFormTemplateVersion</c>.
///
/// Monolith: <c>InProcessFormTemplateQueryClient</c> (Dapper same-schema read in forms).
/// Microservice: gRPC when Forms extracted — internal-id signature will require an inverse
/// PublicId-resolution call there; acceptable cost on this read-cold path.
///
/// Contract uses <c>long</c> for FormTemplateId because callers (BP) hold the bigint FK directly
/// and resolving to Guid then back to bigint is unnecessary indirection in a same-DB monolith.
/// JSDoc this on every method so the future-microservice port flagging is explicit.
/// </summary>
public interface IFormTemplateQueryClient
{
    /// <summary>
    /// Return the schema snapshot for a (formTemplateId, versionNumber) pair, scoped to tenant.
    /// Returns null if the snapshot does not exist, the template was soft-deleted, or it belongs to
    /// a different tenant (does not leak existence across tenants).
    ///
    /// Monolith-only signature: <c>long formTemplateId</c> is the Forms-internal bigint PK. BP holds
    /// this as a FK on <c>BusinessProcessDefinition.IntakeFormTemplateId</c>. When Forms is extracted
    /// to a service, callers must first resolve their bigint FK to <c>Guid PublicId</c> via a new
    /// lookup endpoint (or denormalise PublicId on the BP side).
    /// </summary>
    Task<FormTemplateVersionSchemaDto?> GetVersionSchemaByInternalIdAsync(
        long formTemplateId,
        int versionNumber,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// BP MVP P6: PublicId variant. Resolves the snapshot for a (formTemplate PublicId, version) pair,
    /// scoped to tenant. Used by the prefill resolver where callers hold the Guid AK only (FE never sees
    /// the bigint internal PK). Same null-on-cross-tenant semantics as the internal-id variant.
    /// </summary>
    Task<FormTemplateVersionSchemaDto?> GetVersionSchemaByPublicIdAsync(
        Guid formTemplatePublicId,
        int versionNumber,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// BP MVP P7: resolves FormTemplate PublicId → internal bigint Id, scoped to tenant.
    /// Returns null when the template does not exist OR belongs to a different tenant.
    /// Used by CreateBusinessProcess to pin an intake form via Guid PublicId.
    /// </summary>
    Task<long?> TryResolveTemplateInternalIdAsync(
        Guid formTemplatePublicId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// BP form-map detail (BUG2 fix 2026-05-18): batch-resolve a set of FormTemplate internal
    /// bigint Ids → Guid PublicId, scoped to tenant. Inverse of
    /// <see cref="TryResolveTemplateInternalIdAsync"/>; batched to avoid N+1 on the BP detail
    /// read path (one BP can pin many state/transition forms). Missing or cross-tenant ids are
    /// simply omitted from the result map (no existence leak). Empty input → empty map, no query.
    /// Microservice note: same internal-bigint-FK porting caveat as the rest of this contract.
    /// </summary>
    Task<IReadOnlyDictionary<long, Guid>> ResolveTemplatePublicIdsAsync(
        IReadOnlyCollection<long> formTemplateIds,
        Guid tenantId,
        CancellationToken ct = default);
}
