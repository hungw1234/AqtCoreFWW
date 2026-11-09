using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Forms.Domain.Entities;

/// <summary>
/// ViewFilter — child entity of ViewDefinition.
/// Defines a pre-configured filter predicate for a view column.
/// IsRequired=true means the filter must be provided before the view renders.
/// T1 entity: non-generic AuditableEntity (long Id DB-assigned).
/// </summary>
public sealed class ViewFilter : AuditableEntity
{
    public long ViewDefinitionId { get; private set; }
    public string FieldName { get; private set; } = string.Empty;
    public FilterOperator Operator { get; private set; }
    public string? DefaultValue { get; private set; }
    public bool IsRequired { get; private set; }

    private ViewFilter() { } // EF Core

    public static ViewFilter Create(
        long viewDefinitionId,
        string fieldName,
        FilterOperator @operator,
        string? defaultValue = null,
        bool isRequired = false,
        Guid? createdBy = null)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("FieldName cannot be empty.", nameof(fieldName));

        var filter = new ViewFilter
        {
            ViewDefinitionId = viewDefinitionId,
            FieldName = fieldName,
            Operator = @operator,
            DefaultValue = defaultValue,
            IsRequired = isRequired
        };

        if (createdBy.HasValue)
            filter.SetAuditCreate(createdBy.Value);

        return filter;
    }
}
