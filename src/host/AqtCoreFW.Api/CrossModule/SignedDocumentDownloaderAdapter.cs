using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Forms.Application.Interfaces;

namespace AqtCoreFW.Api.CrossModule;

/// <summary>
/// Host-resident adapter that binds <see cref="ISignedDocumentDownloader"/> (Forms.Application
/// interface) to <see cref="IFileStorageService"/> (Files.Domain implementation).
///
/// Lives in the API host — the only composition root permitted to hold compile-time references
/// to both the Files module and the Forms module simultaneously. This avoids a direct
/// Forms.Infrastructure → Files.Domain compile-time dependency, which would violate the
/// module-isolation architecture invariant enforced by ModuleIsolationTests.
///
/// Registered in Program.cs after both Forms and Files module DI extensions are invoked.
/// </summary>
internal sealed class SignedDocumentDownloaderAdapter(IFileStorageService fileStorage)
    : ISignedDocumentDownloader
{
    /// <inheritdoc />
    public Task<Stream> DownloadAsync(
        string bucket,
        string key,
        CancellationToken cancellationToken = default)
        => fileStorage.DownloadAsync(bucket, key, cancellationToken);
}
