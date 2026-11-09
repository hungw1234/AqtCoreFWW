using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.RepublishFormTemplate;

/// <summary>
/// Re-publishes an already-Active template: bumps Version and emits a fresh
/// FormTemplateVersion snapshot of current schema state.
/// Closes the drift introduced by AddField on Active templates (BP MVP P1-FU).
/// TemplatePublicId = FormTemplate.PublicId (Guid AK — external addressing).
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record RepublishFormTemplateCommand(
    Guid TemplatePublicId,
    Guid TenantId,
    Guid UpdatedBy) : ICommand;
