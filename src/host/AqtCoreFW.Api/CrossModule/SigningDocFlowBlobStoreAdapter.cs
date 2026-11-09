using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Identity.Application.Signing;

namespace AqtCoreFW.Api.CrossModule;

/// <summary>
/// Host-resident adapter binding <see cref="ISigningDocFlowBlobStore"/> (Identity.Application port) to
/// <see cref="IFileStorageService"/> (Files.Domain). The doc-flow handler streams the PDF to be signed
/// and the uploaded signed PDF through this port; the bucket names are owned here, not by the handler.
///
/// Lives in the API host — the only composition root permitted to reference both Identity and Files at
/// compile time, keeping a direct Identity.Infrastructure → Files.Domain dependency out of the module
/// graph (ModuleIsolationTests invariant). Registered in Program.cs after both module DI extensions.
/// </summary>
internal sealed class SigningDocFlowBlobStoreAdapter(IFileStorageService fileStorage) : ISigningDocFlowBlobStore
{
    // Must match the bucket constants the doc-flow round-trip stages into (FileUpload handler + initiate).
    private const string OriginalBucket = "signing-original";
    private const string SignedBucket   = "signing-docflow";

    /// <inheritdoc />
    public Task<Stream> DownloadOriginalAsync(string storageKey, CancellationToken ct = default) =>
        fileStorage.DownloadAsync(OriginalBucket, storageKey, ct);

    /// <inheritdoc />
    public Task<Stream> DownloadPreviousSignedAsync(string storageKey, CancellationToken ct = default) =>
        fileStorage.DownloadAsync(SignedBucket, storageKey, ct);

    /// <inheritdoc />
    public async Task UploadSignedAsync(
        Stream content, string storageKey, string contentType, CancellationToken ct = default) =>
        await fileStorage.UploadAsync(content, SignedBucket, storageKey, contentType, ct).ConfigureAwait(false);
}
