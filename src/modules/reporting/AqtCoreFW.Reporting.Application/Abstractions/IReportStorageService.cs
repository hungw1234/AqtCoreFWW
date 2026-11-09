namespace AqtCoreFW.Reporting.Application.Abstractions;

/// <summary>
/// Stores and retrieves generated report files.
/// Implementation wraps MinIO/local storage in Infrastructure.
/// </summary>
public interface IReportStorageService
{
    /// <summary>Upload report file, return stored file path.</summary>
    Task<string> UploadAsync(byte[] content, string fileName, string tenantId, CancellationToken ct = default);

    /// <summary>Get presigned download URL for a stored report file.</summary>
    Task<string> GetDownloadUrlAsync(string filePath, CancellationToken ct = default);

    /// <summary>Validate that a file path is under the allowed base directory (path traversal prevention).</summary>
    bool IsPathAllowed(string filePath);
}
