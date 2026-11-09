namespace AqtCoreFW.Forms.Application.Interfaces;

/// <summary>Evaluates NCalc-based formula expressions against form field values.</summary>
public interface IFormulaEvaluationService
{
    /// <summary>
    /// Evaluates NCalc expression from formulaJson against the provided field values.
    /// Returns the computed result or null on error/timeout (graceful degradation).
    /// Evaluation runs inside a process-wide bounded pool so a malicious CPU-bound
    /// expression cannot starve the request threadpool; the caller times out to null
    /// while the abandoned eval is capped, never aborted.
    /// </summary>
    Task<object?> EvaluateAsync(
        string formulaJson,
        IDictionary<string, object?> fieldValues,
        CancellationToken cancellationToken = default);
}
