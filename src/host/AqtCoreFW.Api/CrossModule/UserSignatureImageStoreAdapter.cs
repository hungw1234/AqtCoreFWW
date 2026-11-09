using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.SharedKernel.Contracts;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Api.CrossModule;

/// <summary>
/// Host-resident adapter binding <see cref="IUserSignatureImageStore"/> (Identity.Application port) to the
/// Files module's <see cref="IFileSecurityService"/> (validation) and <see cref="IFileStorageService"/>
/// (MinIO). Lives in the API host — the only composition root permitted to reference both Identity and Files
/// at compile time — keeping a direct Identity→Files dependency out of the module graph
/// (ModuleIsolationTests invariant). Mirrors <see cref="SigningDocFlowBlobStoreAdapter"/>; the bucket name
/// and key convention are owned here, not by the calling handler. Registered in Program.cs after both
/// module DI extensions.
/// </summary>
internal sealed class UserSignatureImageStoreAdapter(
    IFileSecurityService security,
    IFileStorageService storage,
    IVirusScanner scanner,
    IFeatureFlagService featureFlags,
    ILogger<UserSignatureImageStoreAdapter> logger) : IUserSignatureImageStore
{
    private const string Bucket = "user-signatures";

    // Same flag the async Files pipeline (ClamAvScanJob) gates on — kept as a literal so the two paths
    // stay in lock-step. When disabled (dev/demo) scanning is skipped; production MUST enable it (NĐ68).
    private const string VirusScanFlag = "feature:files.virus-scan";

    // PNG (89 50 4E 47) and JPEG (FF D8 FF) magic-byte prefixes — the only raster types a signature stamp
    // needs. SVG is excluded by construction (not a magic-byte match) on top of the extension blocklist.
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

    /// <inheritdoc />
    public async Task<Result<string>> ValidateAndStoreAsync(
        Guid tenantId,
        Guid userId,
        byte[] content,
        string fileName,
        CancellationToken ct = default)
    {
        // 1. Extension blocklist (Files security pipeline — blocks .svg/.exe/.html/... by last extension).
        var extResult = security.ValidateExtension(fileName);
        if (!extResult.IsValid)
            return Result.Fail(new ValidationError(extResult.Reason!, "FileName"));

        // 2. Narrow to PNG/JPEG by actual bytes — this is the real teeth. FileSecurityService.ValidateMimeType
        //    deliberately ACCEPTS unknown magic (defers to ClamAV), so it cannot enforce "PNG/JPEG only"; a
        //    renamed-SVG/PDF polyglot would pass it. Inspecting the magic bytes here rejects anything that is
        //    not a real PNG/JPEG and gives "declared image/png but the bytes are not PNG → reject" its teeth
        //    (the client-declared content-type is never trusted — S8).
        var detected = DetectImageContentType(content);
        if (detected is null)
            return Result.Fail(new ValidationError(
                "Image must be a PNG or JPEG file.", "Content"));

        // 3. Deep-scan the bytes with ClamAV BEFORE they can be persisted and later embedded into a signed
        //    PDF that other parties download. Magic-byte narrowing proves the bytes are a PNG/JPEG, not that
        //    the image is benign. This runs synchronously (bytes are in-memory, ≤500 KB) rather than through
        //    the async quarantine pipeline (ClamAvScanJob) — but gates on the SAME feature flag and mirrors
        //    its fail-closed semantics, so the two upload paths behave alike (SIGN-PROFILE-IMAGE-CLAMAV-SCAN).
        var scanResult = await ScanForVirusAsync(content, tenantId, userId, ct).ConfigureAwait(false);
        if (scanResult.IsFailed)
            return scanResult;

        // 4. Store under a UUID key scoped to (tenant, user); never reuse the client filename. The stored
        //    content-type is the detected one, not the (untrusted) declared one.
        var extension = detected == "image/png" ? "png" : "jpg";
        var key = $"{tenantId}/{userId}/{Guid.NewGuid()}.{extension}";

        using var upload = new MemoryStream(content, writable: false);
        await storage.UploadAsync(upload, Bucket, key, detected, ct).ConfigureAwait(false);

        return Result.Ok(key);
    }

    /// <summary>
    /// Runs the ClamAV deep-scan gate on the raw image bytes. Returns <see cref="Result.Ok()"/> when the
    /// image may be stored (clean, or scanning disabled in dev/demo), or a failed result carrying the
    /// appropriate error when it must be blocked. Mirrors <c>ClamAvScanJob</c>'s three-way verdict:
    /// <list type="bullet">
    ///   <item>flag disabled → skip (dev/demo), logged as a CRITICAL bypass.</item>
    ///   <item>Clean → Ok.</item>
    ///   <item>Infected → 422 <see cref="ValidationError"/> (rejected content, like the magic-byte reject).</item>
    ///   <item>Error → 503 <see cref="ServiceUnavailableError"/> (fail-closed: no verdict ⇒ never persist an
    ///     unscanned image; retryable — an unreachable clamd is infra, not the caller's fault).</item>
    /// </list>
    /// </summary>
    private async Task<Result> ScanForVirusAsync(byte[] content, Guid tenantId, Guid userId, CancellationToken ct)
    {
        // Read GLOBAL (no tenant) — same as ClamAvScanJob: scanning is deployment-wide posture, never a
        // per-tenant opt-out. The tenantId parameter is for logging and storage scoping, not the gate.
        if (!featureFlags.IsEnabled(VirusScanFlag))
        {
            // Mirrors ClamAvScanJob's feature-flag bypass: dev/demo mode stores without scanning.
            logger.LogCritical(
                "SECURITY BYPASS: Virus scan disabled by feature flag. Signature image for user {UserId} " +
                "(tenant {TenantId}) stored WITHOUT scan. Ensure {Flag} is enabled in production.",
                userId, tenantId, VirusScanFlag);
            return Result.Ok();
        }

        using var scanStream = new MemoryStream(content, writable: false);

        VirusScanResult scan;
        try
        {
            scan = await scanner.ScanAsync(scanStream, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The current scanner (ClamAvVirusScanner) is fail-closed and never throws — it returns an Error
            // verdict on any fault. But the adapter codes against the IVirusScanner contract: harden the
            // boundary so a throwing implementation still degrades to the retryable 503 (never a 500, and
            // never a stored unscanned image), keeping the fail-closed guarantee implementation-independent.
            logger.LogError(
                ex, "Signature image upload BLOCKED: virus scanner threw for user {UserId} (tenant {TenantId}). " +
                "Fail-closed — image not persisted.", userId, tenantId);
            return Result.Fail(new ServiceUnavailableError(
                "Virus scanning is temporarily unavailable. Please try again shortly.",
                ErrorCodes.Files.VirusScanFailed));
        }

        switch (scan.Status)
        {
            case VirusScanStatus.Clean:
                return Result.Ok();

            case VirusScanStatus.Infected:
                logger.LogWarning(
                    "Signature image upload REJECTED: ClamAV detected {VirusName} (tenant {TenantId}, user {UserId}).",
                    scan.VirusName, tenantId, userId);
                return Result.Fail(new ValidationError(
                    "The uploaded image failed a virus scan and was rejected.", "Content"));

            default: // VirusScanStatus.Error — clamd unreachable / timeout / protocol error.
                // Fail-closed: no verdict ⇒ do NOT persist an unscanned image. Surface a retryable 503 so the
                // client can try again once scanning is healthy, rather than silently accepting the image.
                logger.LogError(
                    "Signature image upload BLOCKED: ClamAV produced no verdict (reason={Reason}) for user " +
                    "{UserId} (tenant {TenantId}). Fail-closed — image not persisted.",
                    scan.VirusName, userId, tenantId);
                return Result.Fail(new ServiceUnavailableError(
                    "Virus scanning is temporarily unavailable. Please try again shortly.",
                    ErrorCodes.Files.VirusScanFailed));
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> GetAsync(string storageKey, CancellationToken ct = default)
    {
        // Full-buffer read: signature images are capped at 500 KB on upload, so buffering is cheaper and
        // simpler than the streaming path the 50 MB doc-flow blobs need. A missing key propagates the storage
        // layer's not-found exception to the caller (a data inconsistency, not a user error).
        await using var stream = await storage.DownloadAsync(Bucket, storageKey, ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        // Best-effort reclaim of a replaced slot's blob — a failed delete just leaves an orphan (the prior,
        // pre-M2 behaviour), it must never fail the upload that already committed the new key to the DB.
        try
        {
            await storage.DeleteAsync(Bucket, storageKey, ct).ConfigureAwait(false);
        }
        catch
        {
            // Swallowed by design — orphan reclaim is non-critical; a sweep can still catch it later.
        }
    }

    /// <summary>Returns the image MIME type if the bytes start with a PNG or JPEG signature, else null.</summary>
    private static string? DetectImageContentType(byte[] content)
    {
        if (StartsWith(content, PngMagic)) return "image/png";
        if (StartsWith(content, JpegMagic)) return "image/jpeg";
        return null;
    }

    private static bool StartsWith(byte[] content, byte[] prefix)
    {
        if (content.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
            if (content[i] != prefix[i]) return false;
        return true;
    }
}
