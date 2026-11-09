using AqtCoreFW.SharedKernel.Abstractions;
using MassTransit;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AqtCoreFW.Infrastructure.Messaging;

/// <summary>
/// Consumes MassTransit Fault&lt;object&gt; messages — triggered after all retries exhausted.
/// Persists failed messages to messaging.dead_letters for admin inspection/retry.
/// Registered via AddConsumer&lt;DeadLetterConsumer&gt;() in MessagingRegistration.
/// MassTransit routes Fault&lt;T&gt; messages to consumers of Fault&lt;object&gt; as a catch-all.
/// </summary>
public sealed class DeadLetterConsumer(
    MessagingDbContext db,
    ILogger<DeadLetterConsumer> logger,
    IGuidGenerator guidGen) : IConsumer<Fault<object>>
{
    public async Task Consume(ConsumeContext<Fault<object>> context)
    {
        var fault = context.Message;
        var messageType = fault.FaultMessageTypes?.FirstOrDefault() ?? "Unknown";
        var payload = SerializeBody(fault.Message);
        var reason = fault.Exceptions?.FirstOrDefault()?.Message ?? "Unknown error";
        var queue = context.ReceiveContext?.InputAddress?.AbsolutePath?.TrimStart('/') ?? "unknown";

        var deadLetter = new MessageDeadLetter
        {
            Id = guidGen.NewIdMonotonic(),
            MessageType = messageType,
            Payload = payload,
            FailureReason = reason.Length > 2000 ? reason[..2000] : reason,
            OriginalQueue = queue
        };

        db.DeadLetters.Add(deadLetter);
        await db.SaveChangesAsync(context.CancellationToken);

        logger.LogWarning(
            "Dead letter recorded. Id={Id} Type={Type} Queue={Queue} Reason={Reason}",
            deadLetter.Id, messageType, queue, reason);
    }

    private static string SerializeBody(object? body)
    {
        if (body is null) return "{}";
        try { return JsonSerializer.Serialize(body); }
        catch { return "{\"_error\": \"Could not serialize message body\"}"; }
    }
}
