using AqtCoreFW.Reporting.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Reporting.Infrastructure.Tests.Services;

/// <summary>
/// TC-RPT-I005: ReportStorageService stores and retrieves files.
/// Also verifies path-traversal protection.
/// </summary>
public sealed class ReportStorageServiceTests : IDisposable
{
    private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"rpt_tests_{Guid.NewGuid():N}");
    private readonly ReportStorageService _sut;

    public ReportStorageServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reporting:StoragePath"] = _basePath
            })
            .Build();

        _sut = new ReportStorageService(config, NullLogger<ReportStorageService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
            Directory.Delete(_basePath, recursive: true);
    }

    [Fact]
    public async Task UploadAsync_StoresFileOnDisk_ReturnsFilePath()
    {
        // TC-RPT-I005: upload stores bytes and returns the stored path
        var content = "report content"u8.ToArray();

        var path = await _sut.UploadAsync(content, "report.xlsx", "tenant-1");

        path.Should().NotBeNullOrEmpty();
        File.Exists(path).Should().BeTrue();
        File.ReadAllBytes(path).Should().Equal(content);
    }

    [Fact]
    public async Task GetDownloadUrlAsync_ExistingFile_ReturnsFilePath()
    {
        // TC-RPT-I005: retrieve returns path for an existing file
        var content = "pdf data"u8.ToArray();
        var storedPath = await _sut.UploadAsync(content, "report.pdf", "tenant-2");

        var url = await _sut.GetDownloadUrlAsync(storedPath);

        url.Should().Be(storedPath);
    }

    [Fact]
    public async Task GetDownloadUrlAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        var act = async () => await _sut.GetDownloadUrlAsync("/nonexistent/path/file.pdf");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task UploadAsync_PathTraversalInFileName_SanitizesFileName()
    {
        // Security: ../../../etc/passwd should be sanitized to just "passwd"
        var content = "data"u8.ToArray();
        var path = await _sut.UploadAsync(content, "../../../etc/passwd", "tenant-1");

        path.Should().NotContain("..");
        Path.GetFileName(path).Should().Be("passwd");
    }

    [Fact]
    public void IsPathAllowed_FileUnderBasePath_ReturnsTrue()
    {
        var filePath = Path.Combine(_basePath, "tenant-1", "report.xlsx");

        _sut.IsPathAllowed(filePath).Should().BeTrue();
    }

    [Fact]
    public void IsPathAllowed_FileOutsideBasePath_ReturnsFalse()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), "other", "file.xlsx");

        _sut.IsPathAllowed(outsidePath).Should().BeFalse();
    }
}
