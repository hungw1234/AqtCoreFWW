using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentValidation;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Applies a Viettel Mysign async-callback (async=3, W6) result to its pending transaction: completes
/// it with the returned signature, or fails it with the mapped vendor reason.
///
/// <para><b>[AllowAnonymous] system callback.</b> Mysign authenticates with an inbound token (validated
/// in the controller before dispatch) and is keyed by the globally-unique, Viettel-assigned
/// <see cref="TransactionId"/>. The tenant is NOT in the request — it is derived from the loaded
/// transaction — so this command is <see cref="ITenantAgnosticCommand"/>: it opts out of ambient-tenant
/// enforcement and is safe because the lookup is an exact match on an unguessable id and the only
/// mutation is on that one transaction. The <c>SigningPendingTransactions</c> table is intentionally
/// not under RLS (lookup-before-tenant), so no SESSION_CONTEXT re-pin is required.</para>
///
/// <para>[SuppressAutoAudit]: the handler emits a structured audit row ONLY when it actually transitions
/// the transaction (Initiated → Completed/Failed). An idempotent re-delivery (already terminal) is a
/// 200 no-op that emits NO audit row — so Mysign retries cannot pollute the HMAC audit chain (H1).</para>
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
[SuppressAutoAudit]
public sealed record CompleteMysignCallbackCommand(
    string TransactionId,
    string? ProviderStatus,
    string? SignatureBase64) : ICommand<MysignCallbackCompletionResult>, ITenantAgnosticCommand;

/// <summary>
/// Outcome of applying a callback. <see cref="Transitioned"/> is true when this delivery moved the
/// transaction to a terminal state; false for an idempotent re-delivery / unknown transaction (the
/// controller maps both to a 200 ack with a different message, preserving the existing contract).
/// </summary>
public sealed record MysignCallbackCompletionResult(bool Transitioned);

public sealed class CompleteMysignCallbackCommandValidator
    : AbstractValidator<CompleteMysignCallbackCommand>
{
    public CompleteMysignCallbackCommandValidator()
    {
        RuleFor(x => x.TransactionId).NotEmpty();
    }
}
