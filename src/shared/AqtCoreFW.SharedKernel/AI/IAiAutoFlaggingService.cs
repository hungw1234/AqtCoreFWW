namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Post-response content classification — detects sensitivity/risk in AI-generated text.
/// Fail-open contract: implementations MUST return a low-risk result when the AI backend
/// is unavailable rather than throwing or returning an error.
/// </summary>
public interface IAiAutoFlaggingService
{
    /// <summary>
    /// Classify <paramref name="content"/> for government data sensitivity.
    /// Never throws; returns low risk with empty flags on backend failure.
    /// </summary>
    Task<AutoFlagResult> FlagAsync(string content, Guid tenantId, CancellationToken ct = default);
}
