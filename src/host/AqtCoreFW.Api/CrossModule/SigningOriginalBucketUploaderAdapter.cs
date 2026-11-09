using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Forms.Application.Interfaces;

namespace AqtCoreFW.Api.CrossModule;

/// <summary>
/// Host-resident adapter that binds <see cref="ISigningOriginalBucketUploader"/> (Forms.Application
/// interface) to <see cref="IFileStorageService"/> (Files.Domain implementation).
///
/// Lives in the API host — the only composition root permitted to hold compile-time references
/// to both the Files module and the Forms module simultaneously. This avoids a direct
/// Forms.Infrastructure → Files.Domain compile-time dependency, which would violate the
/// module-isolation architecture invariant enforced by ModuleIsolationTests.
///
/// Registered in Program.cs after both Forms and Files module DI extensions are invoked.
/// </summary>
internal sealed class SigningOriginalBucketUploaderAdapter(IFileStorageService fileStorage)
    : ISigningOriginalBucketUploader
{
    // Bucket name must match the constant in StageFileForSigningCommandHandler and
    // FileUploadHandlerController — all three reference the same MinIO bucket.
    private const string SigningOriginalBucket = "signing-original";

    /// <inheritdoc />
    public async Task<string> UploadAsync(
        byte[] bytes,
        string key,
        CancellationToken cancellationToken = default)
    {
        // Wrap bytes in a MemoryStream — IFileStorageService.UploadAsync is stream-based.
        // Using a MemoryStream is safe here: QuestPDF PDFs for form submissions are small
        // (a few hundred KB at most); no large-file streaming concern at this layer.
        using var stream = new MemoryStream(bytes, writable: false);
        return await fileStorage.UploadAsync(
            stream, SigningOriginalBucket, key, "application/pdf", cancellationToken);
    }
}
