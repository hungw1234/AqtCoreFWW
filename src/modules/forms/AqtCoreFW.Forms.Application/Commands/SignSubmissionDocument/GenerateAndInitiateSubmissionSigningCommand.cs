using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentResults;

namespace AqtCoreFW.Forms.Application.Commands.SignSubmissionDocument;

/// <summary>
/// Initiates a USB DocFlow signing request for a submission's server-generated PDF.
///
/// Flow:
///   1. Load the submission (tenant-scoped; cross-tenant → 404).
///   2. Generate PDF bytes via ISubmissionPdfComposer (no PDF parsing by this command).
///   3. Mint a fresh docId; upload bytes to signing-original bucket at tenant-prefixed key.
///   4. Call ISigningRequestClient (Identity cross-module client) with ArtifactKind="FormPdf".
///   5. On success: link the request id to the submission (at-most-once, GOV_FRM_030 on double-link).
///   6. Return { RequestId, ClientHandoff } for FE to drive the VGCA plugin ceremony.
///
/// Self-sign only: AssignedSignerId == InitiatedBy == authenticated caller.
/// Tenant from JWT only — TenantId is injected by the controller, NEVER from route/body.
/// MFA is enforced at the controller level ([RequiresMfa]).
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record GenerateAndInitiateSubmissionSigningCommand(
    /// <summary>Submission.PublicId (T2 external AK) of the submission to sign.</summary>
    Guid SubmissionPublicId,
    /// <summary>Tenant resolved from JWT — never from route or request body.</summary>
    Guid TenantId,
    /// <summary>Authenticated caller; also used as AssignedSignerId (self-sign only this round).</summary>
    Guid InitiatedBy,
    /// <summary>
    /// VGCA plugin function to drive. Valid values: Issued | Approved | Income | Comment | Appendix | Copy | Files.
    /// Validated by <see cref="GenerateAndInitiateSubmissionSigningCommandValidator"/>.
    /// </summary>
    string SignType,
    /// <summary>Document reference number (số hiệu văn bản) shown in the visible VGCA stamp.</summary>
    string DocNumber,
    /// <summary>Issuance date (ngày ban hành) shown in the visible VGCA stamp.</summary>
    DateTime IssuedDate,
    /// <summary>
    /// Tenant signing-provider key (e.g. "usb-vgca"). Supplied by FE from the tenant's
    /// configured provider list — never accepted as raw client input for arbitrary providers.
    /// </summary>
    string ProviderKey,
    /// <summary>
    /// API host base URL (e.g. "https://api.example.com") derived server-side by the controller
    /// from the incoming HTTP request (Scheme + Host). NOT accepted from the FE payload — prevents
    /// SSRF via malicious BaseUrl injection. Required by DocFlowSigningStrategy to construct the
    /// VGCA handler callback URL ("{BaseUrl}/api/signing/docflow/file").
    /// </summary>
    string BaseUrl)
    : ICommand<GenerateAndInitiateSubmissionSigningResult>;

/// <summary>
/// Result returned to the FE after a signing request is successfully initiated.
/// The FE uses <see cref="RequestId"/> to poll <c>GET /api/signing/request/{requestId}/status</c>
/// and passes <see cref="ClientHandoff"/> to the VGCA plugin (handlerUrl, jwt, docId).
/// </summary>
public sealed record GenerateAndInitiateSubmissionSigningResult(
    Guid RequestId,
    IReadOnlyDictionary<string, string> ClientHandoff);
