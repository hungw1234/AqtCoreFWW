namespace AqtCoreFW.SharedKernel.Application.Pagination;

/// <summary>
/// Dual-mode pagination: offset (Page/PageSize) or cursor-based (Cursor/Limit).
/// PageSize max = 100; Limit max = 200 (export scenarios).
/// </summary>
public interface IPaginationQuery
{
    // Offset-based
    int? Page { get; }
    int? PageSize { get; }
    // Cursor-based
    string? Cursor { get; }
    int? Limit { get; }
}
