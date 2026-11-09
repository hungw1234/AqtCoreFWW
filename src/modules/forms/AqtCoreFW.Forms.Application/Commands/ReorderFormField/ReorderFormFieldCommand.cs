using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.ReorderFormField;

/// <summary>
/// Update the DisplayOrder of a single FormField.
/// TemplatePublicId = FormTemplate.PublicId (Guid AK — external addressing).
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record ReorderFormFieldCommand(
    Guid TemplatePublicId,
    // arch:accept-long T1 FormField: child of T2 FormTemplate aggregate, addressed via parent PublicId
    long FieldId,
    Guid TenantId,
    int NewOrder) : ICommand;
