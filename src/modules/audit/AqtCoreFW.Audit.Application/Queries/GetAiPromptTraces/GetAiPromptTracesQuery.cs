using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Queries.GetAiPromptTraces;

public sealed record GetAiPromptTracesQuery(
    Guid TenantId,
    string? SearchTerm = null,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<AiPromptTraceDto>>>;
