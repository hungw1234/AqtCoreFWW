using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.CreateFormTemplate;

/// <summary>
/// Creates a new form template in Draft status with no fields.
/// Fields are added via AddFormFieldCommand after creation.
/// StorageMode is immutable after first submission.
/// Code must be unique per tenant and URL/SQL-safe.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateFormTemplateCommand(
    Guid TenantId,
    string Name,
    string NameVi,
    string Code,
    StorageMode StorageMode,
    Guid CreatedBy,
    bool RequiresConsent = false,
    string? ConsentText = null,
    Guid? ApprovalWorkflowDefinitionId = null) : ICommand<FormTemplateDto>;
