namespace AqtCoreFW.SharedKernel.AI;

/// <summary>Input for AI report analysis — report type with structured data payload.</summary>
public sealed record ReportAnalysisRequest(
    string ReportType,
    IReadOnlyDictionary<string, string> Data,
    Guid TenantId);
