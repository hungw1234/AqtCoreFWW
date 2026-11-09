namespace AqtCoreFW.Forms.Application.DTOs;

/// <summary>Read model for a ViewDefinition with all columns and filters included.</summary>
public sealed record ViewDefinitionDto(
    long Id,
    Guid TenantId,
    string Name,
    string EntityType,
    string Type,
    string? DefaultSortField,
    string? DefaultSortDir,
    int PageSize,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    List<ViewColumnDto> Columns,
    List<ViewFilterDto> Filters);

/// <summary>Read model for a single ViewColumn.</summary>
public sealed record ViewColumnDto(
    long Id,
    long ViewDefinitionId,
    string FieldName,
    string Label,
    int? Width,
    bool Sortable,
    bool Filterable,
    int DisplayOrder,
    string Formatter);

/// <summary>Read model for a single ViewFilter.</summary>
public sealed record ViewFilterDto(
    long Id,
    long ViewDefinitionId,
    string FieldName,
    string Operator,
    string? DefaultValue,
    bool IsRequired);
