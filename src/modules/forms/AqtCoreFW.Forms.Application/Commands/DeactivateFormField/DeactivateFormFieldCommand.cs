using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.DeactivateFormField;

/// <summary>
/// Soft-delete a FormField (IsActive=false).
/// In Table mode the column is preserved — data is not lost.
/// TemplatePublicId = FormTemplate.PublicId (Guid AK — external addressing).
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeactivateFormFieldCommand(
    Guid TemplatePublicId,
    // arch:accept-long T1 FormField: child of T2 FormTemplate aggregate, addressed via parent PublicId
    long FieldId,
    Guid TenantId,
    Guid UpdatedBy) : ICommand;
