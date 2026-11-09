namespace AqtCoreFW.Forms.Application.Interfaces;

/// <summary>
/// Port for downloading a signed PDF from the signing-docflow bucket. Defined here
/// (Forms.Application) as an interface so Forms has no compile-time dependency on
/// Files.Domain.Services.IFileStorageService, respecting the module-isolation architecture invariant.
///
/// The implementation lives in the API host adapter (cross-module wiring), exactly as
/// <see cref="ISigningOriginalBucketUploader"/> does for the upload side.
///
/// The caller resolves the bucket and key via <see cref="AqtCoreFW.SharedKernel.Contracts.Clients.ISigningRequestClient"/>
/// before calling here — this interface is download-only and performs no tenant checks.
/// </summary>
public interface ISignedDocumentDownloader
{
    /// <summary>
    /// Opens a readable stream for the signed PDF at <paramref name="bucket"/>/<paramref name="key"/>.
    /// The caller is responsible for disposing the returned stream.
    /// Throws <see cref="FileNotFoundException"/> when the object does not exist in storage.
    /// </summary>
    Task<Stream> DownloadAsync(string bucket, string key, CancellationToken cancellationToken = default);
}
