using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.AddFormField;

/// <summary>
/// Add a FormField to an existing template.
/// Allowed in Draft or Active status.
/// Active + Table mode: triggers ALTER TABLE ADD COLUMN.
/// Set ParentFieldId to add a child field inside a TableField.
/// Active + Table mode + child field: triggers ALTER on the child table.
/// TemplatePublicId = FormTemplate.PublicId (Guid AK — external addressing).
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record AddFormFieldCommand(
    Guid TemplatePublicId,
    Guid TenantId,
    string FieldKey,
    FormFieldType Type,
    string LabelVi,
    string LabelEn,
    int DisplayOrder,
    bool Required,
    string? ValidationRulesJson,
    List<AddFormFieldOptionDto>? Options,
    Guid UpdatedBy,
    /// <summary>JSON config for EnumRef/InternalRef/ExternalRef — required for reference types.</summary>
    string? DataSourceJson = null,
    /// <summary>JSON config for Formula type — required when Type = Formula.</summary>
    string? FormulaJson = null,
    /// <summary>Set to the parent TableField's Id when adding a child column inside a TableField.</summary>
    long? ParentFieldId = null,
    /// <summary>JSON array of visibility conditions — null means always visible.</summary>
    string? VisibilityRulesJson = null,
    /// <summary>BP MVP P6: prefill source pointer JSON (envelope v1) — null means no prefill.</summary>
    string? PrefillSourceJson = null) : ICommand<FormFieldDto>;

/// <summary>Input DTO for a static dropdown option on the new field.</summary>
public sealed record AddFormFieldOptionDto(
    string Value,
    string LabelVi,
    string LabelEn,
    int DisplayOrder);
