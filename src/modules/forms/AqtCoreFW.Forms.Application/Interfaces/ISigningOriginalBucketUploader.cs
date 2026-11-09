namespace AqtCoreFW.Forms.Application.Interfaces;

/// <summary>
/// Abstraction for uploading raw bytes into the signing-original bucket used by the VGCA DocFlow
/// signing workflow. Defined here (Forms.Application) as a port/interface; the implementation lives
/// in the API host adapter (cross-module wiring) to avoid a compile-time Forms→Files dependency,
/// which would violate the module-isolation architecture invariant.
///
/// The bucket name ("signing-original") and key format are owned by the upload contract between
/// the BE signing workflow and the VGCA desktop tool; they are NOT controlled by Forms.
///
/// Security: the key MUST be tenant-prefixed ("{tenantId}/…") before passing here. The caller
/// (GenerateAndInitiateSubmissionSigningCommandHandler) is responsible for constructing the key;
/// this interface is upload-only and performs no validation of key shape.
/// </summary>
public interface ISigningOriginalBucketUploader
{
    /// <summary>
    /// Uploads <paramref name="bytes"/> to the signing-original bucket at <paramref name="key"/>.
    /// The key is expected to be tenant-prefixed (enforced by the caller, not here).
    /// </summary>
    /// <returns>The storage key that was written (same as <paramref name="key"/>).</returns>
    Task<string> UploadAsync(byte[] bytes, string key, CancellationToken cancellationToken = default);
}
