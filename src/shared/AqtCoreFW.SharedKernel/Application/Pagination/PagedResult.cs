namespace AqtCoreFW.SharedKernel.Application.Pagination;

/// <summary>
/// Standard paginated response supporting both offset and cursor pagination.
/// TotalCount is nullable: null in cursor/keyset mode (total is unknown per-page),
/// non-null in offset mode (COUNT(*) available). [RT-07]
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int? TotalCount,
    PaginationMeta Meta);

public sealed record PaginationMeta(
    int? Page,
    int? PageSize,
    int? TotalPages,
    string? NextCursor,
    string? PrevCursor,
    bool HasNextPage);
