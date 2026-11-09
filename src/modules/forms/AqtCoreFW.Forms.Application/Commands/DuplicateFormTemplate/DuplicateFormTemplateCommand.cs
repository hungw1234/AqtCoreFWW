using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.DuplicateFormTemplate;

/// <summary>
/// Clone an existing template into a new Draft with the same fields (new IDs).
/// Submissions are NOT copied — new template starts empty.
/// StorageMode is inherited from the source template.
/// SourcePublicId = source FormTemplate.PublicId (Guid AK — external addressing).
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DuplicateFormTemplateCommand(
    Guid SourcePublicId,
    Guid TenantId,
    string NewName,
    string NewCode,
    Guid CreatedBy) : ICommand<FormTemplateDto>;
