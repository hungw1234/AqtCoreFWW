using FluentResults;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Evaluates declarative JSON conditions against a metadata dictionary.
/// Returns Ok if all conditions pass, or Fail with details of which conditions failed.
/// </summary>
public interface IConditionEvaluator
{
    Result Evaluate(string? conditionsJson, IReadOnlyDictionary<string, object?> metadata);
}
