using AqtCoreFW.Reporting.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Reporting.Infrastructure.Services;

/// <summary>
/// Stores generated report files to local disk with path-traversal protection.
/// For production, replace with MinIO/S3 implementation.
/// Files stored under: {BasePath}/{tenantId}/{fileName}
/// </summary>
public sealed class ReportStorageService(
    IConfiguration configuration,
    ILogger<ReportStorageService> logger) : IReportStorageService
{
    private string BasePath => configuration["Reporting:StoragePath"]
        ?? Path.Combine(Path.GetTempPath(), "aqtcorefw-reports");

    public async Task<string> UploadAsync(
        byte[] content, string fileName, string tenantId, CancellationToken ct = default)
    {
        // Sanitize fileName and tenantId to prevent path traversal
        var safeFileName = Path.GetFileName(fileName);
        var safeTenantId = Path.GetFileName(tenantId);

        var dir = Path.Combine(BasePath, safeTenantId);
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, safeFileName);
        await File.WriteAllBytesAsync(filePath, content, ct);

        logger.LogInformation("Report file stored: {FilePath} ({Size} bytes)", filePath, content.Length);
        return filePath;
    }

    public Task<string> GetDownloadUrlAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Report file not found.", filePath);

        return Task.FromResult(filePath);
    }

    /// <summary>Verify file path is under the allowed base directory (path traversal prevention).</summary>
    public bool IsPathAllowed(string filePath)
    {
        var canonical = Path.GetFullPath(filePath);
        var allowedBase = Path.GetFullPath(BasePath);
        return canonical.StartsWith(allowedBase, StringComparison.OrdinalIgnoreCase);
    }
}
