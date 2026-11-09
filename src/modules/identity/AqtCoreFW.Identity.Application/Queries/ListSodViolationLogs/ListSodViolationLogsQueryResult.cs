namespace AqtCoreFW.Identity.Application.Queries.ListSodViolationLogs;

/// <summary>Paged result for SoD violation logs.</summary>
public sealed record ListSodViolationLogsQueryResult(
    IReadOnlyList<SodViolationLogDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
