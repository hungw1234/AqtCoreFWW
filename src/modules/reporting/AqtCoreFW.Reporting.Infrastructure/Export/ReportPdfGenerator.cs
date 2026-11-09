using AqtCoreFW.Reporting.Application.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static AqtCoreFW.Reporting.Application.Abstractions.ReportFormatHelper;

namespace AqtCoreFW.Reporting.Infrastructure.Export;

/// <summary>
/// Generates GOV-styled PDF from dynamic report data using QuestPDF.
/// A4 landscape, Vietnamese headers, alternating row colors, page numbers.
/// </summary>
public sealed class ReportPdfGenerator : IReportPdfGenerator
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

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                // --- Header ---
                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text(reportNameVi)
                        .Bold().FontSize(14);

                    var dateRange = FormatDateRange(metadata.FromDate, metadata.ToDate);
                    if (!string.IsNullOrEmpty(dateRange))
                    {
                        col.Item().AlignCenter().Text(dateRange)
                            .Italic().FontSize(10);
                    }

                    col.Item().PaddingBottom(8);
                });

                // --- Content: Table ---
                page.Content().Table(table =>
                {
                    // Define columns — equal width
                    table.ColumnsDefinition(columns =>
                    {
                        // Row number column
                        columns.ConstantColumn(30);
                        foreach (var _ in columnNames)
                            columns.RelativeColumn();
                    });

                    // Header row
                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Grey.Darken3)
                            .Padding(3).AlignCenter()
                            .Text("STT").Bold().FontColor(Colors.White).FontSize(8);

                        foreach (var colName in columnNames)
                        {
                            header.Cell().Background(Colors.Grey.Darken3)
                                .Padding(3)
                                .Text(colName).Bold().FontColor(Colors.White).FontSize(8);
                        }
                    });

                    // Data rows
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var bgColor = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                        var row = rows[i];

                        table.Cell().Background(bgColor).Padding(2)
                            .AlignCenter().Text((i + 1).ToString()).FontSize(8);

                        foreach (var colName in columnNames)
                        {
                            row.TryGetValue(colName, out var value);
                            table.Cell().Background(bgColor).Padding(2)
                                .Text(FormatValue(value)).FontSize(8);
                        }
                    }
                });

                // --- Footer ---
                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text($"Ngày xuất: {metadata.GeneratedAt:dd/MM/yyyy HH:mm}")
                        .Italic().FontSize(7);
                    row.RelativeItem().AlignCenter()
                        .Text($"Tổng: {rows.Count:N0} dòng").FontSize(7);
                    row.RelativeItem().AlignRight().Text(text =>
                    {
                        text.Span("Trang ").FontSize(7);
                        text.CurrentPageNumber().FontSize(7);
                        text.Span(" / ").FontSize(7);
                        text.TotalPages().FontSize(7);
                    });
                });
            });
        });

        var bytes = document.GeneratePdf();
        return Task.FromResult(bytes);
    }

}
