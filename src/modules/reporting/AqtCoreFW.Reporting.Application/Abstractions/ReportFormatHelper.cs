namespace AqtCoreFW.Reporting.Application.Abstractions;

/// <summary>Shared formatting utilities for report Excel/PDF generators.</summary>
public static class ReportFormatHelper
{
    public static string FormatDateRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from.HasValue && to.HasValue)
            return $"Từ {from.Value:dd/MM/yyyy} đến {to.Value:dd/MM/yyyy}";
        if (from.HasValue)
            return $"Từ {from.Value:dd/MM/yyyy}";
        if (to.HasValue)
            return $"Đến {to.Value:dd/MM/yyyy}";
        return string.Empty;
    }

    public static string FormatValue(object? value) => value switch
    {
        null => "",
        DateTime dt => dt.ToString("dd/MM/yyyy"),
        DateTimeOffset dto => dto.ToString("dd/MM/yyyy HH:mm"),
        decimal d => d.ToString("N2"),
        double dbl => dbl.ToString("N2"),
        bool b => b ? "Có" : "Không",
        _ => value.ToString() ?? ""
    };
}
