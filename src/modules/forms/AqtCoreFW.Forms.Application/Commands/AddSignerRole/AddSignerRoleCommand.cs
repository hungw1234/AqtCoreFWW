using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.AddSignerRole;

/// <summary>
/// Add a signer role slot to an existing template.
/// Allowed when template is in Draft or Active status.
/// RoleKey must be non-empty and unique within the template.
/// TemplatePublicId = FormTemplate.PublicId (Guid AK — external addressing).
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record AddSignerRoleCommand(
    Guid TemplatePublicId,
    Guid TenantId,
    string RoleKey,
    string RoleLabelVi,
    int DisplayOrder,
    bool IsRequired,
    Guid UpdatedBy,
    string? PlacementJson = null) : ICommand<FormTemplateSignerRoleDto>;
