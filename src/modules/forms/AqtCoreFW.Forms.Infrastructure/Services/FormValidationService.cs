using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// Validates form submission data against active FormField entities.
/// Replaces JSON schema parsing — reads typed FormField.Type for validation rules.
/// ValidationRulesJson deserialized per field for Min/Max/MinLength/MaxLength/Pattern checks.
/// Regex validated with 100ms timeout — ReDoS guard (GOV_FRM_005).
///
/// M1 slice 6b: when constructed with an <see cref="IDataSourceResolverFactory"/> AND the caller
/// passes a non-null tenantId, runs the in-row cascade membership guard as a final step. Both
/// dependencies are optional (fail-open) so legacy callers and unit tests that new-up the service
/// without DI keep compiling and skip only the cascade check.
/// </summary>
public sealed class FormValidationService : IFormValidationService
{
    // UI-only and computed types — skip validation entirely
    private static readonly HashSet<FormFieldType> SkipValidationTypes =
    [
        FormFieldType.Section,
        FormFieldType.Label,
        FormFieldType.Divider,
        FormFieldType.Formula
    ];

    private readonly InRowCascadeMembershipValidator? _cascadeGuard;

    /// <summary>Backward-compatible parameterless ctor for legacy callers / unit tests.</summary>
    public FormValidationService()
    {
        _cascadeGuard = null;
    }

    /// <summary>DI ctor: enables the in-row cascade membership guard when tenantId is supplied at call time.</summary>
    public FormValidationService(
        IDataSourceResolverFactory? resolverFactory,
        ILogger<FormValidationService>? logger)
    {
        _cascadeGuard = resolverFactory is null
            ? null
            : new InRowCascadeMembershipValidator(
                resolverFactory,
                (logger as ILogger<InRowCascadeMembershipValidator>)
                    ?? NullLogger<InRowCascadeMembershipValidator>.Instance);
    }

    public async Task<Result<List<ValidationError>>> ValidateAsync(
        IEnumerable<FormField> fields,
        IDictionary<string, object?> data,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();
        // Materialize once — cascade guard also enumerates the same collection.
        var fieldList = fields as IReadOnlyList<FormField> ?? fields.ToList();
        fields = fieldList;

        foreach (var field in fields)
        {
            // UI-only and computed fields — no validation
            if (SkipValidationTypes.Contains(field.Type))
                continue;

            // Skip fields whose visibility conditions evaluate to false (field is hidden)
            if (!EvaluateVisibility(field, data))
                continue;

            data.TryGetValue(field.FieldKey, out var rawValue);
            var strValue = rawValue?.ToString()?.Trim() ?? string.Empty;

            // Required check — evaluate RequiredIfJson if present, otherwise use field.Required
            var isRequired = string.IsNullOrWhiteSpace(field.RequiredIfJson)
                ? field.Required
                : field.Required || EvaluateConditions(field.RequiredIfJson, data);

            if (isRequired && string.IsNullOrEmpty(strValue))
            {
                errors.Add(new ValidationError(
                    $"Field '{field.FieldKey}' là bắt buộc.", field.FieldKey));
                continue;
            }

            if (string.IsNullOrEmpty(strValue))
                continue;

            // Type-based validation
            switch (field.Type)
            {
                case FormFieldType.Number:
                    ValidateNumber(field, strValue, errors);
                    break;

                case FormFieldType.Date:
                    if (!DateTimeOffset.TryParse(strValue, out _))
                        errors.Add(new ValidationError(
                            $"Field '{field.FieldKey}' phải là ngày hợp lệ.", field.FieldKey));
                    break;

                case FormFieldType.Boolean:
                    if (!bool.TryParse(strValue, out _)
                        && strValue != "0" && strValue != "1"
                        && strValue != "true" && strValue != "false")
                    {
                        errors.Add(new ValidationError(
                            $"Field '{field.FieldKey}' phải là giá trị boolean.", field.FieldKey));
                    }
                    break;

                case FormFieldType.EnumRef:
                case FormFieldType.ExternalRef:
                    // Non-empty string required if Required (already checked above)
                    break;

                case FormFieldType.InternalRef:
                    // Should be a valid GUID string
                    if (!Guid.TryParse(strValue, out _))
                        errors.Add(new ValidationError(
                            $"Field '{field.FieldKey}' phải là GUID hợp lệ.", field.FieldKey));
                    break;

                case FormFieldType.AddressField:
                    ValidateAddressField(field, rawValue, errors);
                    break;

                case FormFieldType.DateRange:
                    ValidateDateRange(field, rawValue, errors);
                    break;
            }

            // Rules from ValidationRulesJson (optional)
            var rules = ParseRules(field.ValidationRulesJson);
            if (rules is null) continue;

            // String length checks (Text, Textarea)
            if (field.Type is FormFieldType.Text or FormFieldType.Textarea)
            {
                if (rules.MinLength.HasValue && strValue.Length < rules.MinLength.Value)
                    errors.Add(new ValidationError(
                        $"Field '{field.FieldKey}' phải có ít nhất {rules.MinLength.Value} ký tự.",
                        field.FieldKey));

                if (rules.MaxLength.HasValue && strValue.Length > rules.MaxLength.Value)
                    errors.Add(new ValidationError(
                        $"Field '{field.FieldKey}' không được vượt quá {rules.MaxLength.Value} ký tự.",
                        field.FieldKey));
            }

            // Regex pattern validation — 100ms timeout to prevent ReDoS
            if (!string.IsNullOrEmpty(rules.Pattern))
            {
                try
                {
                    var isMatch = Regex.IsMatch(strValue, rules.Pattern,
                        RegexOptions.None, TimeSpan.FromMilliseconds(100));

                    if (!isMatch)
                        errors.Add(new ValidationError(
                            $"Field '{field.FieldKey}' không khớp với định dạng yêu cầu.",
                            field.FieldKey));
                }
                catch (RegexMatchTimeoutException)
                {
                    errors.Add(new ValidationError(
                        $"Field '{field.FieldKey}': kiểm tra định dạng quá hạn ({FormErrors.Codes.PatternTimeout}).",
                        field.FieldKey));
                }
            }
        }

        // M1 slice 6b: run the in-row cascade membership guard as a final step. Fail-open when either
        // the resolver factory is absent (legacy DI) or the caller did not supply a tenantId (unit tests).
        if (_cascadeGuard is not null && tenantId is Guid tid)
        {
            await _cascadeGuard.ValidateAsync(fieldList, data, tid, errors, cancellationToken);
        }

        return Result.Ok(errors);
    }

    private static void ValidateAddressField(
        FormField field, object? rawValue, List<ValidationError> errors)
    {
        if (rawValue is null) return;

        try
        {
            var json = rawValue is string s ? s : JsonSerializer.Serialize(rawValue);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Must have province/ward properties. The address model is 2-level (Tỉnh → Phường);
            // the intermediate district level was removed with the hệ-34 administrative reform.
            if (!root.TryGetProperty("province", out _) ||
                !root.TryGetProperty("ward", out _))
            {
                errors.Add(new ValidationError(
                    $"Field '{field.FieldKey}' phải có đầy đủ tỉnh/thành (province), phường/xã (ward).",
                    field.FieldKey));
            }
        }
        catch
        {
            errors.Add(new ValidationError(
                $"Field '{field.FieldKey}' phải là đối tượng địa chỉ hợp lệ (JSON).", field.FieldKey));
        }
    }

    private static void ValidateDateRange(
        FormField field, object? rawValue, List<ValidationError> errors)
    {
        if (rawValue is null) return;

        try
        {
            var json = rawValue is string s ? s : JsonSerializer.Serialize(rawValue);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("from", out var fromEl) ||
                !root.TryGetProperty("to", out var toEl))
            {
                errors.Add(new ValidationError(
                    $"Field '{field.FieldKey}' phải có 'from' và 'to'.", field.FieldKey));
                return;
            }

            if (!DateTimeOffset.TryParse(fromEl.GetString(), out var from) ||
                !DateTimeOffset.TryParse(toEl.GetString(), out var to))
            {
                errors.Add(new ValidationError(
                    $"Field '{field.FieldKey}': 'from' và 'to' phải là ngày hợp lệ.", field.FieldKey));
                return;
            }

            if (from > to)
                errors.Add(new ValidationError(
                    $"Field '{field.FieldKey}': ngày bắt đầu (from) phải <= ngày kết thúc (to).",
                    field.FieldKey));
        }
        catch
        {
            errors.Add(new ValidationError(
                $"Field '{field.FieldKey}' phải là khoảng ngày hợp lệ (JSON).", field.FieldKey));
        }
    }

    private static void ValidateNumber(
        FormField field, string strValue, List<ValidationError> errors)
    {
        if (!decimal.TryParse(strValue,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var numVal))
        {
            errors.Add(new ValidationError(
                $"Field '{field.FieldKey}' phải là số hợp lệ.", field.FieldKey));
            return;
        }

        var rules = ParseRules(field.ValidationRulesJson);
        if (rules is null) return;

        if (rules.Min.HasValue && numVal < rules.Min.Value)
            errors.Add(new ValidationError(
                $"Field '{field.FieldKey}' phải >= {rules.Min.Value}.", field.FieldKey));

        if (rules.Max.HasValue && numVal > rules.Max.Value)
            errors.Add(new ValidationError(
                $"Field '{field.FieldKey}' phải <= {rules.Max.Value}.", field.FieldKey));
    }

    /// <summary>
    /// Evaluates VisibilityRulesJson against submitted data.
    /// Returns true (visible) when: rules are null/empty OR all conditions in the array are met.
    /// Operators: eq, neq, gt, lt, gte, lte, contains, in
    /// </summary>
    private static bool EvaluateVisibility(FormField field, IDictionary<string, object?> data)
    {
        if (string.IsNullOrWhiteSpace(field.VisibilityRulesJson))
            return true; // null = always visible

        return EvaluateConditions(field.VisibilityRulesJson, data);
    }

    /// <summary>Evaluates a JSON conditions array. Returns true if ALL conditions are met.</summary>
    private static bool EvaluateConditions(string conditionsJson, IDictionary<string, object?> data)
    {
        try
        {
            using var doc = JsonDocument.Parse(conditionsJson);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
                return true; // malformed — treat as satisfied

            foreach (var condition in root.EnumerateArray())
            {
                if (!condition.TryGetProperty("fieldKey", out var fieldKeyEl)
                    || !condition.TryGetProperty("operator", out var opEl)
                    || !condition.TryGetProperty("value", out var valueEl))
                    continue; // incomplete condition — skip

                var fieldKey = fieldKeyEl.GetString() ?? string.Empty;
                var op = opEl.GetString() ?? string.Empty;
                var expected = valueEl.GetString() ?? string.Empty;

                data.TryGetValue(fieldKey, out var actual);
                var actualStr = actual?.ToString() ?? string.Empty;

                var conditionMet = op switch
                {
                    "eq" => string.Equals(actualStr, expected, StringComparison.OrdinalIgnoreCase),
                    "neq" => !string.Equals(actualStr, expected, StringComparison.OrdinalIgnoreCase),
                    "contains" => actualStr.Contains(expected, StringComparison.OrdinalIgnoreCase),
                    "in" => expected.Split(',').Any(v =>
                        string.Equals(v.Trim(), actualStr, StringComparison.OrdinalIgnoreCase)),
                    "gt" => decimal.TryParse(actualStr, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var aGt)
                        && decimal.TryParse(expected, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var eGt)
                        && aGt > eGt,
                    "lt" => decimal.TryParse(actualStr, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var aLt)
                        && decimal.TryParse(expected, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var eLt)
                        && aLt < eLt,
                    "gte" => decimal.TryParse(actualStr, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var aGte)
                        && decimal.TryParse(expected, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var eGte)
                        && aGte >= eGte,
                    "lte" => decimal.TryParse(actualStr, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var aLte)
                        && decimal.TryParse(expected, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var eLte)
                        && aLte <= eLte,
                    _ => true // unknown operator — treat as satisfied
                };

                // ALL conditions must be met (AND logic)
                if (!conditionMet)
                    return false;
            }

            return true;
        }
        catch
        {
            return true; // parse error — treat as visible
        }
    }

    private static ValidationRules? ParseRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ValidationRules>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Internal rule model — deserialized from FormField.ValidationRulesJson.</summary>
    private sealed record ValidationRules(
        int? MinLength,
        int? MaxLength,
        decimal? Min,
        decimal? Max,
        string? Pattern);
}
