using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Exceptions;
using AqtCoreFW.SharedKernel.Domain;
using System.Text.RegularExpressions;

namespace AqtCoreFW.Forms.Domain.Entities;

/// <summary>
/// FormField — child entity of FormTemplate.
/// FieldKey is SQL-safe and immutable after first submission.
/// IsActive=false = soft-delete; column never dropped in Table mode.
/// T1 entity: non-generic Entity (long Id DB-assigned).
/// </summary>
public sealed class FormField : Entity
{
    public long FormTemplateId { get; private set; }
    public long? ParentFieldId { get; private set; }
    public string FieldKey { get; private set; } = string.Empty;
    public FormFieldType Type { get; private set; }
    public string LabelVi { get; private set; } = string.Empty;
    public string LabelEn { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }
    public bool Required { get; private set; }
    public bool IsActive { get; private set; }
    public int AddedInVersion { get; private set; }
    public string? ValidationRulesJson { get; private set; }
    /// <summary>JSON config for EnumRef/InternalRef/ExternalRef data source resolution.</summary>
    public string? DataSourceJson { get; private set; }
    /// <summary>JSON config for Formula field NCalc expression (Phase 2).</summary>
    public string? FormulaJson { get; private set; }
    /// <summary>
    /// True if this field stores Personal Identifiable Information (PII).
    /// RTBF anonymization only replaces values of IsPii=true fields — non-PII fields are retained.
    /// </summary>
    public bool IsPii { get; private set; }

    /// <summary>JSON array of visibility conditions. Field shows when ALL conditions are met.
    /// Format: [{"fieldKey":"x","operator":"eq","value":"yes"}]
    /// Operators: eq, neq, gt, lt, gte, lte, contains, in
    /// null = always visible (default)</summary>
    public string? VisibilityRulesJson { get; private set; }

    /// <summary>JSON array of required-if conditions. Field becomes required only when ALL conditions are met.
    /// Same format as VisibilityRulesJson: [{"fieldKey":"x","operator":"eq","value":"yes"}]
    /// null = Required property is used as-is (default)</summary>
    public string? RequiredIfJson { get; private set; }

    /// <summary>JSON envelope describing where to source this field's value when rendering inside a BP-Run.
    /// Format: {"sourceFormTemplateId":"&lt;guid&gt;","sourceFieldKey":"&lt;key&gt;"} (envelope v1, BP MVP P6).
    /// null = no prefill (default). Resolver consumes the published-snapshot copy via FormTemplateVersion,
    /// so live edits only take effect on the next Publish/Republish.</summary>
    public string? PrefillSourceJson { get; private set; }

    private readonly List<FormFieldOption> _options = [];
    public IReadOnlyCollection<FormFieldOption> Options => _options.AsReadOnly();

    private FormField() { } // EF Core

    public static FormField Create(
        long formTemplateId,
        string fieldKey,
        FormFieldType type,
        string labelVi,
        string labelEn,
        int displayOrder,
        bool required,
        int addedInVersion,
        string? validationRulesJson = null,
        long? parentFieldId = null,
        string? dataSourceJson = null,
        string? formulaJson = null,
        bool isPii = false,
        string? visibilityRulesJson = null,
        string? requiredIfJson = null,
        string? prefillSourceJson = null)
    {
        ValidateFieldKey(fieldKey);
        return new FormField
        {
            FormTemplateId = formTemplateId,
            ParentFieldId = parentFieldId,
            FieldKey = fieldKey,
            Type = type,
            LabelVi = labelVi,
            LabelEn = labelEn,
            DisplayOrder = displayOrder,
            Required = required,
            IsActive = true,
            AddedInVersion = addedInVersion,
            ValidationRulesJson = validationRulesJson,
            DataSourceJson = dataSourceJson,
            FormulaJson = formulaJson,
            // PDPL: Address fields contain PII by default — auto-enable unless explicitly set
            IsPii = isPii || type == FormFieldType.AddressField,
            VisibilityRulesJson = visibilityRulesJson,
            RequiredIfJson = requiredIfJson,
            PrefillSourceJson = prefillSourceJson
        };
    }

    public void SetPii(bool isPii) => IsPii = isPii;

    public void UpdateDataSource(string? dataSourceJson) => DataSourceJson = dataSourceJson;
    public void UpdateFormula(string? formulaJson) => FormulaJson = formulaJson;
    public void UpdatePrefillSource(string? prefillSourceJson) => PrefillSourceJson = prefillSourceJson;

    public void Deactivate() => IsActive = false;

    public void UpdateLabels(string labelVi, string labelEn)
    {
        LabelVi = labelVi;
        LabelEn = labelEn;
    }

    public void UpdateDisplayOrder(int order) => DisplayOrder = order;

    public void UpdateValidationRules(string? rulesJson) => ValidationRulesJson = rulesJson;

    public void UpdateVisibilityRules(string? json) => VisibilityRulesJson = json;

    public void UpdateRequiredIf(string? requiredIfJson) => RequiredIfJson = requiredIfJson;

    public void UpdateRequired(bool required) => Required = required;

    public void AddOption(FormFieldOption option) => _options.Add(option);

    /// <summary>
    /// FieldKey must be SQL-safe: starts with letter/underscore, alphanumeric+underscore, max 128.
    /// Regex: ^[a-zA-Z_][a-zA-Z0-9_]{0,127}$
    /// </summary>
    public static void ValidateFieldKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidFormSchemaException("GOV_FRM_011: FieldKey không được để trống.");

        if (!Regex.IsMatch(key, @"^[a-zA-Z_][a-zA-Z0-9_]{0,127}$"))
            throw new InvalidFormSchemaException(
                "GOV_FRM_011: FieldKey phải bắt đầu bằng chữ cái hoặc '_', chỉ chứa chữ cái, số, '_', tối đa 128 ký tự.");
    }
}
