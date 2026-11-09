namespace AqtCoreFW.Reporting.Application.Abstractions;

/// <summary>
/// Generates GOV-styled PDF from dynamic report data.
/// Implementation uses QuestPDF in Infrastructure layer.
/// </summary>
public interface IReportPdfGenerator
{
    Task<byte[]> GenerateAsync(
        string reportName,
        string reportNameVi,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<IDictionary<string, object?>> rows,
        ReportMetadata metadata,
        CancellationToken ct = default);
}
