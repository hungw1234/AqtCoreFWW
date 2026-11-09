using AqtCoreFW.Audit.Domain.Enums;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.ReviewAiOutput;

public sealed record ReviewAiOutputCommand(
    Guid PromptTraceId,
    Guid ReviewerId,
    ReviewDecision Decision,
    string? Reason) : IRequest<Result>;
