using System.Text.Json;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using AqtCoreFW.SharedKernel.Application;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Handles <see cref="CompleteMysignCallbackCommand"/>. Lives in Infrastructure (like
/// <c>ConfirmSignatureCommandHandler</c> / <c>SignatureOrchestrator</c>) so it can use the vendor
/// <see cref="MysignStatusMapper"/> and <see cref="IdentityDbContext"/> directly — the
/// <c>SigningPendingTransactions</c> table carries no RLS, so the tenant-agnostic anonymous lookup by
/// the globally-unique <c>TransactionId</c> needs no SESSION_CONTEXT pin.
/// </summary>
public sealed class CompleteMysignCallbackCommandHandler(
    IdentityDbContext db,
    IAuditLogger auditLogger,
    ILogger<CompleteMysignCallbackCommandHandler> logger)
    : IRequestHandler<CompleteMysignCallbackCommand, Result<MysignCallbackCompletionResult>>
{
    public async Task<Result<MysignCallbackCompletionResult>> Handle(
        CompleteMysignCallbackCommand cmd, CancellationToken ct)
    {
        var tx = await db.SigningPendingTransactions
            .FirstOrDefaultAsync(t => t.TransactionId == cmd.TransactionId, ct)
            .ConfigureAwait(false);

        // Idempotency: unknown txId or already terminal → 200 no-op, NO audit row (H1).
        if (tx is null || tx.Status != PendingTransactionStatus.Initiated)
            return Result.Ok(new MysignCallbackCompletionResult(Transitioned: false));

        ApplyOutcome(tx, cmd);

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // TOCTOU race: a concurrent delivery transitioned the same transaction first. Re-read
            // (never blindly swallow) — if the other delivery won, this one is an idempotent no-op with
            // no audit row; if the row is somehow still Initiated, the failure is genuine → rethrow.
            db.Entry(tx).State = EntityState.Detached;
            var current = await db.SigningPendingTransactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.TransactionId == cmd.TransactionId, ct)
                .ConfigureAwait(false);

            if (current is null || current.Status != PendingTransactionStatus.Initiated)
                return Result.Ok(new MysignCallbackCompletionResult(Transitioned: false));

            throw;
        }

        // Explicit audit — emitted ONLY on a real terminal transition (exactly once per signing event).
        var snapshot = JsonSerializer.Serialize(new
        {
            txId     = tx.TransactionId,
            status   = tx.Status.ToString(),
            tenantId = tx.TenantId,
            userId   = tx.UserId,
        });

        await auditLogger.LogCommandAsync(
            action:       tx.Status == PendingTransactionStatus.Completed
                              ? "MYSIGN_CALLBACK_COMPLETED"
                              : "MYSIGN_CALLBACK_FAILED",
            moduleName:   "Identity",
            resourceType: "SigningPendingTransaction",
            resourceId:   tx.TransactionId,
            commandType:  typeof(CompleteMysignCallbackCommand),
            dataSnapshot: snapshot,
            // Anonymous system callback: attribute the row to the signing tenant (from the loaded
            // transaction) so it lands on that tenant's HMAC chain, not the null/system chain.
            tenantIdOverride: tx.TenantId,
            cancellationToken: ct).ConfigureAwait(false);

        return Result.Ok(new MysignCallbackCompletionResult(Transitioned: true));
    }

    /// <summary>Applies the vendor result to the transaction: success → Complete, else → Fail(reason).</summary>
    private void ApplyOutcome(SigningPendingTransaction tx, CompleteMysignCallbackCommand cmd)
    {
        if (cmd.ProviderStatus == "1" && !string.IsNullOrEmpty(cmd.SignatureBase64))
        {
            try
            {
                tx.Complete(Convert.FromBase64String(cmd.SignatureBase64));
                return;
            }
            catch (FormatException)
            {
                logger.LogWarning(
                    "Mysign callback: signature for txId={TxId} is not valid base64 — marking failed",
                    cmd.TransactionId);
                tx.Fail("Callback signature was not valid base64");
                return;
            }
        }

        var info = MysignStatusMapper.Map(cmd.ProviderStatus);
        tx.Fail(info.ReadableMessage);
        logger.LogWarning(
            "Mysign callback: txId={TxId} failed with status={Status} reason={Reason}",
            cmd.TransactionId, cmd.ProviderStatus, info.ReadableMessage);
    }
}
