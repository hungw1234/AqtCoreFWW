using AqtCoreFW.Forms.Application.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// QuestPDF implementation of IFormSubmissionPdfGenerator.
/// A4 portrait, Vietnamese labels, two-column key/value table, footer with page numbers.
/// Community license is set once via static constructor.
/// </summary>
public sealed class FormSubmissionPdfGenerator : IFormSubmissionPdfGenerator
{
    static FormSubmissionPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(
        string templateName,
        Guid submissionId,
        int templateVersion,
        string status,
        DateTimeOffset submittedAt,
        string? reviewComment,
        IReadOnlyList<(string Key, string LabelVi, string? Value)> fields,
        bool isTableMode,
        IReadOnlyList<(string RoleLabelVi, string? SignerSubject, DateTimeOffset? VerifiedAt, bool? Verdict)>? signerBlocks = null)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                // Header
                page.Header().Column(col =>
                {
                    col.Item().AlignCenter()
                        .Text(templateName)
                        .Bold().FontSize(16);

                    col.Item().AlignCenter()
                        .Text($"Phiên bản {templateVersion}")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);

                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    col.Item().PaddingBottom(8);
                });

                // Content
                page.Content().Column(col =>
                {
                    // Metadata section
                    col.Item().Table(meta =>
                    {
                        meta.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1);
                            c.RelativeColumn(2);
                        });

                        AddMetaRow(meta, "Mã phiếu", submissionId.ToString());
                        AddMetaRow(meta, "Trạng thái", MapStatusVi(status));
                        AddMetaRow(meta, "Ngày nộp", submittedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
                        if (!string.IsNullOrWhiteSpace(reviewComment))
                            AddMetaRow(meta, "Nhận xét", reviewComment);
                    });

                    col.Item().PaddingTop(12)
                        .Text("Dữ liệu biểu mẫu")
                        .Bold().FontSize(12);

                    col.Item().PaddingTop(4);

                    if (isTableMode)
                    {
                        // Table-mode: data stored in relational table, not JSON
                        col.Item()
                            .Background(Colors.Yellow.Lighten4)
                            .Padding(8)
                            .Text("Dữ liệu lưu trữ dạng bảng — xem chi tiết trong hệ thống quản lý.")
                            .Italic().FontColor(Colors.Grey.Darken2);
                    }
                    else if (fields.Count == 0)
                    {
                        col.Item()
                            .Padding(8)
                            .Text("(Không có dữ liệu)")
                            .Italic().FontColor(Colors.Grey.Darken1);
                    }
                    else
                    {
                        // Field data table
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2);
                                c.RelativeColumn(3);
                            });

                            // Table header
                            table.Header(h =>
                            {
                                h.Cell().Background(Colors.Grey.Darken3)
                                    .Padding(5)
                                    .Text("Trường").Bold().FontColor(Colors.White);

                                h.Cell().Background(Colors.Grey.Darken3)
                                    .Padding(5)
                                    .Text("Giá trị").Bold().FontColor(Colors.White);
                            });

                            // Field rows — alternating background
                            for (int i = 0; i < fields.Count; i++)
                            {
                                var (key, labelVi, value) = fields[i];
                                var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten5;
                                var label = string.IsNullOrWhiteSpace(labelVi) ? key : labelVi;

                                table.Cell().Background(bg).Padding(4)
                                    .Text(label).FontSize(9);

                                table.Cell().Background(bg).Padding(4)
                                    .Text(value ?? string.Empty).FontSize(9);
                            }
                        });
                    }

                    // Multi-signer evidence blocks — rendered after field data, before footer.
                    // When signerBlocks is null or empty this branch is skipped entirely,
                    // preserving byte-identical output for single-signer/no-signer submissions.
                    if (signerBlocks is { Count: > 0 })
                    {
                        col.Item().PaddingTop(16).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        col.Item().PaddingTop(8)
                            .Text("Thông tin chữ ký số")
                            .Bold().FontSize(12);

                        col.Item().PaddingTop(4);

                        for (int i = 0; i < signerBlocks.Count; i++)
                        {
                            var (roleLabelVi, signerSubject, verifiedAt, verdict) = signerBlocks[i];
                            var blockBg = i % 2 == 0 ? Colors.Grey.Lighten5 : Colors.White;

                            col.Item().Background(blockBg).Padding(6).Column(block =>
                            {
                                block.Item()
                                    .Text(roleLabelVi)
                                    .Bold().FontSize(10);

                                block.Item().PaddingTop(2).Table(sig =>
                                {
                                    sig.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(1);
                                        c.RelativeColumn(2);
                                    });

                                    AddMetaRow(sig, "Người ký", signerSubject ?? "(Không có thông tin)");
                                    AddMetaRow(sig, "Thời điểm xác minh",
                                        verifiedAt.HasValue
                                            ? verifiedAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
                                            : "—");
                                    AddMetaRow(sig, "Kết quả",
                                        verdict.HasValue
                                            ? (verdict.Value ? "Hợp lệ" : "Không hợp lệ")
                                            : "—");
                                });
                            });
                        }
                    }
                });

                // Footer
                page.Footer().Row(row =>
                {
                    row.RelativeItem()
                        .Text($"Xuất ngày: {DateTimeOffset.UtcNow.ToLocalTime():dd/MM/yyyy HH:mm}")
                        .FontSize(7).Italic().FontColor(Colors.Grey.Darken1);

                    row.RelativeItem().AlignRight().Text(text =>
                    {
                        text.Span("Trang ").FontSize(7);
                        text.CurrentPageNumber().FontSize(7);
                        text.Span(" / ").FontSize(7);
                        text.TotalPages().FontSize(7);
                    });
                });
            });
        }).GeneratePdf();
    }

    // Add a label/value row to the metadata table
    private static void AddMetaRow(TableDescriptor table, string label, string value)
    {
        table.Cell().Padding(3)
            .Text(label).Bold().FontSize(9);
        table.Cell().Padding(3)
            .Text(value).FontSize(9);
    }

    private static string MapStatusVi(string status) => status switch
    {
        "Pending"  => "Chờ duyệt",
        "Approved" => "Đã duyệt",
        "Rejected" => "Từ chối",
        _          => status
    };
}
