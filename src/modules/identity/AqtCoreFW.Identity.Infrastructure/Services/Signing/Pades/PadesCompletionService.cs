using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;

/// <summary>
/// Finalizes a PAdES async-poll transaction: polls Mysign for the signature, and on success splices the CMS into
/// the staged placeholder and stores the signed PDF. Operates on a single <see cref="SigningPendingTransaction"/>
/// and mutates it via its domain methods — the CALLER (the poll job) owns the DbContext and the save, so this
/// service is DbContext-free and unit-testable in isolation.
/// </summary>
public interface IPadesCompletionService
{
    /// <summary>
    /// Drives one completion attempt for <paramref name="txn"/>. Idempotent/replayable: a non-Initiated or
    /// non-PAdES transaction is a no-op; a still-pending poll leaves it Initiated for the next tick; a terminal
    /// success splices + stores the PDF and calls <see cref="SigningPendingTransaction.CompletePades"/>; a terminal
    /// failure calls <see cref="SigningPendingTransaction.Fail"/>. Infrastructure faults (Mysign/storage) throw so
    /// the caller can leave the row Initiated for a later retry.
    /// </summary>
    Task CompleteAsync(SigningPendingTransaction txn, CancellationToken ct = default);
}

/// <inheritdoc cref="IPadesCompletionService"/>
public sealed class PadesCompletionService(
    ISigningPlaceholderPdfReader placeholderReader,
    ISigningPlaceholderPdfWriter padesWriter,
    ViettelMysignLoginClient loginClient,
    ViettelMysignStatusPollClient statusPoll,
    UserManager<ApplicationUser> userManager,
    ISignerCertEnrollmentRepository enrollments,
    ILogger<PadesCompletionService> logger) : IPadesCompletionService
{
    public async Task CompleteAsync(SigningPendingTransaction txn, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(txn);

        // Replay/idempotency guard: only drive Initiated PAdES rows. A row already terminal, or an async-callback
        // (non-PAdES) row with no resume state, is skipped — a replayed sweep never re-signs or double-stores.
        if (txn.Status != PendingTransactionStatus.Initiated || txn.PadesPlaceholderStorageKey is null)
            return;

        // Re-login by the signer's national id (CCCD) to authorize the status poll. Tokens are never persisted; the
        // login client caches them. If the signer can't be resolved we can't poll — leave Initiated (the cleanup
        // job expires it eventually); do NOT fail terminally on a transient resolution miss.
        var user = await userManager.FindByIdAsync(txn.UserId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.SigningNationalId))
        {
            logger.LogWarning(
                "PAdES completion: signer {UserId} for txn {TxId} has no resolvable national id — skipping this tick",
                txn.UserId, txn.TransactionId);
            return;
        }

        string token = await loginClient.GetAccessTokenAsync(user.SigningNationalId, ct);
        var resp = await statusPoll.GetStatusAsync(txn.TransactionId, token, ct);
        var status = MysignStatusMapper.Map(resp.Status);

        if (!status.IsTerminal)
            return; // still waiting on the mobile OTP / CA processing — poll again next tick.

        if (!status.IsSuccess)
        {
            logger.LogInformation(
                "PAdES completion: txn {TxId} terminal failure — {Reason}", txn.TransactionId, status.ReadableMessage);
            txn.Fail(status.ReadableMessage);
            return;
        }

        // Terminal success — extract the raw RSA signature.
        byte[] rawSignature;
        try
        {
            if (resp.Signatures is not { Count: > 0 } sigs || string.IsNullOrEmpty(sigs[0]))
                throw new FormatException("status=success but signatures array is empty");
            rawSignature = Convert.FromBase64String(sigs[0]);
        }
        catch (FormatException ex)
        {
            // A success status with no usable signature is a provider anomaly → terminal failure (no retry would help).
            logger.LogError(ex, "PAdES completion: txn {TxId} success status but unusable signature", txn.TransactionId);
            txn.Fail($"Provider returned success but no usable signature: {ex.Message}");
            return;
        }

        // C5 — re-verify the signer is STILL in good standing at completion. The async poll means an admin can
        // reject/revoke the enrollment between initiate and this completion; that must block the signature (no
        // signed blob). We re-check the enrollment TIER, not the specific cert: the cert's serial/issuer were
        // taken from the Mysign cert-info API at initiate and aren't recoverable from the persisted DER, and
        // thumbprint algorithms differ across enrol paths — a cert-string rematch would false-fail valid
        // completions. If the signer holds any enrollment row but none is currently active+Approved, fail
        // terminally. Bootstrap signers (zero enrollment rows) have nothing to revoke and are unaffected.
        if (await enrollments.HasAnyEnrollmentAsync(txn.TenantId, txn.UserId, ct))
        {
            var activeEnrollments = await enrollments.GetActiveByUserAsync(txn.TenantId, txn.UserId, ct);
            if (!activeEnrollments.Any(e => e.ApprovalStatus == CertApprovalStatus.Approved))
            {
                logger.LogWarning(
                    "PAdES completion: txn {TxId} signer {UserId} has no active+approved enrollment at completion — failing, no blob stored",
                    txn.TransactionId, txn.UserId);
                txn.Fail("Signer enrollment is no longer approved at completion time");
                return;
            }
        }

        byte[] placeholderPdf = await placeholderReader.ReadAsync(txn.PadesPlaceholderStorageKey, ct);
        byte[] signedPdf = PadesPdfAssembler.Assemble(
            signedAttributesDer: txn.PadesSignedAttributesDer!,
            signerCertDer:       txn.PadesSignerCertDer!,
            rawSignature:        rawSignature,
            placeholderPdf:      placeholderPdf,
            contentsGapStart:    txn.PadesContentsGapStart!.Value,
            contentsHexLength:   txn.PadesContentsHexLength!.Value);

        // HA-safe signed-PDF key: derived deterministically from the transaction identity, NOT a random guid. Two
        // poll replicas that both reach terminal success assemble a BYTE-IDENTICAL PDF (the CMS is a pure function
        // of the persisted resume state + the polled raw signature) and write the SAME key, so the object store's
        // overwrite-by-key PUT collapses them to ONE object — no stranded, unreferenced blob. RowVersion still
        // elects a single Initiated→Completed transition (the loser's save throws, caught by the poll job); a
        // crash after the store but before that commit simply re-derives the same key on the next tick and
        // overwrites in place (idempotent, no orphan). TransactionId is the aggregate PK (a unique Viettel UUID).
        string signedKey = $"{txn.TenantId}/{txn.TransactionId}.signed.pdf";
        await padesWriter.WriteAsync(signedPdf, signedKey, ct);

        // CompletePades validates the key BEFORE mutating, then flips Initiated→Completed with the signature + key.
        txn.CompletePades(rawSignature, signedKey);

        logger.LogInformation(
            "PAdES completion: txn {TxId} completed; signed PDF stored at {SignedKey}", txn.TransactionId, signedKey);
    }
}
