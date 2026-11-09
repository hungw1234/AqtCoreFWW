using AqtCoreFW.SharedKernel.AI;
using FluentResults;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Stub — returns empty analysis without throwing.
/// Concrete analyst (NLQ via QueryCatalog, anomaly detection) is project-specific.
/// </summary>
public sealed class AiReportAnalystStub : IAiReportAnalyst
{
    public Task<Result<string>> AnalyzeAsync(ReportAnalysisRequest request, CancellationToken ct = default)
        => Task.FromResult(Result.Ok(
            "AI report analysis not configured. Register IAiReportAnalyst implementation."));
}
