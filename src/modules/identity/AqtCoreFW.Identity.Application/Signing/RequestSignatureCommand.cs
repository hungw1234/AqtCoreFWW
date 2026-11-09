using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Audited write envelope over <see cref="ISignatureOrchestrator.RequestSignatureAsync"/>.
///
/// Creating a signing request is the system's highest-risk write (QĐ 742 §1.6.b). It previously went
/// orchestrator-direct from the controller, bypassing the MediatR pipeline and producing no audit row
/// on the pades/remote routes (the docflow route self-audits via the nested
/// <c>InitiateDocFlowCommand</c>). Routing it through an <see cref="ICommand{TResponse}"/> puts the
/// request-level act on the pipeline so <c>AuditBehavior</c> records it uniformly. The orchestrator
/// still owns the create+route+execute transaction (including the untrusted-PDF upload-sign gate, applied
/// at that single chokepoint so both this command and the cross-module client path are covered); this only
/// adds the audit/validation envelope.
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record RequestSignatureCommand(RequestSignatureInput Input)
    : ICommand<RequestSignatureResult>;

public sealed class RequestSignatureCommandHandler(ISignatureOrchestrator orchestrator)
    : IRequestHandler<RequestSignatureCommand, Result<RequestSignatureResult>>
{
    public Task<Result<RequestSignatureResult>> Handle(RequestSignatureCommand command, CancellationToken ct)
        => orchestrator.RequestSignatureAsync(command.Input, ct);
}
