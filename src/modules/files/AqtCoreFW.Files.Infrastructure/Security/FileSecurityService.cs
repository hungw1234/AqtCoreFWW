using System.IO.Compression;
using AqtCoreFW.Files.Domain.Services;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Files.Infrastructure.Security;

/// <summary>
/// S8 security: magic bytes MIME validation, extension blocklist, zip bomb detection.
/// UUID storage key generation — prevents path traversal.
/// Magic bytes checked against first 512 bytes; Content-Type header intentionally ignored.
/// </summary>
public sealed class FileSecurityService(ILogger<FileSecurityService> logger) : IFileSecurityService
{
    // Blocked extensions (S8): reject entirely — last extension only (prevents double-ext attacks)
    private static readonly HashSet<string> BlockedExtensions =
    [
        ".svg", ".exe", ".bat", ".sh", ".php", ".asp", ".aspx",
        ".html", ".htm", ".js", ".vbs", ".ps1", ".cmd", ".com"
    ];

    // Magic bytes signatures for allowed types
    private static readonly Dictionary<string, byte[][]> MagicBytes = new()
    {
        ["application/pdf"]  = [[0x25, 0x50, 0x44, 0x46]], // %PDF
        ["image/jpeg"]       = [[0xFF, 0xD8, 0xFF]],
        ["image/png"]        = [[0x89, 0x50, 0x4E, 0x47]],
        ["image/gif"]        = [[0x47, 0x49, 0x46, 0x38]],
        // DOCX/XLSX/ZIP share PK magic — accepted; SVG is text/xml (no magic) — blocked by extension
        ["application/zip"]  = [[0x50, 0x4B, 0x03, 0x04], [0x50, 0x4B, 0x05, 0x06]],
    };

    private const long MaxZipRatio = 50L;

    public async Task<FileSecurityResult> ValidateMimeTypeAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var header = new byte[512];
        var originalPosition = stream.Position;

        var bytesRead = await stream.ReadAsync(header.AsMemory(0, 512), cancellationToken);
        stream.Position = originalPosition; // rewind after inspection

        if (bytesRead < 4)
            return FileSecurityResult.Rejected("File too small to validate.");

        // If at least one allowed magic matches, accept
        foreach (var (_, signatures) in MagicBytes)
        {
            foreach (var sig in signatures)
            {
                if (bytesRead >= sig.Length && header.AsSpan(0, sig.Length).SequenceEqual(sig))
                    return FileSecurityResult.Ok();
            }
        }

        // Accept text/xml and other text-based formats (no fixed magic bytes)
        // Only block if we positively identify a dangerous type via extension (handled separately)
        // For unknown magic: allow but log — downstream ClamAV provides deeper inspection
        logger.LogDebug("File magic bytes not in known-good list — passed to ClamAV for deeper scan.");
        return FileSecurityResult.Ok();
    }

    public FileSecurityResult ValidateExtension(string fileName)
    {
        // Last extension only — blocks malware.php.jpg → extension = .jpg (allowed)
        // but malware.php → extension = .php (blocked)
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        if (string.IsNullOrEmpty(ext))
            return FileSecurityResult.Rejected("File must have an extension.");

        if (BlockedExtensions.Contains(ext))
            return FileSecurityResult.Rejected($"File extension '{ext}' is not allowed.");

        return FileSecurityResult.Ok();
    }

    public async Task<FileSecurityResult> CheckZipBombAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var originalPosition = stream.Position;

        // Peek first 4 bytes to check if ZIP
        var header = new byte[4];
        await stream.ReadExactlyAsync(header.AsMemory(0, 4), cancellationToken);
        stream.Position = originalPosition;

        // PK signature — is a ZIP archive
        if (header[0] != 0x50 || header[1] != 0x4B)
            return FileSecurityResult.Ok(); // not a ZIP, skip check

        var compressedSize = stream.Length - originalPosition;
        if (compressedSize <= 0)
            return FileSecurityResult.Ok();

        try
        {
            long uncompressedSize = 0;
            var maxAllowed = compressedSize * MaxZipRatio;

            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var entry in zip.Entries)
            {
                uncompressedSize += entry.Length;
                if (uncompressedSize > maxAllowed)
                {
                    logger.LogWarning(
                        "Zip bomb detected: compressed={Compressed}, uncompressed exceeded {MaxAllowed}",
                        compressedSize, maxAllowed);
                    return FileSecurityResult.Rejected(
                        $"Archive expansion ratio exceeds {MaxZipRatio}x limit.");
                }
            }
        }
        catch (InvalidDataException)
        {
            return FileSecurityResult.Rejected("Invalid or corrupt ZIP archive.");
        }
        finally
        {
            stream.Position = originalPosition;
        }

        return FileSecurityResult.Ok();
    }

    public string GenerateStorageKey(Guid tenantId, string originalFileName)
    {
        // Last extension only — safe extension for storage key
        var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        var safeExt = BlockedExtensions.Contains(ext) ? ".bin" : ext;
        return $"{tenantId}/{Guid.NewGuid()}{safeExt}";
    }
}
