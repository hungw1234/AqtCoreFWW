using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Forms.Domain.Entities;

/// <summary>
/// ViewColumn — child entity of ViewDefinition.
/// Defines a single column to render in a view: field mapping, label, width, sort/filter flags, formatter.
/// DisplayOrder controls left-to-right column position.
/// T1 entity: non-generic AuditableEntity (long Id DB-assigned).
/// </summary>
public sealed class ViewColumn : AuditableEntity
{
    public long ViewDefinitionId { get; private set; }
    public string FieldName { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;
    public int? Width { get; private set; }
    public bool Sortable { get; private set; }
    public bool Filterable { get; private set; }
    public int DisplayOrder { get; private set; }
    public ColumnFormatter Formatter { get; private set; }

    private ViewColumn() { } // EF Core

    public static ViewColumn Create(
        long viewDefinitionId,
        string fieldName,
        string label,
        int displayOrder,
        bool sortable = false,
        bool filterable = false,
        int? width = null,
        ColumnFormatter formatter = ColumnFormatter.Text,
        Guid? createdBy = null)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("FieldName cannot be empty.", nameof(fieldName));
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Label cannot be empty.", nameof(label));

        var col = new ViewColumn
        {
            ViewDefinitionId = viewDefinitionId,
            FieldName = fieldName,
            Label = label,
            DisplayOrder = displayOrder,
            Sortable = sortable,
            Filterable = filterable,
            Width = width,
            Formatter = formatter
        };

        if (createdBy.HasValue)
            col.SetAuditCreate(createdBy.Value);

        return col;
    }
}
