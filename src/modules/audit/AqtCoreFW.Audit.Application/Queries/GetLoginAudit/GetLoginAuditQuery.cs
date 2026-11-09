using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Queries.GetLoginAudit;

public sealed record GetLoginAuditQuery(
    Guid? UserId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool? Success,
    Guid? CallerTenantId,
    bool IsPrivileged,
    string? SearchTerm = null,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<LoginAttemptDto>>>;
