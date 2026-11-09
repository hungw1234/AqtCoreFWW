using AqtCoreFW.SharedKernel.AI;
using FluentResults;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Stub — returns GOV_AI_NOT_IMPL without throwing.
/// Replace with real OCR service (Azure Document Intelligence, VinAI) per project.
/// </summary>
public sealed class AiDocumentExtractorStub : IAiDocumentExtractor
{
    public Task<Result<ExtractedDocument>> ExtractAsync(
        byte[] fileBytes, string mimeType, CancellationToken ct = default)
        => Task.FromResult(Result.Fail<ExtractedDocument>(
            "GOV_AI_NOT_IMPL: IAiDocumentExtractor not configured. Register a concrete implementation."));
}
