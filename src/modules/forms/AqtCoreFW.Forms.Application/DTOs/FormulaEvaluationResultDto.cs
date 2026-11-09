namespace AqtCoreFW.Forms.Application.DTOs;

/// <summary>
/// Batch result of evaluating every active Formula field of a template against a
/// caller-supplied value set. Keyed by <c>FormField.FieldKey</c>; a key is present only when
/// the formula evaluated to a non-null result (mirrors submit-time semantics in
/// <c>FormDataPreprocessor</c> — omit-on-null, never emit a null placeholder).
/// </summary>
/// <param name="Results">FieldKey -> computed value map for active Formula fields.</param>
public sealed record FormulaEvaluationResultDto(IReadOnlyDictionary<string, object?> Results);
