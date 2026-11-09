namespace AqtCoreFW.Reporting.Application.DTOs;

/// <summary>
/// Chart-ready payload returned by GetWidgetData handler.
/// Columns: ordered list of column names.
/// Rows: list of dictionaries keyed by column name.
/// Metadata: optional render hints (e.g. chartType, lastRefreshed).
/// </summary>
public record WidgetDataDto(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    IReadOnlyDictionary<string, string> Metadata);
