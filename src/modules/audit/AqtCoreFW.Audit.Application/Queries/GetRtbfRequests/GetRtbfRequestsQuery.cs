using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Queries.GetRtbfRequests;

public sealed record GetRtbfRequestsQuery(
    Guid? TenantId,
    RtbfStatus? Status,
    string? SearchTerm = null,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<RtbfRequestDto>>>;
