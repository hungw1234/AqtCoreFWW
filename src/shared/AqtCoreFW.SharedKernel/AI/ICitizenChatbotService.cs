using FluentResults;

namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Public-facing citizen chatbot — rate-limited, no case context, anonymous-safe.
/// Delegates to IAiRoutingService internally; always routes as Classification=Public.
/// </summary>
public interface ICitizenChatbotService
{
    Task<Result<string>> RespondAsync(
        string message,
        Guid sessionId,
        Guid tenantId,
        CancellationToken ct = default);
}
