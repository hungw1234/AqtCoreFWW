namespace AqtCoreFW.Identity.Domain.Services;

/// <summary>
/// Metadata for VGCA doc-flow signing — appears in the visible signature stamp on the signed PDF.
/// Passed to all <see cref="IDocumentSigningWorkflow"/> methods.
/// </summary>
/// <param name="FileName">Force filename in BE storage (null → auto-generated from <paramref name="DocNumber"/>).</param>
/// <param name="DocNumber">Document reference number (số hiệu văn bản — shown in visible stamp).</param>
/// <param name="IssuedDate">Issuance date (ngày ban hành — shown in visible stamp).</param>
/// <param name="Extra">Optional metadata key-value pairs for custom fields on the stamp (may be null).</param>
public record DocFlowMetadata(
    string? FileName,
    string DocNumber,
    DateTime IssuedDate,
    IReadOnlyDictionary<string, string>? Extra = null);

/// <summary>
/// Result of a doc-flow signing operation.
/// Signature bytes stay with VGCA-uploaded PDF in blob storage; BE never holds raw signature bytes for Tier-3.
/// </summary>
/// <param name="IsSuccess">True iff VGCA SignService uploaded the signed PDF successfully.</param>
/// <param name="StorageKey"><see cref="IFileStorageService"/> key pointing to the signed PDF blob
/// (populated when <paramref name="IsSuccess"/> is true; null on failure).</param>
/// <param name="DocumentNumber">Document reference number echoed back from VGCA response.</param>
/// <param name="DocumentDate">Document date echoed back from VGCA response (ISO 8601 string).</param>
/// <param name="FailureReason">Human-readable failure description when <paramref name="IsSuccess"/> is false.</param>
/// <param name="RecordId">Identity (PK) of the created <see cref="SigningDocFlowRecord"/> on a successful
/// initiation — lets a wrapping layer (e.g. the unified <c>SigningRequest</c> orchestrator) link the durable
/// request to its underlying provider record. Null on failure or when the operation creates no record.</param>
public record DocFlowResult(
    bool IsSuccess,
    string? StorageKey,
    string? DocumentNumber,
    string? DocumentDate,
    string? FailureReason,
    long? RecordId = null);

/// <summary>
/// Tier-3 doc-flow signing — VGCA SignService downloads a PDF from BE via a short-lived JWT token,
/// applies a visible signature stamp (PIN + drag-drop position), then uploads the signed PDF back
/// via the <c>FileUploadHandler</c> endpoint. BE coordinates the round-trip; no raw signature bytes
/// are retained on the BE for Tier-3 operations.
///
/// <para><b>7 methods, 1-to-1 with VGCA plugin functions (D19 / QĐ 742 mục 5.1):</b></para>
/// <list type="table">
///   <listheader><term>Method</term><term>VGCA plugin function</term><term>Workflow type</term></listheader>
///   <item><term><see cref="SignIssuedAsync"/></term><term><c>vgca_sign_issued</c></term><term>Văn thư phát hành</term></item>
///   <item><term><see cref="SignApprovedAsync"/></term><term><c>vgca_sign_approved</c></term><term>Lãnh đạo phê duyệt</term></item>
///   <item><term><see cref="SignIncomeAsync"/></term><term><c>vgca_sign_income</c></term><term>Ký công văn đến</term></item>
///   <item><term><see cref="CommentAsync"/></term><term><c>vgca_comment</c></term><term>Góp ý / phê bình</term></item>
///   <item><term><see cref="SignAppendixAsync"/></term><term><c>vgca_sign_appendix</c></term><term>Phụ lục đính kèm</term></item>
///   <item><term><see cref="SignCopyAsync"/></term><term><c>vgca_sign_copy</c></term><term>Bản sao điện tử</term></item>
///   <item><term><see cref="SignFilesAsync"/></term><term><c>vgca_sign_files</c></term><term>Ký hàng loạt (batch)</term></item>
/// </list>
///
/// <para><b>Implementation target: W3.</b> W3 also wires <c>FileUploadHandlerController</c> which generates
/// per-sign-request short-lived JWTs (5-minute TTL) for the VGCA service download callback.</para>
///
/// <para>Requires <see cref="SignatureProviderCapabilities.DocFlow"/> capability on the resolved provider.</para>
/// </summary>
public interface IDocumentSigningWorkflow
{
    /// <summary>
    /// Văn thư phát hành — issue-sign a document.
    /// Corresponds to VGCA plugin function <c>vgca_sign_issued</c>.
    /// </summary>
    Task<DocFlowResult> SignIssuedAsync(Guid docId, DocFlowMetadata metadata, CancellationToken ct = default);

    /// <summary>
    /// Lãnh đạo phê duyệt — approval-sign by leadership.
    /// Corresponds to VGCA plugin function <c>vgca_sign_approved</c>.
    /// </summary>
    Task<DocFlowResult> SignApprovedAsync(Guid docId, DocFlowMetadata metadata, CancellationToken ct = default);

    /// <summary>
    /// Ký công văn đến — sign an incoming document.
    /// Corresponds to VGCA plugin function <c>vgca_sign_income</c>.
    /// </summary>
    Task<DocFlowResult> SignIncomeAsync(Guid docId, DocFlowMetadata metadata, CancellationToken ct = default);

    /// <summary>
    /// Góp ý / phê bình — comment-sign on a document.
    /// Corresponds to VGCA plugin function <c>vgca_comment</c>.
    /// </summary>
    Task<DocFlowResult> CommentAsync(Guid docId, DocFlowMetadata metadata, CancellationToken ct = default);

    /// <summary>
    /// Phụ lục đính kèm — sign an appendix attachment.
    /// Corresponds to VGCA plugin function <c>vgca_sign_appendix</c>.
    /// </summary>
    Task<DocFlowResult> SignAppendixAsync(Guid docId, DocFlowMetadata metadata, CancellationToken ct = default);

    /// <summary>
    /// Bản sao điện tử — sign an electronic copy.
    /// Corresponds to VGCA plugin function <c>vgca_sign_copy</c>.
    /// </summary>
    Task<DocFlowResult> SignCopyAsync(Guid docId, DocFlowMetadata metadata, CancellationToken ct = default);

    /// <summary>
    /// Ký hàng loạt — batch-sign multiple PDFs in one operation.
    /// Corresponds to VGCA plugin function <c>vgca_sign_files</c>.
    /// All documents share <paramref name="commonMetadata"/>; individual doc IDs in <paramref name="docIds"/>.
    /// </summary>
    Task<DocFlowResult> SignFilesAsync(
        IReadOnlyList<Guid> docIds,
        DocFlowMetadata commonMetadata,
        CancellationToken ct = default);
}
