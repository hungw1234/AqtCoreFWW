using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.ListViewDefinitions;

/// <summary>
/// Returns a paginated list of ViewDefinitions for a tenant.
/// Columns and Filters are NOT included in list results — use GetViewDefinitionQuery for details.
/// </summary>
public sealed record ListViewDefinitionsQuery(
    Guid TenantId,
    int Page = 1,
    int PageSize = 20) : IQuery<ListViewDefinitionsResult>;

/// <summary>Paginated result wrapper for ListViewDefinitionsQuery.</summary>
public sealed record ListViewDefinitionsResult(
    List<ViewDefinitionDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
