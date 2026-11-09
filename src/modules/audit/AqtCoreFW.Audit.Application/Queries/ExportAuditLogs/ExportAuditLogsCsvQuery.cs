using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Queries.ExportAuditLogs;

/// <summary>
/// Export audit logs as a CSV byte array (max 10,000 rows).
/// Same filter surface as GetAuditLogsQuery — no pagination, hard-capped at 10k rows.
/// </summary>
public sealed record ExportAuditLogsCsvQuery(
    Guid? TenantId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? UserId,
    string? Action,
    string? ModuleName,
    string? ResourceType,
    string? ResourceId) : IRequest<Result<byte[]>>;
