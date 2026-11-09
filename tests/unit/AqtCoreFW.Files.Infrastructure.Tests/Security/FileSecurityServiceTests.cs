using System.IO.Compression;
using AqtCoreFW.Files.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Files.Infrastructure.Tests.Security;

/// <summary>
/// Unit tests for FileSecurityService — pure logic, no external dependencies.
/// Covers extension blocklist, magic bytes MIME validation, zip bomb detection,
/// and UUID storage key generation.
/// FileSecurityService is instantiated directly; only ILogger is mocked.
/// </summary>
public sealed class FileSecurityServiceTests
{
    private readonly FileSecurityService _service;

    public FileSecurityServiceTests()
    {
        _service = new FileSecurityService(Substitute.For<ILogger<FileSecurityService>>());
    }

    // --- Extension validation: allowed ---

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("photo.jpg")]
    [InlineData("image.png")]
    [InlineData("spreadsheet.docx")]
    [InlineData("data.xlsx")]
    [InlineData("archive.zip")]
    public void ValidateExtension_AllowedExtension_ReturnsOk(string fileName)
    {
        var result = _service.ValidateExtension(fileName);

        result.IsValid.Should().BeTrue();
    }

    // --- Extension validation: blocked ---

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("script.bat")]
    [InlineData("payload.php")]
    [InlineData("code.js")]
    [InlineData("vector.svg")]
    [InlineData("shell.sh")]
    [InlineData("command.cmd")]
    [InlineData("vbscript.vbs")]
    public void ValidateExtension_BlockedExtension_ReturnsRejected(string fileName)
    {
        var result = _service.ValidateExtension(fileName);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateExtension_NoExtension_ReturnsRejected()
    {
        var result = _service.ValidateExtension("filename_no_extension");

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("extension");
    }

    [Fact]
    public void ValidateExtension_LastExtensionUsed_DoubleExtensionBlockedByLastExt()
    {
        // malware.php.jpg — last extension is .jpg (allowed), .php part is irrelevant
        var result = _service.ValidateExtension("malware.php.jpg");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateExtension_LastExtensionUsed_DoubleExtensionBlockedIfLastExtIsBlocked()
    {
        // malware.pdf.exe — last extension is .exe (blocked)
        var result = _service.ValidateExtension("malware.pdf.exe");

        result.IsValid.Should().BeFalse();
    }

    // --- MIME validation: magic bytes ---

    [Fact]
    public async Task ValidateMimeType_PdfMagicBytes_ReturnsOk()
    {
        // %PDF magic: 25 50 44 46
        var stream = MakeStream(0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34);

        var result = await _service.ValidateMimeTypeAsync(stream);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateMimeType_JpegMagicBytes_ReturnsOk()
    {
        // JPEG magic: FF D8 FF
        var stream = MakeStream(0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10);

        var result = await _service.ValidateMimeTypeAsync(stream);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateMimeType_PngMagicBytes_ReturnsOk()
    {
        // PNG magic: 89 50 4E 47
        var stream = MakeStream(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);

        var result = await _service.ValidateMimeTypeAsync(stream);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateMimeType_ZipMagicBytes_ReturnsOk()
    {
        // ZIP PK magic: 50 4B 03 04
        var stream = MakeStream(0x50, 0x4B, 0x03, 0x04, 0x14, 0x00);

        var result = await _service.ValidateMimeTypeAsync(stream);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateMimeType_FileTooSmall_ReturnsRejected()
    {
        // Only 3 bytes — too small to validate (needs at least 4)
        var stream = MakeStream(0x01, 0x02, 0x03);

        var result = await _service.ValidateMimeTypeAsync(stream);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("small");
    }

    [Fact]
    public async Task ValidateMimeType_UnknownMagicBytes_ReturnsOk()
    {
        // Unknown magic bytes — passed to ClamAV for deeper inspection, not blocked
        var stream = MakeStream(0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03);

        var result = await _service.ValidateMimeTypeAsync(stream);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateMimeType_RewinsStreamAfterValidation()
    {
        var stream = MakeStream(0x25, 0x50, 0x44, 0x46, 0x00); // PDF magic

        await _service.ValidateMimeTypeAsync(stream);

        // Stream position must be restored so subsequent reads (SHA-256, upload) work
        stream.Position.Should().Be(0);
    }

    // --- Zip bomb detection ---

    [Fact]
    public async Task CheckZipBomb_NonZipFile_ReturnsOk()
    {
        // Does not start with PK — treated as non-ZIP
        var stream = MakeStream(0x25, 0x50, 0x44, 0x46, 0x00, 0x00, 0x00, 0x00);

        var result = await _service.CheckZipBombAsync(stream);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CheckZipBomb_NormalZip_ReturnsOk()
    {
        var stream = CreateNormalZip("hello.txt", "Hello, World!");

        var result = await _service.CheckZipBombAsync(stream);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CheckZipBomb_RewinsStreamAfterCheck()
    {
        var stream = CreateNormalZip("test.txt", "test content");
        var originalPosition = stream.Position;

        await _service.CheckZipBombAsync(stream);

        stream.Position.Should().Be(originalPosition);
    }

    // --- Storage key generation ---

    [Fact]
    public void GenerateStorageKey_IncludesTenantIdAndSafeExtension()
    {
        var tenantId = Guid.NewGuid();

        var key = _service.GenerateStorageKey(tenantId, "report.pdf");

        key.Should().StartWith(tenantId.ToString());
        key.Should().EndWith(".pdf");
    }

    [Fact]
    public void GenerateStorageKey_EachCallGeneratesDifferentKey()
    {
        var tenantId = Guid.NewGuid();

        var key1 = _service.GenerateStorageKey(tenantId, "file.pdf");
        var key2 = _service.GenerateStorageKey(tenantId, "file.pdf");

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void GenerateStorageKey_BlockedExtension_UsesBinExtension()
    {
        var tenantId = Guid.NewGuid();

        // Even if somehow a blocked ext slips through, storage key uses .bin
        var key = _service.GenerateStorageKey(tenantId, "malware.exe");

        key.Should().EndWith(".bin");
    }

    [Fact]
    public void GenerateStorageKey_ContainsUuidSegment()
    {
        var tenantId = Guid.NewGuid();

        var key = _service.GenerateStorageKey(tenantId, "file.pdf");

        // Format: {tenantId}/{uuid}.pdf — split on '/' gives [{tenantId}, {uuid}.pdf]
        var parts = key.Split('/');
        parts.Should().HaveCount(2);
        var uuidPart = Path.GetFileNameWithoutExtension(parts[1]);
        Guid.TryParse(uuidPart, out _).Should().BeTrue("storage key UUID segment must be a valid Guid");
    }

    // --- Helpers ---

    private static MemoryStream MakeStream(params byte[] bytes)
    {
        var ms = new MemoryStream(bytes);
        ms.Position = 0;
        return ms;
    }

    /// <summary>Creates a valid small ZIP in memory to test non-bomb detection.</summary>
    private static MemoryStream CreateNormalZip(string entryName, string content)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new System.IO.StreamWriter(entry.Open());
            writer.Write(content);
        }
        ms.Position = 0;
        return ms;
    }
}
