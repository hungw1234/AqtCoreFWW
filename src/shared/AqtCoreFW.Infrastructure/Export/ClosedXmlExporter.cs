using System.Reflection;
using AqtCoreFW.SharedKernel.Application.Export;
using ClosedXML.Excel;
using Microsoft.IO;

namespace AqtCoreFW.Infrastructure.Export;

/// <summary>
/// ClosedXML implementation of IExcelExporter.
/// Uses RecyclableMemoryStream for allocation-efficient byte[] production.
/// Reads [ExcelColumn] attribute for Vietnamese column headers.
/// Header row: bold + blue background (#1F3864) + white font, freeze row 1, auto-fit columns.
/// </summary>
public sealed class ClosedXmlExporter(RecyclableMemoryStreamManager memoryStreamManager) : IExcelExporter
{
    public async Task<byte[]> ExportAsync<TRow>(
        IEnumerable<TRow> data,
        ExcelExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ExcelExportOptions();

        var columns = GetColumnMappings(typeof(TRow));
        var rows = data.ToList();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(options.SheetName);

        // Write header row
        for (var col = 0; col < columns.Count; col++)
        {
            var cell = sheet.Cell(1, col + 1);
            cell.Value = columns[col].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Write data rows
        for (var rowIdx = 0; rowIdx < rows.Count; rowIdx++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var col = 0; col < columns.Count; col++)
            {
                var value = columns[col].Property.GetValue(rows[rowIdx]);
                var cell = sheet.Cell(rowIdx + 2, col + 1);
                SetCellValue(cell, value);
            }
        }

        // Apply table style
        if (rows.Count > 0)
        {
            var range = sheet.Range(1, 1, rows.Count + 1, columns.Count);
            var table = range.CreateTable();
            table.Theme = XLTableTheme.FromName(options.TableStyle);
        }

        // Auto-fit and freeze
        if (options.AutoFitColumns)
            sheet.Columns().AdjustToContents();

        if (options.FreezeHeaderRow)
            sheet.SheetView.FreezeRows(1);

        await using var ms = memoryStreamManager.GetStream("excel-export");
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    // --- private helpers ---

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null: break;
            case string s: cell.Value = s; break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = l; break;
            case double d: cell.Value = d; break;
            case decimal dec: cell.Value = (double)dec; break;
            case bool b: cell.Value = b; break;
            case DateTime dt: cell.Value = dt; break;
            case DateTimeOffset dto: cell.Value = dto.LocalDateTime; break;
            case Guid g: cell.Value = g.ToString(); break;
            default: cell.Value = value.ToString(); break;
        }
    }

    private static List<(string Header, PropertyInfo Property)> GetColumnMappings(Type type)
    {
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var mappings = props
            .Select(p => new
            {
                Property = p,
                Attr = p.GetCustomAttribute<ExcelColumnAttribute>()
            })
            .Where(x => x.Attr?.Ignore != true)
            .Select(x => (
                Header: x.Attr?.Header ?? x.Property.Name,
                Order: x.Attr?.Order ?? int.MaxValue,
                Property: x.Property))
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Property.Name)
            .Select(x => (x.Header, x.Property))
            .ToList();

        return mappings;
    }
}
