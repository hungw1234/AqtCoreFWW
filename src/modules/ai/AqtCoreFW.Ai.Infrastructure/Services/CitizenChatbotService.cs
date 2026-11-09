using AqtCoreFW.SharedKernel.AI;
using AqtCoreFW.SharedKernel.Domain;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Public-facing citizen chatbot — delegates to IAiRoutingService as Classification=Public.
/// No case context, no PII exposure. Rate-limited at YARP level (10 req/s).
/// </summary>
public sealed class CitizenChatbotService(
    IAiRoutingService routingService,
    ILogger<CitizenChatbotService> logger)
    : ICitizenChatbotService
{
    public async Task<Result<string>> RespondAsync(
        string message,
        Guid sessionId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        // Citizen chatbot: always Public classification — never routes to confidential data
        var ctx = new AiContext(
            TenantId: tenantId,
            UserId: Guid.Empty,
            CorrelationId: sessionId.ToString(),
            Classification: ClassificationLevel.Public);

        logger.LogDebug(
            "CitizenChatbot: session={SessionId} tenant={TenantId}", sessionId, tenantId);

        return await routingService.RouteAsync(message, ClassificationLevel.Public, ctx, ct);
    }
}
