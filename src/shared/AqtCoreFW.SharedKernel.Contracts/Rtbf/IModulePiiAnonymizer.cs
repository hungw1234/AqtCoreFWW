namespace AqtCoreFW.SharedKernel.Contracts.Rtbf;

/// <summary>
/// Contract for per-module PII anonymization in the RTBF pipeline.
/// Each module (Identity, Cases, Forms, Audit) implements this interface.
/// ProcessRtbfRequestCommandHandler resolves IEnumerable&lt;IModulePiiAnonymizer&gt;
/// and calls them sequentially. Steps are idempotent — safe to retry on failure.
/// </summary>
public interface IModulePiiAnonymizer
{
    /// <summary>Human-readable module name used in failure logs (e.g. "Identity").</summary>
    string ModuleName { get; }

    /// <summary>
    /// Anonymize all PII belonging to dataSubjectId within the given tenant.
    /// Must be idempotent: re-running on already-anonymized data must be safe (skip or no-op).
    /// citizenNationalId is provided when the data subject submitted forms as a guest
    /// (SubmittedBy = Guid.Empty); otherwise null.
    /// </summary>
    Task<RtbfAnonymizationResult> AnonymizeAsync(
        Guid dataSubjectId,
        Guid tenantId,
        string? citizenNationalId,
        CancellationToken cancellationToken = default);
}
