using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Application.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AqtCoreFW.Reporting.Infrastructure.Export;

/// <summary>
/// Generates GOV compliance evidence PDF using QuestPDF.
/// Sections: overview, audit summary, security incidents, access review, data retention.
/// </summary>
public sealed class CompliancePdfGenerator : ICompliancePdfGenerator
{
    public Task<byte[]> GenerateAsync(ComplianceEvidenceDto data, CancellationToken ct = default)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                // --- Header ---
                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text("BÁO CÁO TUÂN THỦ")
                        .Bold().FontSize(18);
                    col.Item().AlignCenter().Text($"Framework: {data.Framework}")
                        .FontSize(12);
                    col.Item().AlignCenter().Text($"Ngày tạo: {data.GeneratedAt:dd/MM/yyyy HH:mm}")
                        .Italic().FontSize(9);
                    col.Item().AlignCenter().Text($"Người tạo: {data.GeneratedBy}")
                        .FontSize(9);
                    col.Item().PaddingBottom(12);
                });

                // --- Content ---
                page.Content().Column(col =>
                {
                    // Audit Summary
                    col.Item().Text("1. Tổng quan kiểm toán").Bold().FontSize(12);
                    col.Item().PaddingBottom(4);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(1);
                        });
                        AddSummaryRow(table, "Tổng sự kiện", data.AuditSummary.TotalEvents.ToString("N0"));
                        AddSummaryRow(table, "Đăng nhập", data.AuditSummary.LoginEvents.ToString("N0"));
                        AddSummaryRow(table, "Truy cập dữ liệu", data.AuditSummary.DataAccessEvents.ToString("N0"));
                        AddSummaryRow(table, "Quản trị", data.AuditSummary.AdminEvents.ToString("N0"));
                        AddSummaryRow(table, "Giai đoạn",
                            $"{data.AuditSummary.PeriodFrom:dd/MM/yyyy} - {data.AuditSummary.PeriodTo:dd/MM/yyyy}");
                    });

                    col.Item().PaddingVertical(10);

                    // Security Incidents
                    col.Item().Text("2. Sự cố bảo mật").Bold().FontSize(12);
                    col.Item().PaddingBottom(4);
                    if (data.SecurityIncidents.Count == 0)
                    {
                        col.Item().Text("Không có sự cố trong giai đoạn báo cáo.").Italic();
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                                c.RelativeColumn(3);
                                c.RelativeColumn(1);
                            });

                            // Header
                            foreach (var h in new[] { "Mã", "Mức độ", "Trạng thái", "Mô tả", "Thời gian" })
                            {
                                table.Cell().Background(Colors.Grey.Darken3).Padding(3)
                                    .Text(h).Bold().FontColor(Colors.White).FontSize(8);
                            }

                            foreach (var incident in data.SecurityIncidents)
                            {
                                table.Cell().Padding(2).Text(incident.IncidentId).FontSize(8);
                                table.Cell().Padding(2).Text(incident.Severity).FontSize(8);
                                table.Cell().Padding(2).Text(incident.Status).FontSize(8);
                                table.Cell().Padding(2).Text(incident.Description).FontSize(8);
                                table.Cell().Padding(2).Text(incident.OccurredAt.ToString("dd/MM/yyyy")).FontSize(8);
                            }
                        });
                    }

                    col.Item().PaddingVertical(10);

                    // Access Review
                    col.Item().Text("3. Đánh giá quyền truy cập").Bold().FontSize(12);
                    col.Item().PaddingBottom(4);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(1);
                        });
                        AddSummaryRow(table, "Tổng đã đánh giá", data.AccessReviewStatus.TotalReviewed.ToString("N0"));
                        AddSummaryRow(table, "Phê duyệt", data.AccessReviewStatus.Approved.ToString("N0"));
                        AddSummaryRow(table, "Thu hồi", data.AccessReviewStatus.Revoked.ToString("N0"));
                        AddSummaryRow(table, "Đánh giá gần nhất", data.AccessReviewStatus.LastReviewDate.ToString("dd/MM/yyyy"));
                    });

                    col.Item().PaddingVertical(10);

                    // Data Retention
                    col.Item().Text("4. Lưu trữ dữ liệu").Bold().FontSize(12);
                    col.Item().PaddingBottom(4);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(1);
                        });
                        AddSummaryRow(table, "Tổng bản ghi", data.DataRetentionStatus.TotalRecords.ToString("N0"));
                        AddSummaryRow(table, "Đã lưu trữ", data.DataRetentionStatus.ArchivedRecords.ToString("N0"));
                        AddSummaryRow(table, "Đã xoá", data.DataRetentionStatus.PurgedRecords.ToString("N0"));
                        AddSummaryRow(table, "Chờ xoá", data.DataRetentionStatus.PendingPurge.ToString("N0"));
                    });
                });

                // --- Footer ---
                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text("Tài liệu nội bộ — Bảo mật")
                        .Italic().FontSize(7);
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

    private static void AddSummaryRow(TableDescriptor table, string label, string value)
    {
        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .Padding(3).Text(label).FontSize(9);
        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .Padding(3).AlignRight().Text(value).FontSize(9).Bold();
    }
}
