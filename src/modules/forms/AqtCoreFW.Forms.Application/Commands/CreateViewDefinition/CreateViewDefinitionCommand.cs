using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.CreateViewDefinition;

/// <summary>
/// Creates a new ViewDefinition with an optional initial set of columns and filters.
/// Name must be unique per EntityType+Tenant combination is enforced at the domain level.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateViewDefinitionCommand(
    Guid TenantId,
    string Name,
    string EntityType,
    ViewType Type,
    Guid CreatedBy,
    string? DefaultSortField = null,
    string? DefaultSortDir = null,
    int PageSize = 20,
    bool IsDefault = false,
    List<CreateViewColumnDto>? Columns = null,
    List<CreateViewFilterDto>? Filters = null) : ICommand<ViewDefinitionDto>;

/// <summary>Column definition payload used when creating a ViewDefinition.</summary>
public sealed record CreateViewColumnDto(
    string FieldName,
    string Label,
    int DisplayOrder,
    bool Sortable = false,
    bool Filterable = false,
    int? Width = null,
    ColumnFormatter Formatter = ColumnFormatter.Text);

/// <summary>Filter definition payload used when creating a ViewDefinition.</summary>
public sealed record CreateViewFilterDto(
    string FieldName,
    FilterOperator Operator,
    string? DefaultValue = null,
    bool IsRequired = false);
