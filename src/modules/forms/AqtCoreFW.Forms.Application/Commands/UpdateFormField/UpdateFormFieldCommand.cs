using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.UpdateFormField;

/// <summary>
/// Update mutable properties of an existing form field.
/// Only allowed on Draft templates (FieldKey and Type are immutable).
/// TemplatePublicId = FormTemplate.PublicId (Guid AK — external addressing).
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateFormFieldCommand(
    Guid TemplatePublicId,
    // arch:accept-long T1 FormField: child of T2 FormTemplate aggregate, addressed via parent PublicId
    long FieldId,
    Guid TenantId,
    string? LabelVi,
    string? LabelEn,
    bool? Required,
    string? ValidationRulesJson,
    string? DataSourceJson,
    string? FormulaJson,
    bool? IsPii,
    Guid UpdatedBy,
    /// <summary>JSON array of visibility conditions — null = no change; empty string = clear rules.</summary>
    string? VisibilityRulesJson = null,
    /// <summary>JSON array of required-if conditions — null = no change; empty string = clear rules.</summary>
    string? RequiredIfJson = null,
    /// <summary>BP MVP P6: prefill source pointer JSON — null = no change; empty string = clear pointer.</summary>
    string? PrefillSourceJson = null) : ICommand<FormFieldDto>;
