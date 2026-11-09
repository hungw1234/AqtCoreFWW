using AqtCoreFW.Audit.Application.Commands.CreateAiPromptTrace;
using AqtCoreFW.SharedKernel.AI;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Audit.Application.Services;

/// <summary>
/// Implements IAiPromptTracer (SharedKernel cross-module contract).
/// Called by M16 AiModelRouter after every AI request to persist a compliance trace.
/// Dispatches CreateAiPromptTraceCommand via MediatR — keeps decorator thin.
/// TenantId defaults to Guid.Empty when not provided (system-level calls).
/// </summary>
public sealed class AiPromptTraceDecorator(
    ISender mediator,
    ILogger<AiPromptTraceDecorator> logger) : IAiPromptTracer
{
    public async Task TraceAsync(AiPromptTraceInput input, CancellationToken ct = default)
    {
        try
        {
            var command = new CreateAiPromptTraceCommand(
                SessionId: input.SessionId,
                ModelName: input.ModelName,
                PromptHash: input.PromptHash,
                InputTokens: input.InputTokens,
                OutputTokens: input.OutputTokens,
                LatencyMs: input.LatencyMs,
                Cost: input.Cost,
                ClassificationLevel: input.ClassificationLevel,
                TenantId: Guid.Empty, // caller should set via overload when tenant context available
                PromptText: input.PromptText,
                ResponseHash: input.ResponseHash);

            await mediator.Send(command, ct);
        }
        catch (Exception ex)
        {
            // Tracing must not break the AI request pipeline — log and swallow
            logger.LogError(ex,
                "AiPromptTraceDecorator failed to persist trace for session {SessionId}, model {ModelName}",
                input.SessionId, input.ModelName);
        }
    }
}
