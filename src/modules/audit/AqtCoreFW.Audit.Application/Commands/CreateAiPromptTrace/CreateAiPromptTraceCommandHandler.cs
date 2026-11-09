using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.CreateAiPromptTrace;

/// <summary>
/// Creates an AI prompt trace record.
/// Returns MessageId (Guid AK) — T3 plain-class, no PublicId.
/// </summary>
public sealed class CreateAiPromptTraceCommandHandler(
    IAiPromptTraceRepository repository,
    IGuidGenerator guidGenerator)
    : IRequestHandler<CreateAiPromptTraceCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateAiPromptTraceCommand request,
        CancellationToken cancellationToken)
    {
        var trace = AiPromptTrace.Create(
            guidGenerator,
            request.SessionId,
            request.ModelName,
            request.PromptHash,
            request.InputTokens,
            request.OutputTokens,
            request.LatencyMs,
            request.Cost,
            request.ClassificationLevel,
            request.TenantId,
            request.ModelProfileId,
            request.PromptText,
            request.ResponseHash);

        await repository.AddAsync(trace, cancellationToken);
        return Result.Ok(trace.MessageId);
    }
}
