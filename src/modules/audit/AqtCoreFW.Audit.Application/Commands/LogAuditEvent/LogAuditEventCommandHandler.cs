using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.LogAuditEvent;

/// <summary>
/// Inserts an audit entry immediately (synchronous write).
/// HMAC chain computation is enqueued as a Hangfire job post-insert.
/// Returns MessageId (Guid AK) — T3 plain-class, no PublicId.
/// </summary>
public sealed class LogAuditEventCommandHandler(
    IAuditLogRepository repository,
    IBackgroundAuditJobEnqueuer jobEnqueuer,
    IGuidGenerator guidGenerator) : IRequestHandler<LogAuditEventCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        LogAuditEventCommand request,
        CancellationToken cancellationToken)
    {
        var entry = AuditLogEntry.Create(
            guidGenerator,
            request.TenantId,
            request.UserId,
            request.UserName,
            request.Action,
            request.ModuleName,
            request.ResourceType,
            request.ResourceId,
            request.DataSnapshot,
            request.IpAddress,
            request.CorrelationId);

        await repository.AddAsync(entry, cancellationToken);

        // Async HMAC chain — does NOT block the write path.
        // Pass TenantId so the batch signer locks per-tenant (sp_getapplock) and signs all
        // unsigned rows for this tenant in one pass, not just the single newly created entry.
        jobEnqueuer.EnqueueHmacChain(entry.TenantId);

        return Result.Ok(entry.MessageId);
    }
}
