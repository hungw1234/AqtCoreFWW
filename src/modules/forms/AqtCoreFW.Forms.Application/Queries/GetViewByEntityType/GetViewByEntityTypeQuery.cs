using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.GetViewByEntityType;

/// <summary>
/// Returns the default ViewDefinition for an EntityType+Tenant, falling back to the first one
/// if no IsDefault view exists. Returns columns and filters.
/// </summary>
public sealed record GetViewByEntityTypeQuery(
    string EntityType,
    Guid TenantId) : IQuery<ViewDefinitionDto>;
