namespace AqtCoreFW.SharedKernel.Application.Export;

/// <summary>
/// Excel export abstraction — modules call this interface, not ClosedXML directly.
/// Small datasets (&lt;1000 rows): use ExportAsync with IEnumerable.
/// Large datasets: pipe IAsyncEnumerable from IStreamingExportService into ClosedXML OpenXmlWriter streaming mode.
/// ClosedXML implementation: ClosedXmlExporter (Infrastructure).
/// </summary>
public interface IExcelExporter
{
    /// <summary>
    /// Export typed collection to .xlsx bytes.
    /// Uses [ExcelColumn("...")] attribute on TRow properties for Vietnamese headers.
    /// </summary>
    Task<byte[]> ExportAsync<TRow>(
        IEnumerable<TRow> data,
        ExcelExportOptions? options = null,
        CancellationToken cancellationToken = default);
}
