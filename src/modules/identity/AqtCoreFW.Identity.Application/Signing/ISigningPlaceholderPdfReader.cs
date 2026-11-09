namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Reads back a staged PAdES artifact (the placeholder PDF written by <see cref="ISigningPlaceholderPdfWriter"/>)
/// from the PAdES staging bucket, so the async-poll completion path can splice the polled signature into the
/// placeholder's <c>/Contents</c> gap WITHOUT re-issuing signHash (F1 idempotency).
///
/// <para>Read twin of <see cref="ISigningPlaceholderPdfWriter"/> over the same bucket; a primitive-typed PORT owned
/// by Identity so the completion job reads staged bytes without a compile-time dependency on the Files module
/// (the host binds it to <c>IFileStorageService</c> at the composition root).</para>
/// </summary>
public interface ISigningPlaceholderPdfReader
{
    /// <summary>
    /// Reads the full PDF bytes stored under <paramref name="storageKey"/> in the PAdES staging bucket.
    /// Throws when the object does not exist (a missing placeholder is a hard failure for completion, not a soft miss).
    /// </summary>
    Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken = default);
}
