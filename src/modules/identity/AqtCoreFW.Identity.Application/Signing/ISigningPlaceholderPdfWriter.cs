namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Stages the prepared PAdES <b>placeholder</b> PDF (incremental update laid out, <c>/Contents</c> still zeros)
/// into durable object storage so the async-poll completion path can read it back and splice the CMS WITHOUT
/// re-issuing signHash (F1 idempotency). The placeholder bytes are the whole PDF — too large for a DB column —
/// so the resume state persists only the returned storage KEY.
///
/// <para>Primitive-typed PORT owned by Identity (the write-side twin of <see cref="ISigningOriginalPdfReader"/>):
/// the host binds it to <c>IFileStorageService</c> at the composition root, keeping the PAdES strategy free of a
/// compile-time dependency on the Files module. The bucket is the adapter's concern; callers pass a tenant-prefixed
/// key.</para>
/// </summary>
public interface ISigningPlaceholderPdfWriter
{
    /// <summary>
    /// Writes <paramref name="bytes"/> under <paramref name="key"/> in the PAdES staging bucket.
    /// The key is expected to be tenant-prefixed (enforced by the caller, not here).
    /// </summary>
    /// <returns>The storage key that was written (same as <paramref name="key"/>).</returns>
    Task<string> WriteAsync(byte[] bytes, string key, CancellationToken cancellationToken = default);
}
