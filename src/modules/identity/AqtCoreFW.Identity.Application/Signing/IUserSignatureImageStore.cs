using FluentResults;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Cross-module port: validates and persists a user's visual signature image. The implementation lives in
/// the API host (<c>AqtCoreFW.Api.CrossModule</c>) — the only composition root permitted to reference both
/// Identity and Files at compile time — so the Identity module never gains a direct Files dependency
/// (ModuleIsolationTests invariant). Mirrors <see cref="ISigningDocFlowBlobStore"/>; the bucket name and key
/// convention are owned by the adapter, not the calling handler.
///
/// <para>Validation routes through the Files security pipeline (extension blocklist + magic-byte MIME) and
/// then narrows to PNG/JPEG by inspecting actual bytes — the declared content-type is never trusted (S8).
/// SVG is rejected (stored-XSS/XXE class). A rejected image surfaces as a <c>ValidationError</c> result so
/// the handler returns a clean 422 rather than throwing.</para>
/// </summary>
public interface IUserSignatureImageStore
{
    /// <summary>
    /// Validates <paramref name="content"/> as a PNG/JPEG image by inspecting its magic bytes (the
    /// client-declared content-type is never accepted — the stored type is derived from the bytes), then
    /// uploads it to the user-signatures bucket scoped to <paramref name="tenantId"/>/<paramref name="userId"/>.
    /// <paramref name="fileName"/> feeds the extension blocklist only. Returns the storage key to persist on
    /// the profile, or a failed result carrying a <c>ValidationError</c> when the image is rejected (oversize
    /// is enforced upstream by the command validator).
    /// </summary>
    Task<Result<string>> ValidateAndStoreAsync(
        Guid tenantId,
        Guid userId,
        byte[] content,
        string fileName,
        CancellationToken ct = default);

    /// <summary>
    /// Reads back a previously-stored signature image as raw bytes by its storage key (the value persisted on
    /// the profile's <c>FullSignatureKey</c>/<c>InitialSignatureKey</c>). Used by the PAdES visible-stamp path
    /// to fetch the image for rasterizing into the signed PDF. The image is small (≤500 KB upload cap) so a
    /// full-buffer read is used rather than streaming (unlike the 50 MB doc-flow blobs in
    /// <see cref="ISigningDocFlowBlobStore"/>). Throws when the object is absent.
    /// </summary>
    Task<byte[]> GetAsync(string storageKey, CancellationToken ct = default);

    /// <summary>
    /// Best-effort delete of a previously-stored image by its storage key (the prior slot key when a slot is
    /// re-uploaded), so the bucket does not accumulate orphaned signature images (personal data — retention).
    /// Implementations swallow not-found / transient errors: a failed reclaim must never fail the upload.
    /// </summary>
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}
