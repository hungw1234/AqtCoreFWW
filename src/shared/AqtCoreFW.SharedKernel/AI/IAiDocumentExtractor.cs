using FluentResults;

namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// OCR + structured field extraction from scanned government documents
/// (CCCD, HoKhau, SoDo, contracts).
/// Default: AiDocumentExtractorStub returns GOV_AI_NOT_IMPL.
/// </summary>
public interface IAiDocumentExtractor
{
    Task<Result<ExtractedDocument>> ExtractAsync(
        byte[] fileBytes,
        string mimeType,
        CancellationToken ct = default);
}
