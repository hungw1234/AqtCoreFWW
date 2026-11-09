using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.CreateAiPromptTrace;

public sealed record CreateAiPromptTraceCommand(
    Guid SessionId,
    string ModelName,
    string PromptHash,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    decimal Cost,
    string ClassificationLevel,
    Guid TenantId,
    Guid? ModelProfileId = null,
    string? PromptText = null,
    string? ResponseHash = null) : IRequest<Result<Guid>>;
