using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.GetFormTemplateSignerRoles;

/// <summary>List all signer roles for a template, ordered by DisplayOrder.</summary>
public sealed record GetFormTemplateSignerRolesQuery(
    Guid TemplatePublicId,
    Guid TenantId) : IQuery<IReadOnlyList<FormTemplateSignerRoleDto>>;
