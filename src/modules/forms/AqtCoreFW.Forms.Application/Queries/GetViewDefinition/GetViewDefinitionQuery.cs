using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.GetViewDefinition;

/// <summary>Returns a single ViewDefinition with Columns and Filters by ID. Id = long (T1 internal PK).</summary>
public sealed record GetViewDefinitionQuery(
    long Id,
    Guid TenantId) : IQuery<ViewDefinitionDto>;
