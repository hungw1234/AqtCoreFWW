namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module prefill resolver: walks each target FormField's published <c>PrefillSourceJson</c>
/// snapshot, looks up the source field value from a prior submission in the same BP-Run, and
/// returns a <c>{fieldKey -&gt; value}</c> dict. Implementation lives in BusinessProcess.Application.
///
/// Contract is FluentResults-free by design (this assembly carries no PackageReferences) — outcomes
/// use plain C# types compatible with future gRPC / JSON transport. Caller (BP controller) maps
/// <c>null</c> to <c>NotFoundError</c> -&gt; HTTP 404 per Pattern-A precedent (BP MVP P4 Slice 2A-Read,
/// Slice 2C runtime endpoints, GetFormSubmissionQueryHandler).
///
/// Tenant scoping: cross-tenant or instance-not-found returns <c>null</c> (does not leak existence).
/// Per-field unresolvable values are logged + omitted from the result dict (partial prefill &gt; total fail).
/// </summary>
public interface IFormPrefillContextClient
{
    /// <summary>
    /// Resolve the prefill dictionary for <paramref name="targetFormTemplatePublicId"/> rendered inside
    /// the BP-Run identified by <paramref name="workflowInstanceId"/>. Returns <c>null</c> when the
    /// instance does not exist or belongs to a different tenant. Returns an empty dict when the run
    /// exists but no prefill values resolved (target schema has no prefill-tagged fields, or every
    /// source submission/field was unresolvable).
    /// </summary>
    Task<IReadOnlyDictionary<string, object?>?> GetPrefillContextAsync(
        Guid workflowInstanceId,
        Guid targetFormTemplatePublicId,
        int targetFormTemplateVersion,
        Guid tenantId,
        CancellationToken ct = default);
}
