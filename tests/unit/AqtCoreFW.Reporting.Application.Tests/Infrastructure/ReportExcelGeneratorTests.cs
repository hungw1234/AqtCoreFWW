using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Infrastructure.Export;

namespace AqtCoreFW.Reporting.Application.Tests.Infrastructure;

/// <summary>
/// Integration-style tests for ReportExcelGenerator.
/// Uses real ClosedXML — no mocks — to verify XLSX bytes are valid.
/// </summary>
public sealed class ReportExcelGeneratorTests
{
    private readonly ReportExcelGenerator _sut = new();

    private static readonly IReadOnlyList<string> Columns = ["Id", "Title", "Status"];

    private static readonly ReportMetadata Metadata = new(
        TenantId: "tenant-001",
        FromDate: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        ToDate: new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero),
        GeneratedAt: new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GenerateAsync_WithData_ReturnsValidXlsxBytes()
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

        // Assert — ZIP/XLSX magic bytes: PK\x03\x04
        bytes.Should().NotBeNullOrEmpty();
        bytes.Length.Should().BeGreaterThan(0);
        bytes[0].Should().Be(0x50); // 'P'
        bytes[1].Should().Be(0x4B); // 'K'
    }

    [Fact]
    public async Task GenerateAsync_EmptyRows_ReturnsHeaderOnlyWorkbook()
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

        // Assert — still a valid XLSX (PK magic bytes present)
        bytes.Should().NotBeNullOrEmpty();
        bytes[0].Should().Be(0x50); // 'P'
        bytes[1].Should().Be(0x4B); // 'K'
    }
}
