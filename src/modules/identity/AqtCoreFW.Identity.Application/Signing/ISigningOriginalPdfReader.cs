namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Reads the original (unsigned) PDF bytes that the server-side PAdES assembler signs. The bucket is an
/// implementation detail of the host adapter (the staged-original bucket); callers pass only the storage key.
///
/// <para>This is a primitive-typed PORT owned by Identity so the PAdES strategy can read a staged PDF without a
/// compile-time dependency on the Files module — the host binds it to <c>IFileStorageService</c> at the
/// composition root, the only place permitted to reference both modules. Mirrors the write-side
/// <c>ISigningOriginalBucketUploader</c> pattern.</para>
/// </summary>
public interface ISigningOriginalPdfReader
{
    /// <summary>
    /// Reads the full PDF bytes stored under <paramref name="storageKey"/> in the staged-original bucket.
    /// Throws when the object does not exist (a missing original is a hard failure for signing, not a soft miss).
    /// </summary>
    Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken = default);
}
