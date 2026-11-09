using AqtCoreFW.Forms.Application.Commands.SubmitForm;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.SubmitPublicForm;

/// <summary>
/// Submit form data anonymously via public URL.
/// SubmittedBy = Guid.Empty (guest/anonymous submission).
///
/// <para><b>[AllowAnonymous] — tenant is NOT on the principal</b>: it is resolved server-side from the
/// FormTemplate row keyed by the globally-unique <see cref="TemplateCode"/>. So this command is
/// <see cref="ITenantAgnosticCommand"/> and opts out of <c>TenantAwareBehavior</c>'s ambient-tenant
/// enforcement (which an anonymous request can never satisfy). The handler takes full responsibility for
/// tenant safety: it pins SESSION_CONTEXT to the template-resolved tenant before the RLS-protected
/// submission write. Mirrors the anonymous signing commands (e.g. <c>CompleteDocFlowUploadCommand</c>).</para>
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record SubmitPublicFormCommand(
    string TemplateCode,
    Dictionary<string, object?> Data,
    bool ConsentGiven = false,
    string? ClientIp = null,
    string? UserAgent = null,
    SignaturePayload? Signature = null) : ICommand<Guid>, ITenantAgnosticCommand;
