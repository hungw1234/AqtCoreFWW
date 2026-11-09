using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.LogAuditEvent;

/// <summary>
/// Command to log a single audit event.
/// Fire-and-forget — callers do NOT await the HMAC chain result.
/// HMAC chain is computed asynchronously by HmacChainJob after insert.
/// </summary>
public sealed record LogAuditEventCommand(
    Guid? TenantId,
    Guid? UserId,
    string UserName,
    string Action,
    string ModuleName,
    string ResourceType,
    string? ResourceId = null,
    string? DataSnapshot = null,
    string? IpAddress = null,
    string? CorrelationId = null) : IRequest<Result<Guid>>;
