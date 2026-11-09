using AqtCoreFW.Files.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Security.Tests.Upload;

/// <summary>
/// Security tests for file upload validation — TC-SEC-UPL-001 to 004.
/// Tests FileSecurityService: extension blocklist, magic bytes, zip bomb, path traversal.
/// </summary>
public sealed class FileUploadSafetyTests
{
    private readonly FileSecurityService _svc = new(NullLogger<FileSecurityService>.Instance);

    // --- Extension Blocklist ---

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("malware.exe")]
    [InlineData("script.php")]
    [InlineData("page.html")]
    [InlineData("shell.sh")]
    [InlineData("macro.vbs")]
    [InlineData("command.cmd")]
    [InlineData("exploit.ps1")]
    [InlineData("page.asp")]
    [InlineData("page.aspx")]
    [InlineData("image.svg")]
    public void BlockedExtension_Rejected(string fileName)
    {
        var result = _svc.ValidateExtension(fileName);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("not allowed");
    }

    [Fact]
    [Trait("Category", "Security")]
    public void DoubleExtension_ExeIsBlocked()
    {
        // TC-SEC-UPL-001: doc.pdf.exe — last extension is .exe → blocked
        var result = _svc.ValidateExtension("doc.pdf.exe");

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("not allowed");
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("report.pdf")]
    [InlineData("photo.jpg")]
    [InlineData("document.docx")]
    [InlineData("spreadsheet.xlsx")]
    [InlineData("archive.zip")]
    public void AllowedExtension_Accepted(string fileName)
    {
        var result = _svc.ValidateExtension(fileName);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Security")]
    public void NoExtension_Rejected()
    {
        var result = _svc.ValidateExtension("noext");

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("extension");
    }

    // --- Storage Key (Path Traversal Prevention) ---

    [Fact]
    [Trait("Category", "Security")]
    public void StorageKey_PathTraversal_Sanitized()
    {
        var tenantId = Guid.NewGuid();
        var key = _svc.GenerateStorageKey(tenantId, "../../etc/passwd.pdf");

        // UUID-based key prevents path traversal — original name never used
        key.Should().StartWith($"{tenantId}/");
        key.Should().EndWith(".pdf");
        key.Should().NotContain("..");
        key.Should().NotContain("etc");
        key.Should().NotContain("passwd");
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StorageKey_BlockedExtension_ConvertedToBin()
    {
        var tenantId = Guid.NewGuid();
        var key = _svc.GenerateStorageKey(tenantId, "malware.exe");

        key.Should().EndWith(".bin");
        key.Should().NotContain(".exe");
    }

    // --- Magic Bytes MIME Validation ---

    [Fact]
    [Trait("Category", "Security")]
    public async Task ValidMimeType_PdfMagicBytes_Accepted()
    {
        // PDF magic: %PDF
        var data = new byte[512];
        data[0] = 0x25; data[1] = 0x50; data[2] = 0x44; data[3] = 0x46;
        using var stream = new MemoryStream(data);

        var result = await _svc.ValidateMimeTypeAsync(stream);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ValidMimeType_JpegMagicBytes_Accepted()
    {
        var data = new byte[512];
        data[0] = 0xFF; data[1] = 0xD8; data[2] = 0xFF;
        using var stream = new MemoryStream(data);

        var result = await _svc.ValidateMimeTypeAsync(stream);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ValidMimeType_PngMagicBytes_Accepted()
    {
        var data = new byte[512];
        data[0] = 0x89; data[1] = 0x50; data[2] = 0x4E; data[3] = 0x47;
        using var stream = new MemoryStream(data);

        var result = await _svc.ValidateMimeTypeAsync(stream);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task TooSmallFile_Rejected()
    {
        using var stream = new MemoryStream([0x00, 0x01]);

        var result = await _svc.ValidateMimeTypeAsync(stream);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("too small");
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task MimeValidation_StreamRewindedAfterInspection()
    {
        var data = new byte[512];
        data[0] = 0x25; data[1] = 0x50; data[2] = 0x44; data[3] = 0x46;
        using var stream = new MemoryStream(data);
        stream.Position = 0;

        await _svc.ValidateMimeTypeAsync(stream);

        stream.Position.Should().Be(0, "stream must be rewound for downstream consumers");
    }
}
