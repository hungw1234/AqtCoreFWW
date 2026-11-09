using AqtCoreFW.SharedKernel.AI;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Ai.Application.Commands.IngestDocument;

/// <summary>Delegates to IDocumentIngestionService (chunking + embedding + Qdrant upsert).</summary>
public sealed class IngestDocumentCommandHandler(IDocumentIngestionService ingestionService)
    : IRequestHandler<IngestDocumentCommand, Result>
{
    public async Task<Result> Handle(IngestDocumentCommand request, CancellationToken cancellationToken)
        => await ingestionService.IngestAsync(request.FileId, request.Text, request.TenantId, cancellationToken);
}
