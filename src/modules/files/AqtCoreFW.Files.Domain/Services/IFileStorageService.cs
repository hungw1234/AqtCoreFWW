namespace AqtCoreFW.Files.Domain.Services;

/// <summary>
/// Metadata for a single stored object, returned by <see cref="IFileStorageService.ListObjectsAsync"/>.
/// <para><see cref="LastModified"/> is null when the backend does not report it; callers that gate on
/// age (e.g. orphan reclamation) MUST treat a null timestamp as "do not touch yet".</para>
/// </summary>
public sealed record StorageObjectInfo(string Key, DateTimeOffset? LastModified, long Size);

/// <summary>
/// Object storage abstraction — swap MinIO for Azure Blob / AWS S3 via DI without code changes.
/// All operations are async-streaming; no full buffer in memory.
/// </summary>
public interface IFileStorageService
{
    /// <summary>Upload stream to storage. Returns the storage key used.</summary>
    Task<string> UploadAsync(Stream stream, string bucket, string key, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Download stream from storage. Caller is responsible for disposing the stream.</summary>
    Task<Stream> DownloadAsync(string bucket, string key, CancellationToken cancellationToken = default);

    /// <summary>Permanently delete object from storage.</summary>
    Task DeleteAsync(string bucket, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists objects under <paramref name="prefix"/> in <paramref name="bucket"/> (recursive).
    /// Returns an empty list when the bucket does not exist. Used by reclamation sweeps that must
    /// enumerate stored blobs and reconcile them against the database (the storage key of an
    /// orphaned blob is not recorded anywhere else, so it can only be discovered by listing).
    /// </summary>
    Task<IReadOnlyList<StorageObjectInfo>> ListObjectsAsync(string bucket, string prefix, CancellationToken cancellationToken = default);
}
