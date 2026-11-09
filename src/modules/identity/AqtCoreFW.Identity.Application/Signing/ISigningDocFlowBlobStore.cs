namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Streaming blob port for the VGCA Tier-3 doc-flow handler (W3). Owned by Identity so the doc-flow
/// signing logic can move PDF bytes without a compile-time dependency on the Files module — the host
/// binds it to <c>IFileStorageService</c> at the composition root (the only place permitted to
/// reference both modules; the ModuleIsolationTests invariant keeps a direct
/// Identity.Infrastructure → Files.Domain edge out of the module graph). Mirrors the
/// <see cref="ISigningOriginalPdfReader"/> port + host-adapter pattern, but streams (no full-buffer)
/// because the doc-flow artifacts are up to 50 MB.
///
/// <para>The bucket is an implementation detail of the host adapter — callers pass only a storage key
/// and the semantic operation (original vs previous-signed download, signed upload).</para>
/// </summary>
public interface ISigningDocFlowBlobStore
{
    /// <summary>
    /// Streams the original (unsigned) PDF for the first signer. Throws
    /// <see cref="System.IO.FileNotFoundException"/> when the object is absent (the handler maps that to
    /// a retryable 404 BEFORE consuming the nonce). Caller owns disposing the returned stream.
    /// </summary>
    Task<Stream> DownloadOriginalAsync(string storageKey, CancellationToken ct = default);

    /// <summary>
    /// Streams signer N-1's signed PDF for a subsequent signer (incremental PAdES chaining). Same
    /// not-found / disposal semantics as <see cref="DownloadOriginalAsync"/>.
    /// </summary>
    Task<Stream> DownloadPreviousSignedAsync(string storageKey, CancellationToken ct = default);

    /// <summary>
    /// Stores the uploaded signed PDF under <paramref name="storageKey"/>. Streams from
    /// <paramref name="content"/> without buffering the whole file.
    /// </summary>
    Task UploadSignedAsync(Stream content, string storageKey, string contentType, CancellationToken ct = default);
}
