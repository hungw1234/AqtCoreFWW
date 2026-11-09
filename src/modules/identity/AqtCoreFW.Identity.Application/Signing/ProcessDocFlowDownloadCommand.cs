using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Serves the PDF to be signed for a VGCA Tier-3 doc-flow download (W3, nonce slot 1). Validates +
/// consumes the nonce, asserts tenant isolation, fetches the bytes, then commits the
/// Initiated → Downloaded transition — in that order, so a missing/unreachable object leaves the nonce
/// and record untouched for the desktop service to retry (the fetch-before-commit invariant).
///
/// <para><b>[AllowAnonymous] system call.</b> The desktop service authenticates with a signed JWT whose
/// tenant_id claim — not the request principal — is the tenant. The command is
/// <see cref="ITenantAgnosticCommand"/> (no ambient tenant); the handler pins SESSION_CONTEXT to the
/// JWT tenant so the RLS-protected signing tables filter to it, and the explicit tenant assertions stay
/// as defense-in-depth.</para>
/// </summary>
// The download consumes a nonce + marks the record Downloaded but is not itself an audited event — the
// audited signing commit is the upload. [SuppressAutoAudit] keeps it off the auto-audit path; the
// [AuditCategory] is the required category metadata every command declares (coverage guard).
[AuditCategory(AuditCategory.AppAccess)]
[SuppressAutoAudit]
public sealed record ProcessDocFlowDownloadCommand(
    Guid DocId,
    Guid JwtTenant,
    Guid UserId,
    Guid Nonce) : ICommand<DocFlowDownloadResult>, ITenantAgnosticCommand;

/// <summary>The PDF stream the desktop service downloads + the file name to present. Caller streams it
/// straight to the HTTP response (no buffering).</summary>
public sealed record DocFlowDownloadResult(Stream Content, string FileName);
