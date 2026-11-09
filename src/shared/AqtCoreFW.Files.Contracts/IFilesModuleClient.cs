namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module interface for file metadata access.
/// Monolith: InProcessFilesModuleClient (direct DB query, zero overhead).
/// Microservice: GrpcFilesModuleClient (when module extracted — no .proto in monolith).
/// </summary>
public interface IFilesModuleClient
{
    /// <summary>Check if a file exists and is available (scan passed) for a given tenant.</summary>
    Task<bool> IsFileAvailableAsync(Guid fileId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Get basic file info for cross-module references (e.g. Cases attachment).</summary>
    Task<FileReferenceInfo?> GetFileReferenceAsync(Guid fileId, Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed record FileReferenceInfo(
    Guid FileId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Status);
