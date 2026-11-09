using AqtCoreFW.Reporting.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Reporting.Application.Queries.GetSavedQueries;

/// <summary>Returns paged list of QueryCatalogEntry entries visible to the caller's role.</summary>
public record GetSavedQueriesQuery(
    string? CallerRole,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<QueryCatalogEntryDto>>>;

/// <summary>T1 entity: Id is long internal PK (no PublicId on QueryCatalogEntry).</summary>
public record QueryCatalogEntryDto(
    long Id,
    string Code,
    string Description,
    string DescriptionVi,
    string RequiredParams,
    string OptionalParams,
    string? RequiredRole);
