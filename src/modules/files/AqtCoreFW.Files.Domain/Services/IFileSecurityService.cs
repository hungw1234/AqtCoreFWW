namespace AqtCoreFW.Files.Domain.Services;

/// <summary>
/// File security validation — magic bytes MIME check, blocklist, zip bomb detection.
/// S8 security requirements: ignore Content-Type header, inspect actual bytes.
/// </summary>
public interface IFileSecurityService
{
    /// <summary>
    /// Validate MIME type by inspecting magic bytes (first 512 bytes).
    /// Ignores Content-Type header — prevents polyglot file attacks.
    /// </summary>
    Task<FileSecurityResult> ValidateMimeTypeAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if filename extension is in blocklist.
    /// Uses last extension only — blocks double extensions like malware.php.jpg.
    /// </summary>
    FileSecurityResult ValidateExtension(string fileName);

    /// <summary>
    /// Detect zip bombs: abort if uncompressed/compressed ratio exceeds 50x.
    /// Only checked for zip/archive content types.
    /// </summary>
    Task<FileSecurityResult> CheckZipBombAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>Generate UUID-based storage key: {tenantId}/{uuid}.{safeExt}.</summary>
    string GenerateStorageKey(Guid tenantId, string originalFileName);
}

public sealed record FileSecurityResult(bool IsValid, string? Reason = null)
{
    public static FileSecurityResult Ok() => new(true);
    public static FileSecurityResult Rejected(string reason) => new(false, reason);
}
