using FluentResults;
using MediatR;

namespace AqtCoreFW.Ai.Application.Commands.IngestDocument;

/// <summary>
/// Chunks, embeds, and upserts document text into Qdrant for RAG retrieval.
/// Chunking: paragraph-based (\n\n), max 1200 chars, min 50 chars filter.
/// </summary>
public sealed record IngestDocumentCommand(
    Guid FileId,
    string Text,
    Guid TenantId)
    : IRequest<Result>;
