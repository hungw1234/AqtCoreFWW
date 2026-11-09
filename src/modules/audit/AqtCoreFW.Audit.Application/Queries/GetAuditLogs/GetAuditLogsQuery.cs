using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Queries.GetAuditLogs;

/// <summary>Paginated audit log query with optional filters.</summary>
public sealed record GetAuditLogsQuery(
    Guid? TenantId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? UserId,
    string? Action,
    string? ModuleName,
    string? ResourceType,
    string? ResourceId,
    string? SearchTerm = null,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<AuditLogDto>>>;
