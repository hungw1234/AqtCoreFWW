namespace AqtCoreFW.SharedKernel.Application.Pagination;

/// <summary>
/// Centralized pagination caps for query handlers (ARCH-FU2). Prevents DoS via crafted
/// PageSize requests reaching database FETCH NEXT / LIMIT unchecked.
///
/// Tiers:
/// <list type="bullet">
///   <item><description><see cref="AdminMax"/> (100) — default for admin lists,
///     audit/security log scrolls, CRUD pages. Sized to keep a single page tractable
///     for a UI table without server pressure.</description></item>
///   <item><description><see cref="PickerMax"/> (1000) — FE picker/dropdown endpoints
///     that bulk-load selectable items. Sized to comfortably exceed the largest
///     observed FE picker pageSize (500) with headroom.</description></item>
/// </list>
///
/// Use <see cref="Normalize"/> at handler entry to clamp Page (≥1) and PageSize in
/// one call; the returned tuple drives both Dapper/EF Offset+FETCH NEXT and the
/// PaginationMeta echo.
///
/// Enforced by <c>PaginationClampScannerTests</c> — new handlers reading
/// <c>request.PageSize</c> without referencing this class (or an inline
/// <c>Math.Clamp</c>) fail the build. (<c>Math.Min</c> was rejected as a
/// defense token in ARCH-FU2.1 — it caps only the upper bound.)
/// </summary>
public static class PaginationDefaults
{
    /// <summary>Standard admin list cap (audit logs, CRUD pages, search results).</summary>
    public const int AdminMax = 100;

    /// <summary>Bulk-picker cap for FE dropdown / multi-select endpoints.</summary>
    public const int PickerMax = 1000;

    /// <summary>Sensible default when a caller omits PageSize entirely.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// Clamp <paramref name="requestedPage"/> to ≥1 and <paramref name="requestedPageSize"/>
    /// to the range [1, <paramref name="maxPageSize"/>]. Returned tuple is safe for both
    /// SQL offset/limit parameters and PaginationMeta echo back to the caller.
    /// </summary>
    public static (int Page, int PageSize) Normalize(int requestedPage, int requestedPageSize, int maxPageSize)
        => (Math.Max(1, requestedPage), Math.Clamp(requestedPageSize, 1, maxPageSize));
}
