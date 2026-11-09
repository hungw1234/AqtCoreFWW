using AqtCoreFW.Identity.Domain.Models;

namespace AqtCoreFW.Identity.Domain.Services;

/// <summary>
/// Evaluates policy rules for a given permission code and user context.
/// Rules are loaded from DB, cached in Redis (key: rule:{permissionCode}, TTL 5 min).
/// Evaluation order: sorted by Priority desc — first matching Deny → denied,
/// first matching Allow → allowed, no match → allowed by default.
/// </summary>
public interface IPolicyRuleEvaluator
{
    /// <summary>
    /// Evaluates all active policy rules for <paramref name="permissionCode"/>
    /// against the supplied <paramref name="context"/> claims dictionary.
    /// </summary>
    /// <param name="permissionCode">Permission code to evaluate, e.g. "HOSO.HOSO.APPROVE".</param>
    /// <param name="context">Key-value claim context (field → value) for condition matching.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PolicyDecision> EvaluateAsync(
        string permissionCode,
        IReadOnlyDictionary<string, string> context,
        CancellationToken ct = default);
}
