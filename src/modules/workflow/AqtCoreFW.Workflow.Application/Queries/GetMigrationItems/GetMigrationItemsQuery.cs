using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetMigrationItems;

/// <summary>
/// Paged query for per-instance migration results.
/// PageSize capped at 200 (enforced in validator).
/// Requires MIGRATION.READ permission.
/// </summary>
public sealed record GetMigrationItemsQuery(
    long MigrationId,
    int Page,
    int PageSize,
    MigrationItemStatus? StatusFilter = null) : IRequest<Result<PagedMigrationItemsDto>>;

/// <summary>Paged response wrapper for migration items.</summary>
public sealed class PagedMigrationItemsDto
{
    public IReadOnlyList<MigrationItemDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}
