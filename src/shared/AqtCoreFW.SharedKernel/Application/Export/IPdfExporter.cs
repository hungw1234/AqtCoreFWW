namespace AqtCoreFW.SharedKernel.Application.Export;

/// <summary>
/// PDF export abstraction — modules call this interface, not QuestPDF directly.
/// Compose document layout via Action&lt;IDocumentContainer&gt; (QuestPDF fluent API).
/// QuestPDF implementation: QuestPdfExporter (Infrastructure).
/// Community license: free for revenue &lt; $1M (suitable for internal GOV tools).
/// </summary>
public interface IPdfExporter
{
    /// <summary>
    /// Generate PDF bytes from a QuestPDF document composition delegate.
    /// Caller provides the layout via compose action; this abstraction isolates QuestPDF dependency.
    /// </summary>
    Task<byte[]> ExportAsync(
        Action<object> compose,
        CancellationToken cancellationToken = default);
}
