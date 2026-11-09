namespace AqtCoreFW.SharedKernel.Application.Export;

/// <summary>
/// Streaming export abstraction for large datasets — constant memory regardless of row count.
/// DB query uses AsAsyncEnumerable() + yield return; caller pipes into ClosedXML OpenXmlWriter.
/// CancellationToken propagation: HTTP request cancel → DB query cancelled immediately.
/// 10k rows ~ few MB RAM (vs. full buffer which could be hundreds of MB).
/// Implement per data source (DB query streamed via AsAsyncEnumerable()).
/// </summary>
public interface IStreamingExportService<TFilter, TRow>
{
    IAsyncEnumerable<TRow> StreamAsync(
        TFilter filter,
        CancellationToken cancellationToken = default);
}
