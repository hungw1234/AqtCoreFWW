using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Domain.Signing;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Parameters;
using BcX509 = Org.BouncyCastle.X509;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;

/// <summary>
/// Drives the server-side PAdES <b>initiation</b> (async-poll, option A): everything up to and including the
/// <c>signHash async=2</c> call, persisting a <see cref="SigningPendingTransaction"/> with the resume state the
/// completion path (P4-4) needs. Returns FAST with a pollable id — it never blocks on the mobile OTP.
/// </summary>
public interface IPadesTransactionInitiator
{
    Task<Result<SigningExecutionResult>> InitiateAsync(SigningExecutionContext context, CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="IPadesTransactionInitiator"/>. Composes the proven PAdES crypto (writer + CMS builder) with
/// the Mysign typed clients in async-poll mode. The PDF assembly is split exactly like the eventual completion:
/// here we prepare the placeholder + signed attributes and submit the hash; the CMS splice + final storage happen
/// later when the polled signature arrives.
///
/// <para><b>Death-trap #1.</b> The bytes submitted to <c>signHash</c> are <c>SHA-256(DER(signedAttributes))</c>
/// (<see cref="PadesSignedAttributes.SignInput"/>) — NEVER the document digest. Substituting the document hash
/// yields an Adobe-invalid PAdES with green self-tests.</para>
///
/// <para><b>F1 idempotency.</b> The placeholder PDF is staged to durable storage BEFORE <c>signHash</c> (a stranded
/// placeholder is harmless; a stranded signature is not), and the transaction persists the resume state so the
/// completion path is replayable without re-issuing <c>signHash</c> (which has no idempotency — a retry = a second
/// OTP prompt).</para>
/// </summary>
public sealed class PadesTransactionInitiator(
    ISigningOriginalPdfReader originalReader,
    ISigningPlaceholderPdfWriter placeholderWriter,
    ISignerBindingGuard bindingGuard,
    ViettelMysignLoginClient loginClient,
    ViettelMysignCertInfoClient certInfoClient,
    ViettelMysignSignHashClient signHashClient,
    UserManager<ApplicationUser> userManager,
    ITenantSignatureConfigRepository configRepo,
    IdentityDbContext db,
    IGuidGenerator guidGenerator,
    IOptionsMonitor<ViettelMysignOptions> options,
    IUserSignatureProfileRepository profileRepo,
    IUserSignatureImageStore imageStore,
    ISignatureStampComposer stampComposer,
    ISignatureStampRasterizer stampRasterizer,
    ILogger<PadesTransactionInitiator> logger) : IPadesTransactionInitiator
{
    // Stateless crypto helpers — no per-instance state, safe to share.
    private static readonly PdfIncrementalSignatureWriter Writer = new();
    private static readonly PadesCmsBuilder CmsBuilder = new();

    // Vietnam is a fixed UTC+7 with no daylight saving — a constant offset is deterministic and avoids
    // depending on a host tz database id (which differs on Windows vs Linux).
    private static readonly TimeSpan VnUtcOffset = TimeSpan.FromHours(7);

    public async Task<Result<SigningExecutionResult>> InitiateAsync(
        SigningExecutionContext context, CancellationToken ct = default)
    {
        var input = context.Input;
        if (input.Pades is not { } pades)
            return Result.Fail(new BadRequestError(
                "PAdES signing requires PAdES parameters (original storage key).", "SIGNING_PADES_PARAMS_REQUIRED"));

        // Multi-signer PAdES is intentionally out of this slice: signer N must read signer N-1's output from the
        // pades bucket (a different read path), and there is no consumer yet. Fail-closed with a clear code rather
        // than silently signing over the wrong base. The orchestrator's sequence guard already gates ordering.
        if (input.SignerSequenceNumber > 1)
            return Result.Fail(new BadRequestError(
                "Multi-signer PAdES (sequence > 1) is not yet supported.", "SIGNING_PADES_MULTISIGNER_UNSUPPORTED"));

        // Mysign keys on the signer's national id (CCCD), NOT the internal Guid. Resolve it the same way
        // SignPayloadCommandHandler does and fail closed if absent / cross-tenant — the Guid would target the
        // wrong (or no) Mysign account and the OTP would never reach the signer's phone.
        var signer = await userManager.FindByIdAsync(input.AssignedSignerId.ToString());
        if (signer is null || signer.TenantId != input.TenantId || string.IsNullOrWhiteSpace(signer.SigningNationalId))
            return Result.Fail(new ForbiddenError(
                "Assigned signer has no resolvable Mysign identity in this tenant (fail-closed)."));
        string nationalId = signer.SigningNationalId;

        // The Mysign-touching region throws typed exceptions on realistic third-party failures (bad creds, outage,
        // non-2xx, account-not-enrolled). Map them to typed Results so the SPA gets a 4xx/503 it can branch on,
        // never an opaque 500. A cert-info failure is a signer-account problem (caller-correctable → 400); login /
        // signHash / transport failures are an upstream outage (retryable → 503). Cancellation and infrastructure
        // faults (storage, DB) are intentionally NOT caught here — they propagate to their correct handling.
        try
        {
            return await AssembleAndSubmitAsync(input, pades, nationalId, ct);
        }
        catch (MysignCertInfoException ex)
        {
            logger.LogWarning(ex, "PAdES initiation: Mysign cert-info unavailable for the assigned signer");
            return Result.Fail(new BadRequestError(
                $"Không lấy được chứng thư ký số của người ký từ Viettel Mysign: {ex.Message}",
                "SIGNING_PADES_CERT_UNAVAILABLE"));
        }
        catch (Exception ex) when (ex is MysignLoginException or MysignSignHashException or HttpRequestException)
        {
            logger.LogWarning(ex, "PAdES initiation: Viettel Mysign provider failure");
            return Result.Fail(new ServiceUnavailableError(
                "Dịch vụ ký số từ xa (Viettel Mysign) tạm thời không khả dụng. Vui lòng thử lại.",
                "SIGNING_PADES_PROVIDER_UNAVAILABLE"));
        }
    }

    private async Task<Result<SigningExecutionResult>> AssembleAndSubmitAsync(
        RequestSignatureInput input, PadesSignParameters pades, string nationalId, CancellationToken ct)
    {
        // 1. Read the staged original PDF (a missing original is a hard failure inside ReadAsync → 500, correct).
        byte[] originalPdf = await originalReader.ReadAsync(pades.OriginalStorageKey, ct);

        // 2. login + cert-info → the signer leaf cert (hashed into signing-certificate-v2, embedded in the CMS bag).
        string token = await loginClient.GetAccessTokenAsync(nationalId, ct);
        var (certInfo, credentialId, signerCertDer) = await certInfoClient.GetCertInfoAsync(nationalId, token, ct);

        // ECDSA fail-loud guard (VIETTEL-SIGN-ALG-ECHO): the CMS + signedAttrs hardcode SHA-256 + RSA. A non-RSA
        // credential signed with those params yields an Adobe-invalid signature — a false-green before rollout.
        var bcCert = new BcX509.X509CertificateParser().ReadCertificate(signerCertDer);
        if (bcCert.GetPublicKey() is not RsaKeyParameters)
            return Result.Fail(new BadRequestError(
                "Mysign signer certificate is not RSA; the PAdES path hardcodes SHA-256+RSA.",
                "SIGNING_PADES_NON_RSA_CERT"));

        // 3. S2 — bind the Mysign cert to the assigned signer. Fail-closed: without this a Mysign tenant could
        // sign with any credential_id. The guard never throws; an unbound verdict maps to 403.
        var verdict = await bindingGuard.VerifyAsync(certInfo, input.AssignedSignerId, input.TenantId, ct);
        if (!verdict.IsBound)
            return Result.Fail(new ForbiddenError(
                $"Signing certificate is not bound to the assigned signer: {verdict.Reason}"));

        // 3b. Build the visible signature stamp appearance when the request carries a placement. Serial/CA come
        // from the signer leaf just validated above (the binding guard already gated it to the signer's Approved
        // enrolled cert — so the live cert IS the approved enrolled cert; no separate enrollment read needed).
        // No placement → null appearance → the unchanged invisible path (backward-compat + USB).
        var appearanceResult = await BuildAppearanceAsync(input, pades, bcCert, ct);
        if (appearanceResult.IsFailed)
            return Result.Fail<SigningExecutionResult>(appearanceResult.Errors);
        SignatureAppearance? appearance = appearanceResult.Value;

        // 4. Prepare the incremental-update placeholder (ByteRange + zero /Contents) over the original. The
        // appearance bytes (image XObject + form XObject) land INSIDE the signed ByteRange — frozen at initiate.
        PdfSigningPreparation prep;
        try
        {
            prep = Writer.Prepare(originalPdf, appearance: appearance);
        }
        catch (PdfSigningException ex)
        {
            return Result.Fail(new BadRequestError(
                $"Original PDF is unsupported by the PAdES writer: {ex.Message}", "SIGNING_PADES_PDF_UNSUPPORTED"));
        }

        // 5. Build the signed attributes (messageDigest = the ByteRange digest; signing-certificate-v2 = the cert).
        var attrs = CmsBuilder.BuildSignedAttributes(prep.DocumentDigest, bcCert, DateTime.UtcNow);

        // 6. Resolve the flow: a per-request override wins over the tenant config, which itself defaults to Async.
        // Sync blocks on the OTP and finalizes in-request; Async returns fast and a poll job finalizes later. Only
        // the remote server-side PAdES path honours this; the effective default stays Async (non-blocking + F1-safe).
        var cfg = await configRepo.GetByTenantIdAsync(input.TenantId, ct);
        SignatureFlowMode flowMode = pades.SignFlowModeOverride ?? cfg?.SignFlowMode ?? SignatureFlowMode.Async;
        bool sync = flowMode == SignatureFlowMode.Sync;

        return sync
            ? await SubmitSyncAsync(input, pades, nationalId, token, credentialId, prep, attrs, signerCertDer, ct)
            : await SubmitAsyncPollAsync(input, pades, nationalId, token, credentialId, prep, attrs, signerCertDer, ct);
    }

    /// <summary>
    /// Composes the visible signature stamp for this request, or returns a <c>null</c> appearance (invisible
    /// sign — backward-compatible + the USB path) when no placement is supplied. Fail-closed when a placement
    /// names a profile not owned by the signer (M4 ownership re-assert: the repo query is scoped to
    /// <c>(tenant, signer)</c> so a foreign profile resolves to null → 403) or a profile that has no uploaded
    /// image for the requested visual type. The stamp's timestamp is the server initiate-time (the
    /// authoritative signing time lives in the CMS <c>signingTime</c>); serial/issuer come from the signer leaf.
    /// </summary>
    private async Task<Result<SignatureAppearance?>> BuildAppearanceAsync(
        RequestSignatureInput input, PadesSignParameters pades, BcX509.X509Certificate bcCert, CancellationToken ct)
    {
        // Malformed/blank placement → invisible (do not fail an otherwise-valid sign over an unparseable hint).
        // A non-blank-but-unparseable placement is logged: the caller intended a stamp, so the downgrade to an
        // invisible signature must be observable rather than silent.
        if (!SignaturePlacement.TryParse(input.Placement, out var placement) || placement is null)
        {
            if (!string.IsNullOrWhiteSpace(input.Placement))
                logger.LogInformation(
                    "PAdES initiate: placement supplied but unparseable — signing invisibly (no visible stamp). tenant={TenantId}",
                    input.TenantId);
            return Result.Ok<SignatureAppearance?>(null);
        }

        UserSignatureProfile? profile = placement.ProfilePublicId is { } pid
            ? await profileRepo.GetByPublicIdAsync(input.TenantId, input.AssignedSignerId, pid, ct)
            : await profileRepo.GetDefaultAsync(input.TenantId, input.AssignedSignerId, ct);

        if (profile is null)
            return Result.Fail<SignatureAppearance?>(new ForbiddenError(
                "Signature profile not found for the signer, or not owned by them (fail-closed)."));

        // "initial" selects the paraph slot; anything else (incl. null) uses the full signature slot.
        string? imageKey = string.Equals(placement.VisualType, "initial", StringComparison.OrdinalIgnoreCase)
            ? profile.InitialSignatureKey
            : profile.FullSignatureKey;
        if (string.IsNullOrEmpty(imageKey))
            return Result.Fail<SignatureAppearance?>(new BadRequestError(
                "Chữ ký của hồ sơ chưa có ảnh cho loại con dấu được yêu cầu. Vui lòng tải ảnh chữ ký trước.",
                "SIGNING_PADES_PROFILE_IMAGE_MISSING"));

        // Fetch the image + compose + raster. These can throw (a missing blob = a server data inconsistency; a
        // QuestPDF/native render fault) — map them to a typed, coded Result instead of an opaque 500, mirroring
        // the file's typed-error discipline. This runs BEFORE signHash/persist, so a failure strands nothing.
        // Cancellation is rethrown (not a render failure).
        byte[] jpeg;
        try
        {
            byte[] imageBytes = await imageStore.GetAsync(imageKey, ct);

            // "Ký bởi" must show the SIGNER'S name from the signing certificate's subject (CN for a person,
            // O for an org — the same derivation enrollment uses for SignerCertEnrollment.DisplayName), NOT
            // the signature profile's display label. Fall back to the profile label only when the subject
            // yields no usable name (never throws, empty on failure).
            var (_, certDisplayName) = VgcaCertSubjectParser.Classify(bcCert.SubjectDN.ToString());
            var signerName = string.IsNullOrWhiteSpace(certDisplayName) ? profile.DisplayName : certDisplayName;

            var content = new StampContent(
                SignatureImage: imageBytes,
                SignerName:     signerName,
                // Render the stamp time in Vietnam local time (UTC+7, no DST) so it matches the signing time the
                // PDF reader shows for the CMS signingTime — otherwise the stamp shows UTC and reads ~7h earlier.
                SignedAt:       DateTimeOffset.UtcNow.ToOffset(VnUtcOffset),
                // "Lý do" is the user-entered reason (blank → composer omits the line), NOT the Mysign doc label.
                Reason:         pades.Reason,
                // Serial / Nhà CC are optional per profile — pass empty when suppressed so the composer skips them.
                Serial:         profile.ShowSerial ? bcCert.SerialNumber.ToString(16).ToUpperInvariant() : string.Empty,
                IssuerCa:       profile.ShowIssuer ? bcCert.IssuerDN.ToString() : string.Empty);

            ComposedStamp composed = stampComposer.Compose(content);
            jpeg = stampRasterizer.Rasterize(composed, placement.W, placement.H);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "PAdES initiate: visible-stamp render failed (image fetch/compose/raster). tenant={TenantId} imageKey={ImageKey}",
                input.TenantId, imageKey);
            return Result.Fail<SignatureAppearance?>(new ServiceUnavailableError(
                "Không tạo được con dấu chữ ký nhìn thấy. Vui lòng thử lại.", "SIGNING_PADES_STAMP_RENDER_FAILED"));
        }

        return Result.Ok<SignatureAppearance?>(new SignatureAppearance(
            jpeg, placement.X, placement.Y, placement.W, placement.H, placement.Page));
    }

    /// <summary>
    /// Async-poll (option A, default): stage the placeholder, submit <c>signHash async=2</c> (returns a
    /// transactionId immediately — no OTP block), and persist an <c>Initiated</c> transaction with full resume
    /// state. The completion job finalizes the PDF later. Persisting BEFORE relying on completion satisfies F1.
    /// </summary>
    private async Task<Result<SigningExecutionResult>> SubmitAsyncPollAsync(
        RequestSignatureInput input, PadesSignParameters pades, string nationalId, string token, string credentialId,
        PdfSigningPreparation prep, PadesSignedAttributes attrs, byte[] signerCertDer, CancellationToken ct)
    {
        // Stage the placeholder to durable storage BEFORE signHash (F1 ordering: a stranded placeholder blob is
        // harmless; a signature obtained without a stored placeholder to splice it into would be unrecoverable).
        string placeholderKey = $"{input.TenantId}/{guidGenerator.NewIdMonotonic()}.placeholder.pdf";
        await placeholderWriter.WriteAsync(prep.PreparedPdf, placeholderKey, ct);

        // signHash async=2 → returns a transactionId immediately, no OTP block. Death-trap #1: the submitted
        // hash is attrs.SignInput (the signedAttributes DER hash), never prep.DocumentDigest.
        var signResp = await signHashClient.SignHashRawAsync(
            userId:       nationalId,
            credentialId: credentialId,
            hashBytes:    attrs.SignInput,
            accessToken:  token,
            asyncMode:    2,
            documentName: pades.DocumentName ?? "AqtCoreFW PAdES",
            ct:           ct);

        if (!string.IsNullOrEmpty(signResp.Error))
            return Result.Fail(new BadRequestError(
                $"Mysign signHash error [{signResp.Error}]: {signResp.ErrorDescription}", "SIGNING_PADES_SIGNHASH_FAILED"));
        if (string.IsNullOrWhiteSpace(signResp.TransactionId))
            return Result.Fail(new BadRequestError(
                "Mysign signHash async returned no transactionId.", "SIGNING_PADES_NO_TRANSACTION_ID"));
        string transactionId = signResp.TransactionId;

        // Persist the transaction + resume state. The transactionId is the idempotency key; the resume state
        // (placeholder key + offsets + signedAttrs DER + signer cert DER) lets completion splice the polled
        // signature without re-signing.
        var ttl = TimeSpan.FromHours(Math.Max(options.CurrentValue.PendingTxTtlHours, 1));
        var txn = SigningPendingTransaction.Initiate(
            transactionId: transactionId,
            tenantId:      input.TenantId,
            userId:        input.AssignedSignerId,
            requestId:     input.ArtifactRef,
            documentId:    input.ArtifactRef,
            ttl:           ttl);
        txn.AttachPadesResumeState(
            placeholderKey, prep.ContentsGapStart, prep.ContentsHexLength, attrs.Der, signerCertDer);

        db.SigningPendingTransactions.Add(txn);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PAdES async-poll initiated: txId={TxId} tenant={TenantId} placeholderKey={PlaceholderKey}",
            transactionId, input.TenantId, placeholderKey);

        return Result.Ok(new SigningExecutionResult(
            SigningUnderlyingKind.PendingTransaction,
            transactionId,
            new Dictionary<string, string>
            {
                ["transactionId"] = transactionId,
                ["pollable"]      = "true",
            }));
    }

    /// <summary>
    /// Synchronous flow (opt-in per tenant): submit <c>signHash async=0</c>, which BLOCKS until the signer confirms
    /// on their phone (up to <c>SyncSignTimeoutSeconds</c>, ~120s), then assemble + store the signed PDF and persist
    /// the transaction already <c>Completed</c> — the same record shape the async path produces, so the status
    /// projection and the signed-PDF download path are identical for both flows.
    ///
    /// <para><b>Trade-off vs async (why async is the default).</b> Sync does NOT persist a transaction before the
    /// signature is obtained, so a signature returned by Mysign but lost to a post-sign storage/DB fault is orphaned
    /// (no row to resume from — the signer must re-confirm). It also ties up a request thread for the OTP wait,
    /// exposing it to gateway/proxy/client timeouts. Acceptable only because the caller is actively watching.</para>
    /// </summary>
    private async Task<Result<SigningExecutionResult>> SubmitSyncAsync(
        RequestSignatureInput input, PadesSignParameters pades, string nationalId, string token, string credentialId,
        PdfSigningPreparation prep, PadesSignedAttributes attrs, byte[] signerCertDer, CancellationToken ct)
    {
        // signHash async=0 blocks until the mobile OTP is confirmed and returns the raw RSA signature in-band.
        // Death-trap #1: the submitted hash is attrs.SignInput (the signedAttributes DER hash), never the doc digest.
        var signResp = await signHashClient.SignHashRawAsync(
            userId:       nationalId,
            credentialId: credentialId,
            hashBytes:    attrs.SignInput,
            accessToken:  token,
            asyncMode:    0,
            documentName: pades.DocumentName ?? "AqtCoreFW PAdES",
            ct:           ct);

        if (!string.IsNullOrEmpty(signResp.Error))
            return Result.Fail(new BadRequestError(
                $"Mysign signHash error [{signResp.Error}]: {signResp.ErrorDescription}", "SIGNING_PADES_SIGNHASH_FAILED"));
        if (signResp.Signatures is not { Count: > 0 } sigs || string.IsNullOrEmpty(sigs[0]))
            return Result.Fail(new BadRequestError(
                "Mysign signHash sync returned no signature.", "SIGNING_PADES_NO_SIGNATURE"));

        byte[] rawSignature;
        try { rawSignature = Convert.FromBase64String(sigs[0]); }
        catch (FormatException ex)
        {
            return Result.Fail(new BadRequestError(
                $"Mysign signHash sync returned a non-base64 signature: {ex.Message}", "SIGNING_PADES_BAD_SIGNATURE"));
        }

        // Splice the CMS into the in-memory placeholder (no placeholder blob is staged — sync finalizes in-request).
        byte[] signedPdf = PadesPdfAssembler.Assemble(
            signedAttributesDer: attrs.Der,
            signerCertDer:       signerCertDer,
            rawSignature:        rawSignature,
            placeholderPdf:      prep.PreparedPdf,
            contentsGapStart:    prep.ContentsGapStart,
            contentsHexLength:   prep.ContentsHexLength);

        string signedKey = $"{input.TenantId}/{guidGenerator.NewIdMonotonic()}.signed.pdf";
        await placeholderWriter.WriteAsync(signedPdf, signedKey, ct);

        // async=0 may not carry a transactionId; synthesize a stable correlation id when absent. It is only an
        // internal key (sync never polls Mysign), so a generated id is sufficient and never collides with a poll.
        string transactionId = string.IsNullOrWhiteSpace(signResp.TransactionId)
            ? $"sync-{guidGenerator.NewIdMonotonic():N}"
            : signResp.TransactionId;

        // Persist the transaction already Completed so the status projection + download path match the async flow.
        var ttl = TimeSpan.FromHours(Math.Max(options.CurrentValue.PendingTxTtlHours, 1));
        var txn = SigningPendingTransaction.Initiate(
            transactionId: transactionId,
            tenantId:      input.TenantId,
            userId:        input.AssignedSignerId,
            requestId:     input.ArtifactRef,
            documentId:    input.ArtifactRef,
            ttl:           ttl);
        txn.CompletePades(rawSignature, signedKey);

        db.SigningPendingTransactions.Add(txn);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PAdES sync completed in-request: txId={TxId} tenant={TenantId} signedKey={SignedKey}",
            transactionId, input.TenantId, signedKey);

        return Result.Ok(new SigningExecutionResult(
            SigningUnderlyingKind.PendingTransaction,
            transactionId,
            new Dictionary<string, string>
            {
                ["transactionId"] = transactionId,
                ["pollable"]      = "false",
            }));
    }
}
