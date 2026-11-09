using AqtCoreFW.Reporting.Application.Abstractions;
using ClosedXML.Excel;
using static AqtCoreFW.Reporting.Application.Abstractions.ReportFormatHelper;

namespace AqtCoreFW.Reporting.Infrastructure.Export;

/// <summary>
/// Generates GOV-styled Excel workbook from dynamic report data using ClosedXML.
/// Max 50k rows enforced. Header includes Vietnamese report name, date range, tenant info.
/// </summary>
public sealed class ReportExcelGenerator : IReportExcelGenerator
{
    private const int MaxRows = 50_000;

    public Task<byte[]> GenerateAsync(
        string reportName,
        string reportNameVi,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<IDictionary<string, object?>> rows,
        ReportMetadata metadata,
        CancellationToken ct = default)
    {
        if (rows.Count > MaxRows)
            throw new InvalidOperationException($"Row count {rows.Count} exceeds limit of {MaxRows}.");

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Report");

        // --- GOV Header ---
        var headerRow = 1;
        ws.Cell(headerRow, 1).Value = reportNameVi;
        ws.Range(headerRow, 1, headerRow, Math.Max(columnNames.Count, 3)).Merge();
        ws.Cell(headerRow, 1).Style.Font.Bold = true;
        ws.Cell(headerRow, 1).Style.Font.FontSize = 14;
        ws.Cell(headerRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        headerRow++;
        var dateRange = FormatDateRange(metadata.FromDate, metadata.ToDate);
        if (!string.IsNullOrEmpty(dateRange))
        {
            ws.Cell(headerRow, 1).Value = dateRange;
            ws.Range(headerRow, 1, headerRow, Math.Max(columnNames.Count, 3)).Merge();
            ws.Cell(headerRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(headerRow, 1).Style.Font.Italic = true;
        }

        // --- Column Headers ---
        var dataStartRow = headerRow + 2;
        for (int col = 0; col < columnNames.Count; col++)
        {
            var cell = ws.Cell(dataStartRow, col + 1);
            cell.Value = columnNames[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        // --- Data Rows ---
        for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
        {
            ct.ThrowIfCancellationRequested();
            var row = rows[rowIdx];
            for (int col = 0; col < columnNames.Count; col++)
            {
                var cell = ws.Cell(dataStartRow + 1 + rowIdx, col + 1);
                var key = columnNames[col];
                row.TryGetValue(key, out var value);
                SetCellValue(cell, value);
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
        }

        // --- Footer ---
        var footerRow = dataStartRow + 1 + rows.Count + 1;
        ws.Cell(footerRow, 1).Value = $"Ngày xuất: {metadata.GeneratedAt:dd/MM/yyyy HH:mm}";
        ws.Cell(footerRow, 1).Style.Font.Italic = true;
        ws.Cell(footerRow + 1, 1).Value = $"Tổng số dòng: {rows.Count:N0}";

        // Auto-fit columns (capped at 50 chars width)
        ws.Columns().AdjustToContents(1, 50);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                cell.Value = Blank.Value;
                break;
            case DateTime dt:
                cell.Value = dt;
                cell.Style.DateFormat.Format = "dd/MM/yyyy";
                break;
            case DateTimeOffset dto:
                cell.Value = dto.DateTime;
                cell.Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
                break;
            case decimal d:
                cell.Value = d;
                cell.Style.NumberFormat.Format = "#,##0.##";
                break;
            case double dbl:
                cell.Value = dbl;
                cell.Style.NumberFormat.Format = "#,##0.##";
                break;
            case int i:
                cell.Value = i;
                cell.Style.NumberFormat.Format = "#,##0";
                break;
            case long l:
                cell.Value = l;
                cell.Style.NumberFormat.Format = "#,##0";
                break;
            case bool b:
                cell.Value = b ? "Có" : "Không";
                break;
            default:
                cell.Value = value.ToString();
                break;
        }
    }

}
