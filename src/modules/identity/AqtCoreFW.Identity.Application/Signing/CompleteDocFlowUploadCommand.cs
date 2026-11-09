using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Commits a VGCA Tier-3 doc-flow signed-PDF upload (W3, nonce slot 2). Validates + consumes the nonce,
/// asserts tenant isolation + the Downloaded state, stores the uploaded bytes, then completes the record.
///
/// <para><b>[AllowAnonymous] system call</b> — tenant from the JWT, not the principal — so it is
/// <see cref="ITenantAgnosticCommand"/> and the handler pins SESSION_CONTEXT to the JWT tenant for the
/// RLS-protected signing tables. This is the signing-commit write, so it is audited: the command is
/// <see cref="SuppressAutoAuditAttribute">[SuppressAutoAudit]</see> and the handler emits a structured
/// row only on the actual Completed transition (exactly once; a concurrency-loser replay emits none).</para>
///
/// <para>The PDF format checks (content-type / size / %PDF- magic) are HTTP request validation done in
/// the controller before dispatch; <see cref="Content"/> is the already-validated, fresh stream.</para>
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
[SuppressAutoAudit]
public sealed record CompleteDocFlowUploadCommand(
    Guid DocId,
    Guid JwtTenant,
    Guid UserId,
    Guid Nonce,
    Stream Content,
    string FileName,
    string BaseUrl) : ICommand<DocFlowUploadResult>, ITenantAgnosticCommand;

/// <summary>The VGCA response fields the desktop tool reads (mapped to its PascalCase contract by the
/// controller): the uploaded file name + the server-side location of the signed PDF.</summary>
public sealed record DocFlowUploadResult(string FileName, string FileServer);
