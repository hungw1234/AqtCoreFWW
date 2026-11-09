using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.UpdateFormTemplateCode;

/// <summary>
/// Change the unique Code (slug) of a Draft template.
/// Only allowed when Status=Draft to avoid breaking existing views.
/// TemplatePublicId = FormTemplate.PublicId (T2 Guid AK — external addressing).
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateFormTemplateCodeCommand(
    Guid TemplatePublicId,
    Guid TenantId,
    string NewCode,
    Guid UpdatedBy) : ICommand;
