using FluentResults;

namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// AI report analysis — narration, NLQ query, anomaly detection.
/// Stub returns empty/placeholder results without throwing.
/// Concrete implementation uses IAiRoutingService + QueryCatalog (no raw Text2SQL).
/// </summary>
/// <remarks>
/// DEFERRED IMPLEMENTATION — interface kept as port per platform strategy.
/// Concrete adapter will be wired when a concrete business case arrives
/// (e.g. analyst NLQ → QueryCatalog executor).
/// See docs/system-architecture.md → "AI Module — Implementation Strategy".
/// </remarks>
public interface IAiReportAnalyst
{
    Task<Result<string>> AnalyzeAsync(ReportAnalysisRequest request, CancellationToken ct = default);
}
