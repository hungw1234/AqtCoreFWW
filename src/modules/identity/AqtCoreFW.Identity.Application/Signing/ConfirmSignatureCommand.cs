using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentValidation;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Confirms the outcome of a deferred async signing transaction (Viettel Mysign DeferredPoll / W5).
///
/// <para>Caller issues this after receiving <c>IsPending=true</c> from <see cref="SignPayloadCommand"/>
/// with a <c>TransactionId</c>. The handler (in Infrastructure) queries /requests/status once and
/// returns the current state: still in-flight → IsPending=true; terminal success → IsSuccess=true
/// + Signature; terminal failure → IsSuccess=false.</para>
///
/// [SuppressAutoAudit]: handler emits structured audit including providerKey, txId, outcome.
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
[SuppressAutoAudit]
public sealed record ConfirmSignatureCommand(
    Guid TenantId,
    string TransactionId,
    string UserId) : ICommand<SignPayloadResultDto>;

public sealed class ConfirmSignatureCommandValidator : AbstractValidator<ConfirmSignatureCommand>
{
    public ConfirmSignatureCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.TransactionId).NotEmpty().WithMessage("TransactionId must not be empty.");
        RuleFor(x => x.UserId).NotEmpty();
    }
}
