using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.PublishFormTemplate;

/// <summary>
/// Activates a form template so it can accept submissions.
/// TemplatePublicId = FormTemplate.PublicId (Guid AK — external addressing).
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record PublishFormTemplateCommand(
    Guid TemplatePublicId,
    Guid TenantId,
    Guid UpdatedBy) : ICommand;
