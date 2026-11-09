namespace AqtCoreFW.Reporting.Application.Abstractions;

/// <summary>
/// Generates GOV-styled Excel (.xlsx) from dynamic report data.
/// Implementation uses ClosedXML in Infrastructure layer.
/// </summary>
public interface IReportExcelGenerator
{
    Task<byte[]> GenerateAsync(
        string reportName,
        string reportNameVi,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<IDictionary<string, object?>> rows,
        ReportMetadata metadata,
        CancellationToken ct = default);
}

/// <summary>Shared metadata for Excel/PDF report generation.</summary>
public record ReportMetadata(
    string TenantId,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate,
    DateTimeOffset GeneratedAt);
