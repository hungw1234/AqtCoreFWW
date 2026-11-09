using AqtCoreFW.Files.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace AqtCoreFW.Files.Infrastructure.Storage;

/// <summary>
/// MinIO S3-compatible storage adapter.
/// Swap for Azure Blob / AWS S3 by replacing this implementation via DI — interface unchanged.
/// F-05: RecyclableMemoryStreamManager used for downloads — avoids LOH pressure for large files.
/// </summary>
public sealed class MinioFileStorageService(
    IMinioClient minioClient,
    RecyclableMemoryStreamManager streamManager,
    ILogger<MinioFileStorageService> logger) : IFileStorageService
{
    public async Task<string> UploadAsync(
        Stream stream,
        string bucket,
        string key,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(bucket, cancellationToken);

        var args = new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(key)
            .WithStreamData(stream)
            .WithObjectSize(stream.Length > 0 ? stream.Length : -1)
            .WithContentType(contentType);

        await minioClient.PutObjectAsync(args, cancellationToken);

        logger.LogDebug("Uploaded object {Key} to bucket {Bucket}", key, bucket);
        return key;
    }

    public async Task<Stream> DownloadAsync(
        string bucket,
        string key,
        CancellationToken cancellationToken = default)
    {
        // HEAD first: turn a missing object or unreachable storage into a thrown error BEFORE any
        // streaming begins, so the caller never ships an empty body as a 200. Previously a GetObject
        // that retrieved nothing (object gone, or MinIO down) left the buffer empty and surfaced as a
        // corrupt 0-byte "success". A missing object becomes FileNotFoundException (→ 404 upstream);
        // connectivity failures propagate as MinioException (→ 5xx).
        long expectedSize;
        try
        {
            var stat = await minioClient.StatObjectAsync(
                new StatObjectArgs().WithBucket(bucket).WithObject(key), cancellationToken);
            expectedSize = stat.Size;
        }
        catch (ObjectNotFoundException)
        {
            throw new FileNotFoundException($"Object '{key}' not found in bucket '{bucket}'.");
        }

        // F-05: RecyclableMemoryStream — avoids MemoryStream LOH allocations for large downloads
        var recyclableStream = streamManager.GetStream("minio-download");
        try
        {
            var args = new GetObjectArgs()
                .WithBucket(bucket)
                .WithObject(key)
                .WithCallbackStream(async (stream, ct) =>
                {
                    await stream.CopyToAsync(recyclableStream, ct);
                });

            await minioClient.GetObjectAsync(args, cancellationToken);

            // Minio SDK 6.x does not throw when the GET itself is refused (e.g. 403 AccessDenied while
            // the HEAD above was allowed): it hands the S3 error XML to the callback as if it were the
            // object. Callers would then serve that XML as the file and the virus scan would certify it
            // clean. A byte count that differs from the HEAD size means we did not read the object.
            if (recyclableStream.Length != expectedSize)
            {
                throw new IOException(
                    $"Read {recyclableStream.Length} bytes for object '{key}' in bucket '{bucket}' but the " +
                    $"object is {expectedSize} bytes; the storage GET was likely refused (check the storage " +
                    "account's read permission).");
            }

            recyclableStream.Position = 0;

            return recyclableStream;
        }
        catch (ObjectNotFoundException)
        {
            // Object existed at the HEAD above but was deleted before the GET (TOCTOU) — map to the
            // same FileNotFoundException so it surfaces as a consistent 404, not a raw 5xx.
            await recyclableStream.DisposeAsync();
            throw new FileNotFoundException($"Object '{key}' not found in bucket '{bucket}'.");
        }
        catch
        {
            // Any other failure (connectivity, cancellation) — release the half-filled buffer before
            // the exception propagates, then let it surface unchanged (→ 5xx).
            await recyclableStream.DisposeAsync();
            throw;
        }
    }

    public async Task DeleteAsync(
        string bucket,
        string key,
        CancellationToken cancellationToken = default)
    {
        var args = new RemoveObjectArgs()
            .WithBucket(bucket)
            .WithObject(key);

        await minioClient.RemoveObjectAsync(args, cancellationToken);
        logger.LogDebug("Deleted object {Key} from bucket {Bucket}", key, bucket);
    }

    public async Task<IReadOnlyList<StorageObjectInfo>> ListObjectsAsync(
        string bucket,
        string prefix,
        CancellationToken cancellationToken = default)
    {
        // A sweep over a non-existent bucket has nothing to reclaim — return empty rather than letting
        // MinIO throw, so the caller does not special-case first-run / empty-environment states.
        var exists = await minioClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(bucket), cancellationToken);
        if (!exists)
            return [];

        var args = new ListObjectsArgs()
            .WithBucket(bucket)
            .WithPrefix(prefix)
            .WithRecursive(true);

        var results = new List<StorageObjectInfo>();
        await foreach (var item in minioClient.ListObjectsEnumAsync(args, cancellationToken))
        {
            if (item.IsDir)
                continue;
            results.Add(new StorageObjectInfo(
                Key:          item.Key,
                LastModified: item.LastModifiedDateTime,
                Size:         (long)item.Size));
        }

        return results;
    }

    private async Task EnsureBucketExistsAsync(string bucket, CancellationToken cancellationToken)
    {
        var exists = await minioClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(bucket), cancellationToken);

        if (!exists)
        {
            await minioClient.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(bucket), cancellationToken);
            logger.LogInformation("Created MinIO bucket {Bucket}", bucket);
        }
    }
}
