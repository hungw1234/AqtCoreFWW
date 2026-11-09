using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using Ganss.Xss;
using System.Text.Json;

namespace AqtCoreFW.Forms.Application.Commands.SubmitForm;

/// <summary>
/// Static helper that preprocesses raw submission data before storage:
/// - Strips Section/Label/Divider (UI-only, no storage).
/// - Flattens AddressField → {key}_province, {key}_district, {key}_ward.
/// - Flattens DateRange → {key}_from, {key}_to.
/// - Evaluates Formula → included in Json response data but NOT stored.
/// </summary>
// Public: ParseTableFieldRows is reused cross-assembly by Infrastructure (InRowCascadeMembershipValidator).
// Other members stay effectively internal via their access modifiers.
public static class FormDataPreprocessor
{
    // UI-only types stripped from storage data
    private static readonly HashSet<FormFieldType> UiOnlyTypes =
    [
        FormFieldType.Section,
        FormFieldType.Label,
        FormFieldType.Divider
    ];

    // Server-side HTML sanitizer for RichText fields — defense-in-depth against stored XSS.
    // Allowlist mirrors FE DOMPurify config in rich-text-widget.tsx.
    private static readonly HtmlSanitizer RichTextSanitizer = CreateRichTextSanitizer();

    private static HtmlSanitizer CreateRichTextSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "strong", "em", "u", "ol", "ul", "li", "a", "span" })
            sanitizer.AllowedTags.Add(tag);
        sanitizer.AllowedAttributes.Clear();
        foreach (var attr in new[] { "href", "target", "rel", "class" })
            sanitizer.AllowedAttributes.Add(attr);
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("mailto");
        return sanitizer;
    }

    internal static async Task<IDictionary<string, object?>> PreprocessDataAsync(
        IEnumerable<FormField> activeFields,
        IDictionary<string, object?> rawData,
        IFormulaEvaluationService formulaEvaluationService,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, object?>();

        foreach (var field in activeFields)
        {
            // Skip UI-only fields entirely
            if (UiOnlyTypes.Contains(field.Type))
                continue;

            rawData.TryGetValue(field.FieldKey, out var value);

            switch (field.Type)
            {
                case FormFieldType.AddressField:
                    FlattenAddress(field.FieldKey, value, result);
                    break;

                case FormFieldType.DateRange:
                    FlattenDateRange(field.FieldKey, value, result);
                    break;

                case FormFieldType.RichText:
                    // Sanitize HTML to prevent stored XSS — strips script/iframe/event handlers
                    result[field.FieldKey] = value is string html && !string.IsNullOrEmpty(html)
                        ? RichTextSanitizer.Sanitize(html)
                        : value;
                    break;

                case FormFieldType.Formula:
                    // Evaluate but do NOT store — formulas are computed on read
                    // (included here so Json-mode responses contain computed value).
                    // Real HTTP submissions arrive with scalars boxed as JsonElement (no
                    // object-unboxing converter on the host's JsonSerializerOptions), and NCalc's
                    // arithmetic operators throw on a boxed JsonElement operand, silently degrading
                    // every referencing formula to null. Normalize the whole input map to native CLR
                    // types before evaluating — the SAME normalization the endpoint-time
                    // FormulaBatchEvaluator applies, via the canonical JsonValueNormalizer.
                    if (!string.IsNullOrWhiteSpace(field.FormulaJson))
                    {
                        var evalData = JsonValueNormalizer.NormalizeDict(rawData);
                        var computed = await formulaEvaluationService
                            .EvaluateAsync(field.FormulaJson, evalData, cancellationToken)
                            .ConfigureAwait(false);
                        if (computed is not null)
                            result[field.FieldKey] = computed;
                    }
                    break;

                default:
                    // Real HTTP submissions arrive as JsonElement — SubmitFormRequest.Data is
                    // Dictionary<string, object?> and no object-unboxing JSON converter is registered,
                    // so System.Text.Json binds each scalar as a JsonElement. Table-mode storage hands
                    // these values straight to Dapper, which rejects a raw JsonElement
                    // (NotSupportedException in LookupDbType). Normalize scalar JsonElements to CLR types
                    // here via the canonical JsonValueNormalizer. Array/Object shapes (a TableField's
                    // rows) pass through untouched — the handler routes them to ParseTableFieldRows.
                    result[field.FieldKey] = value is JsonElement je
                        && je.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object)
                            ? JsonValueNormalizer.Normalize(je)
                            : value;
                    break;
            }
        }

        return result;
    }

    /// <summary>Returns storageData without keys belonging to TableField fields.</summary>
    internal static IDictionary<string, object?> ExcludeTableFieldEntries(
        IEnumerable<FormField> activeFields,
        IDictionary<string, object?> data)
    {
        var tableFieldKeys = activeFields
            .Where(f => f.Type == FormFieldType.TableField)
            .Select(f => f.FieldKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return data
            .Where(kvp => !tableFieldKeys.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Parses a TableField value (JSON array of row objects) into a list of row dictionaries.
    /// Accepts JsonElement arrays or already-deserialized List&lt;Dictionary&gt; from API binding.
    /// Returns empty list on null, non-array, or parse failure.
    /// </summary>
    // Public: cross-assembly reuse from Infrastructure (FormValidationService cascade membership guard).
    public static IReadOnlyList<IDictionary<string, object?>> ParseTableFieldRows(object? rawRows)
    {
        try
        {
            // Case 1: already deserialized as list of dictionaries (e.g. from System.Text.Json minimal API binding)
            if (rawRows is IEnumerable<object> enumerable)
            {
                var result = new List<IDictionary<string, object?>>();
                foreach (var item in enumerable)
                {
                    if (item is IDictionary<string, object?> dict)
                        result.Add(dict);
                    else if (item is JsonElement elem && elem.ValueKind == JsonValueKind.Object)
                        result.Add(DeserializeJsonRow(elem));
                }
                return result;
            }

            // Case 2: JsonElement array
            if (rawRows is JsonElement jsonEl && jsonEl.ValueKind == JsonValueKind.Array)
            {
                return jsonEl.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(DeserializeJsonRow)
                    .ToList();
            }

            // Case 3: string JSON
            if (rawRows is string json && !string.IsNullOrWhiteSpace(json))
            {
                var parsed = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
                return parsed?
                    .Select(d => (IDictionary<string, object?>)d
                        .ToDictionary(kvp => kvp.Key, kvp => JsonValueNormalizer.Normalize(kvp.Value)))
                    .ToList() ?? [];
            }
        }
        catch (Exception)
        {
            // Swallow parse errors — return empty; validation layer should have caught bad input
        }

        return [];
    }

    private static IDictionary<string, object?> DeserializeJsonRow(JsonElement element)
    {
        var row = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
            row[prop.Name] = JsonValueNormalizer.Normalize(prop.Value);
        return row;
    }

    private static void FlattenAddress(
        string fieldKey, object? value, Dictionary<string, object?> result)
    {
        if (value is null)
        {
            result[$"{fieldKey}_province"] = null;
            result[$"{fieldKey}_district"] = null;
            result[$"{fieldKey}_ward"]     = null;
            return;
        }

        try
        {
            var json = value is string s ? s : JsonSerializer.Serialize(value);
            var doc = JsonDocument.Parse(json).RootElement;

            result[$"{fieldKey}_province"] = doc.TryGetProperty("province", out var p) ? p.GetString() : null;
            result[$"{fieldKey}_district"] = doc.TryGetProperty("district", out var d) ? d.GetString() : null;
            result[$"{fieldKey}_ward"]     = doc.TryGetProperty("ward",     out var w) ? w.GetString() : null;
        }
        catch
        {
            result[$"{fieldKey}_province"] = null;
            result[$"{fieldKey}_district"] = null;
            result[$"{fieldKey}_ward"]     = null;
        }
    }

    private static void FlattenDateRange(
        string fieldKey, object? value, Dictionary<string, object?> result)
    {
        if (value is null)
        {
            result[$"{fieldKey}_from"] = null;
            result[$"{fieldKey}_to"]   = null;
            return;
        }

        try
        {
            var json = value is string s ? s : JsonSerializer.Serialize(value);
            var doc = JsonDocument.Parse(json).RootElement;

            result[$"{fieldKey}_from"] = doc.TryGetProperty("from", out var f) ? f.GetString() : null;
            result[$"{fieldKey}_to"]   = doc.TryGetProperty("to",   out var t) ? t.GetString() : null;
        }
        catch
        {
            result[$"{fieldKey}_from"] = null;
            result[$"{fieldKey}_to"]   = null;
        }
    }
}
