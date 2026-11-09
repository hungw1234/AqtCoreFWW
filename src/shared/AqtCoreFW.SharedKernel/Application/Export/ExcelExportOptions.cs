namespace AqtCoreFW.SharedKernel.Application.Export;

/// <summary>
/// Options controlling Excel export layout and styling.
/// Passed to IExcelExporter.ExportAsync to configure output workbook.
/// </summary>
public record ExcelExportOptions(
    string SheetName = "Data",
    bool AutoFitColumns = true,
    bool FreezeHeaderRow = true,
    string TableStyle = "TableStyleMedium2");
