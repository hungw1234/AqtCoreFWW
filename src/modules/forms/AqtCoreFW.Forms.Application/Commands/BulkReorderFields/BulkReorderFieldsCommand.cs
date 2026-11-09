using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.BulkReorderFields;

/// <summary>
/// Replace display order for all fields in a template atomically.
/// TemplatePublicId = FormTemplate.PublicId (Guid AK — external addressing).
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record BulkReorderFieldsCommand(
    Guid TemplatePublicId,
    Guid TenantId,
    IReadOnlyList<FieldOrderItem> Items) : ICommand;

/// <summary>Maps a field ID to its new display order.</summary>
// arch:accept-long T1 FormField: child of T2 FormTemplate aggregate, addressed via parent PublicId
public sealed record FieldOrderItem(long FieldId, int NewOrder);
