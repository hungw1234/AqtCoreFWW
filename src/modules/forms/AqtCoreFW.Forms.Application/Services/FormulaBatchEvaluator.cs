using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Entities;

namespace AqtCoreFW.Forms.Application.Services;

/// <summary>
/// Shared per-field evaluation loop for batch Formula compute — used by the authed
/// evaluate-formulas query handler and (P2) the public evaluate-formulas handler.
/// Not a new evaluation engine: delegates every expression to the injected
/// <see cref="IFormulaEvaluationService"/> sandbox, exactly like submit-time
/// <c>FormDataPreprocessor</c> does. Null-omit contract mirrored from
/// <c>FormDataPreprocessor.cs</c> (only add a key when the computed value is non-null).
/// </summary>
public static class FormulaBatchEvaluator
{
    /// <summary>
    /// Evaluates every active Formula field with a non-empty FormulaJson against
    /// <paramref name="values"/>. Caller is responsible for pre-filtering to active
    /// Formula fields (kept explicit rather than filtering here, so callers can log/trace
    /// which fields were considered).
    /// </summary>
    public static async Task<Dictionary<string, object?>> EvaluateAllAsync(
        IEnumerable<FormField> activeFormulaFields,
        IDictionary<string, object?> values,
        IFormulaEvaluationService formulaEvaluationService,
        CancellationToken cancellationToken)
    {
        // This endpoint's request body binds Values as Dictionary<string, object?> — real HTTP
        // requests arrive with scalars as System.Text.Json.JsonElement (no object-unboxing
        // converter registered on the host's JsonSerializerOptions; same root cause documented
        // in FormDataPreprocessor.cs's default case). NCalc's arithmetic operators throw on a
        // boxed JsonElement operand (e.g. "JsonElement * Int32"), degrading every formula
        // referencing that field to null. Normalize the input map to native CLR types up front
        // via the canonical JsonValueNormalizer — the SAME normalizer submit-time
        // FormDataPreprocessor uses; formulas only reference scalar fields, so the recursive
        // array/object handling is inert here but keeps a single normalization implementation.
        var normalizedValues = JsonValueNormalizer.NormalizeDict(values);

        var results = new Dictionary<string, object?>();

        foreach (var field in activeFormulaFields)
        {
            if (string.IsNullOrWhiteSpace(field.FormulaJson))
                continue;

            var computed = await formulaEvaluationService
                .EvaluateAsync(field.FormulaJson, normalizedValues, cancellationToken)
                .ConfigureAwait(false);

            if (computed is not null)
                results[field.FieldKey] = computed;
        }

        return results;
    }
}
