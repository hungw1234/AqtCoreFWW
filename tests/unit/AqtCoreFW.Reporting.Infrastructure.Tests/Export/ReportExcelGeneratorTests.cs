using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Infrastructure.Export;
using FluentAssertions;

namespace AqtCoreFW.Reporting.Infrastructure.Tests.Export;

/// <summary>
/// TC-RPT-I001: ReportExcelGenerator produces valid workbook bytes.
/// TC-RPT-I002 (partial): CompliancePdfGenerator headers/footers tested via CompliancePdfGeneratorTests.
/// </summary>
public sealed class ReportExcelGeneratorTests
{
    private readonly ReportExcelGenerator _sut = new();
    private readonly ReportMetadata _metadata = new(
        TenantId: "tenant-1",
        FromDate: DateTimeOffset.UtcNow.AddDays(-30),
        ToDate: DateTimeOffset.UtcNow,
        GeneratedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task GenerateAsync_WithData_ReturnNonEmptyBytes()
    {
        // TC-RPT-I001: result must be valid Excel bytes (non-empty, starts with PK zip header)
        var columns = new List<string> { "Mã hồ sơ", "Tên", "Ngày tạo" };
        var rows = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["Mã hồ sơ"] = "HS-001", ["Tên"] = "Nguyễn A", ["Ngày tạo"] = DateTime.UtcNow },
            new Dictionary<string, object?> { ["Mã hồ sơ"] = "HS-002", ["Tên"] = "Trần B",   ["Ngày tạo"] = DateTime.UtcNow }
        };

        var bytes = await _sut.GenerateAsync("TestReport", "Báo cáo thử nghiệm", columns, rows, _metadata);

        bytes.Should().NotBeNullOrEmpty();
        // ZIP magic bytes: PK\x03\x04
        bytes[0].Should().Be(0x50); // P
        bytes[1].Should().Be(0x4B); // K
    }

    [Fact]
    public async Task GenerateAsync_EmptyRows_ProducesValidWorkbook()
    {
        // TC-RPT-I001 (empty path): zero rows must still produce a valid file
        var columns = new List<string> { "Col1", "Col2" };
        var rows = new List<IDictionary<string, object?>>();

        var bytes = await _sut.GenerateAsync("Empty", "Trống", columns, rows, _metadata);

        bytes.Should().NotBeNullOrEmpty();
        bytes[0].Should().Be(0x50); // PK header
    }

    [Fact]
    public async Task GenerateAsync_ExceedsMaxRows_ThrowsInvalidOperationException()
    {
        // Max 50k rows enforced
        var columns = new List<string> { "Col" };
        var rows = Enumerable.Range(0, 50_001)
            .Select(_ => (IDictionary<string, object?>)new Dictionary<string, object?> { ["Col"] = "x" })
            .ToList();

        var act = async () => await _sut.GenerateAsync("Big", "Lớn", columns, rows, _metadata);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*50000*");
    }

    [Fact]
    public async Task GenerateAsync_NullCellValues_DoesNotThrow()
    {
        var columns = new List<string> { "NullCol" };
        var rows = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["NullCol"] = null }
        };

        var act = async () => await _sut.GenerateAsync("Nulls", "Null", columns, rows, _metadata);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(42)]
    [InlineData(3.14)]
    [InlineData(true)]
    public async Task GenerateAsync_VariousCellTypes_ProducesValidWorkbook(object cellValue)
    {
        var columns = new List<string> { "Col" };
        var rows = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["Col"] = cellValue }
        };

        var bytes = await _sut.GenerateAsync("Types", "Loại", columns, rows, _metadata);

        bytes.Should().NotBeNullOrEmpty();
    }
}
