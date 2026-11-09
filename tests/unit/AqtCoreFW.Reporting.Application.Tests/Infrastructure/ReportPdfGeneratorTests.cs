using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Infrastructure.Export;
using QuestPDF.Infrastructure;

namespace AqtCoreFW.Reporting.Application.Tests.Infrastructure;

/// <summary>
/// Integration-style tests for ReportPdfGenerator.
/// Uses real QuestPDF — no mocks — to verify PDF bytes are valid.
/// QuestPDF Community licence is set in the static constructor so it applies
/// before any test in this class runs.
/// </summary>
public sealed class ReportPdfGeneratorTests
{
    static ReportPdfGeneratorTests()
    {
        // Required by QuestPDF — must be set before GeneratePdf() is called.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly ReportPdfGenerator _sut = new();

    private static readonly IReadOnlyList<string> Columns = ["Id", "Title", "Status"];

    private static readonly ReportMetadata Metadata = new(
        TenantId: "tenant-001",
        FromDate: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        ToDate: new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero),
        GeneratedAt: new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GenerateAsync_WithData_ReturnsValidPdfBytes()
    {
        // Arrange — two data rows
        var rows = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["Id"] = 1, ["Title"] = "Case A", ["Status"] = "Open" },
            new Dictionary<string, object?> { ["Id"] = 2, ["Title"] = "Case B", ["Status"] = "Closed" }
        };

        // Act
        var bytes = await _sut.GenerateAsync(
            reportName: "Cases Report",
            reportNameVi: "Báo cáo vụ việc",
            columnNames: Columns,
            rows: rows,
            metadata: Metadata);

        // Assert — PDF magic bytes: %PDF
        bytes.Should().NotBeNullOrEmpty();
        bytes[0].Should().Be(0x25); // '%'
        bytes[1].Should().Be(0x50); // 'P'
        bytes[2].Should().Be(0x44); // 'D'
        bytes[3].Should().Be(0x46); // 'F'
    }

    [Fact]
    public async Task GenerateAsync_EmptyRows_ReturnsHeaderOnlyPdf()
    {
        // Arrange — no data rows
        var rows = new List<IDictionary<string, object?>>();

        // Act
        var bytes = await _sut.GenerateAsync(
            reportName: "Empty Report",
            reportNameVi: "Báo cáo rỗng",
            columnNames: Columns,
            rows: rows,
            metadata: Metadata);

        // Assert — still a valid PDF (magic bytes present)
        bytes.Should().NotBeNullOrEmpty();
        bytes[0].Should().Be(0x25); // '%'
        bytes[1].Should().Be(0x50); // 'P'
        bytes[2].Should().Be(0x44); // 'D'
        bytes[3].Should().Be(0x46); // 'F'
    }
}
