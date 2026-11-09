namespace AqtCoreFW.Forms.Application.DTOs;

/// <summary>API projection of a FormField entity.</summary>
public sealed record FormFieldDto(
    long Id,
    string FieldKey,
    string Type,
    string LabelVi,
    string LabelEn,
    int DisplayOrder,
    bool Required,
    bool IsActive,
    int AddedInVersion,
    FormFieldValidationRulesDto? ValidationRules,
    IReadOnlyList<FormFieldOptionDto>? Options,
    /// <summary>Raw JSON of visibility conditions. null = always visible.</summary>
    string? VisibilityRulesJson = null,
    /// <summary>Raw JSON of required-if conditions. null = use Required property as-is.</summary>
    string? RequiredIfJson = null,
    /// <summary>
    /// Raw JSON of prefill source pointer (envelope v1). null = no prefill (default).
    /// <para>
    /// WRITE-ONLY-VIA-GET BY DESIGN: this property is declared on the DTO for symmetry
    /// with the entity (write path: AddFormField/UpdateFormField command handlers persist it),
    /// but is intentionally NOT projected by <c>GetFormTemplateQueryHandler.MapField</c> nor
    /// <c>GetPublicFormQueryHandler</c>'s inline mapper. The sole reader is the BE-only cross-module
    /// <c>IFormPrefillContextClient</c>, which walks published <c>FormField.PrefillSourceJson</c>
    /// direct from the entity (see <c>FormTemplateVersionFactory</c> snapshot path), never through
    /// the template GET endpoint. FE has zero consumers of this field on the template read path
    /// (verified 2026-08-13 for row FORMS-GET-TEMPLATE-PREFILL-SOURCE-JSON-PROJECTION-GAP:
    /// grep <c>web/src</c> for <c>prefillSourceJson</c> = 0 matches).
    /// </para>
    /// <para>
    /// If a future FE builder needs a "prefilled from X" affordance visible on the template GET,
    /// add the projection to both handlers using named-args (per
    /// <c>feedback-formtemplate-addfield-ispii-positional-trap</c>) and add FE integration coverage.
    /// </para>
    /// </summary>
    string? PrefillSourceJson = null,
    /// <summary>Internal FK to parent FormField.Id (bigint T1). Non-null for TableField child columns;
    /// null for top-level fields. Exposed so the FE table widget discovers child columns by id instead
    /// of the prefix-based fallback (fieldKey `parent.child` would collide with AddFormField regex).</summary>
    long? ParentFieldId = null);

/// <summary>API projection of a FormFieldOption entity.</summary>
public sealed record FormFieldOptionDto(
    long Id,
    string Value,
    string LabelVi,
    string LabelEn,
    int DisplayOrder);

/// <summary>Validation rule set for a field — deserialized from ValidationRulesJson.</summary>
public sealed record FormFieldValidationRulesDto(
    int? MinLength,
    int? MaxLength,
    decimal? Min,
    decimal? Max,
    /// <summary>Regex pattern — validated with 100ms timeout to prevent ReDoS.</summary>
    string? Pattern);
