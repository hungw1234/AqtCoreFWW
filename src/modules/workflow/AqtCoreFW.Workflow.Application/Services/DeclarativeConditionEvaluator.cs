using System.Text.Json;
using AqtCoreFW.Workflow.Domain.ValueObjects;
using FluentResults;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Security-first declarative condition evaluator.
/// Whitelist operators only — no dynamic code execution (GOV context).
/// Supports AND logic: all conditions must pass for the result to be Ok.
/// Max 20 conditions enforced to prevent abuse.
/// </summary>
public sealed class DeclarativeConditionEvaluator : IConditionEvaluator
{
    private const int MaxConditions = 20;

    private static readonly HashSet<string> AllowedOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "equals", "notEquals",
        "greaterThan", "lessThan", "greaterThanOrEqual", "lessThanOrEqual",
        "in", "notIn",
        "contains",
        "isNull", "isNotNull"
    };

    /// <inheritdoc />
    public Result Evaluate(string? conditionsJson, IReadOnlyDictionary<string, object?> metadata)
    {
        // Null/empty → backward compatible: no conditions = always pass
        if (string.IsNullOrWhiteSpace(conditionsJson))
            return Result.Ok();

        List<WorkflowCondition> conditions;
        try
        {
            conditions = JsonSerializer.Deserialize<List<WorkflowCondition>>(conditionsJson)
                         ?? new List<WorkflowCondition>();
        }
        catch (JsonException ex)
        {
            return Result.Fail($"ConditionsJson is invalid JSON: {ex.Message}");
        }

        if (conditions.Count == 0)
            return Result.Ok();

        if (conditions.Count > MaxConditions)
            return Result.Fail($"Too many conditions: {conditions.Count} (max {MaxConditions}).");

        // Evaluate each condition — collect all failures (fail-all, not fail-fast)
        var failures = new List<string>();

        foreach (var condition in conditions)
        {
            if (!AllowedOperators.Contains(condition.Operator))
            {
                failures.Add($"Unknown operator '{condition.Operator}' on field '{condition.Field}'.");
                continue;
            }

            var fieldExists = metadata.TryGetValue(condition.Field, out var fieldValue);

            var conditionResult = EvaluateCondition(condition, fieldValue, fieldExists);
            if (!conditionResult.IsSuccess)
                failures.Add(conditionResult.Message);
        }

        return failures.Count == 0
            ? Result.Ok()
            : Result.Fail(string.Join("; ", failures));
    }

    // ---------------------------------------------------------------------------
    // Private evaluation — each returns (IsSuccess, Message)
    // ---------------------------------------------------------------------------

    private static (bool IsSuccess, string Message) EvaluateCondition(
        WorkflowCondition condition, object? fieldValue, bool fieldExists)
    {
        var op = condition.Operator.ToLowerInvariant();
        var conditionValue = UnwrapJsonElement(condition.Value);

        return op switch
        {
            "isnull"    => EvaluateIsNull(condition.Field, fieldValue, fieldExists),
            "isnotnull" => EvaluateIsNotNull(condition.Field, fieldValue, fieldExists),
            "in"        => EvaluateIn(condition.Field, fieldValue, condition.Value, negate: false),
            "notin"     => EvaluateIn(condition.Field, fieldValue, condition.Value, negate: true),
            _           => EvaluateComparison(condition.Field, op, fieldValue, conditionValue, fieldExists)
        };
    }

    private static (bool IsSuccess, string Message) EvaluateIsNull(
        string field, object? fieldValue, bool fieldExists)
    {
        var isNull = !fieldExists || fieldValue is null;
        return isNull
            ? (true, string.Empty)
            : (false, $"Field '{field}' is expected to be null but has value '{fieldValue}'.");
    }

    private static (bool IsSuccess, string Message) EvaluateIsNotNull(
        string field, object? fieldValue, bool fieldExists)
    {
        var isNotNull = fieldExists && fieldValue is not null;
        return isNotNull
            ? (true, string.Empty)
            : (false, $"Field '{field}' is expected to be non-null but is null or missing.");
    }

    private static (bool IsSuccess, string Message) EvaluateIn(
        string field, object? fieldValue, object? rawConditionValue, bool negate)
    {
        // conditionValue must be a JsonElement array
        if (rawConditionValue is not JsonElement jsonArray || jsonArray.ValueKind != JsonValueKind.Array)
            return (false, $"Operator '{(negate ? "notIn" : "in")}' on field '{field}' requires an array value.");

        var fieldStr = fieldValue?.ToString() ?? string.Empty;
        var inArray = false;

        foreach (var element in jsonArray.EnumerateArray())
        {
            var elemStr = UnwrapJsonElement(element)?.ToString() ?? string.Empty;
            if (string.Equals(fieldStr, elemStr, StringComparison.OrdinalIgnoreCase))
            {
                inArray = true;
                break;
            }
        }

        if (!negate && !inArray)
            return (false, $"Field '{field}' value '{fieldStr}' is not in allowed list.");

        if (negate && inArray)
            return (false, $"Field '{field}' value '{fieldStr}' is in excluded list.");

        return (true, string.Empty);
    }

    private static (bool IsSuccess, string Message) EvaluateComparison(
        string field, string op, object? fieldValue, object? conditionValue, bool fieldExists)
    {
        // Missing field for comparison operators → fail closed (security: unknown = denied)
        if (!fieldExists || fieldValue is null)
            return (false, $"Field '{field}' is missing or null; condition '{op}' cannot be evaluated.");

        var fieldStr = fieldValue.ToString() ?? string.Empty;
        var condStr  = conditionValue?.ToString() ?? string.Empty;

        return op switch
        {
            "equals"    => EvaluateEquals(field, fieldStr, condStr, negate: false),
            "notequals" => EvaluateEquals(field, fieldStr, condStr, negate: true),
            "contains"  => EvaluateContains(field, fieldStr, condStr),
            _           => EvaluateNumeric(field, op, fieldStr, condStr)
        };
    }

    private static (bool IsSuccess, string Message) EvaluateEquals(
        string field, string fieldStr, string condStr, bool negate)
    {
        // Try numeric equality first, fall back to case-insensitive string
        bool equal;
        if (decimal.TryParse(fieldStr, out var fieldNum) && decimal.TryParse(condStr, out var condNum))
            equal = fieldNum == condNum;
        else
            equal = string.Equals(fieldStr, condStr, StringComparison.OrdinalIgnoreCase);

        var pass = negate ? !equal : equal;
        return pass
            ? (true, string.Empty)
            : (false, $"Field '{field}': '{fieldStr}' {(negate ? "equals" : "does not equal")} '{condStr}'.");
    }

    private static (bool IsSuccess, string Message) EvaluateContains(
        string field, string fieldStr, string condStr)
    {
        var pass = fieldStr.Contains(condStr, StringComparison.OrdinalIgnoreCase);
        return pass
            ? (true, string.Empty)
            : (false, $"Field '{field}': '{fieldStr}' does not contain '{condStr}'.");
    }

    private static (bool IsSuccess, string Message) EvaluateNumeric(
        string field, string op, string fieldStr, string condStr)
    {
        if (!decimal.TryParse(fieldStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var fieldNum))
            return (false, $"Field '{field}' value '{fieldStr}' is not numeric.");

        if (!decimal.TryParse(condStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var condNum))
            return (false, $"Condition value '{condStr}' for field '{field}' is not numeric.");

        var pass = op switch
        {
            "greaterthan"        => fieldNum > condNum,
            "lessthan"           => fieldNum < condNum,
            "greaterthanorequal" => fieldNum >= condNum,
            "lessthanorequal"    => fieldNum <= condNum,
            _                    => false
        };

        return pass
            ? (true, string.Empty)
            : (false, $"Field '{field}': {fieldNum} failed '{op}' check against {condNum}.");
    }

    /// <summary>
    /// Unwraps a JsonElement to a CLR primitive (string, decimal, bool, null).
    /// Non-JsonElement values are returned as-is.
    /// </summary>
    private static object? UnwrapJsonElement(object? value)
    {
        if (value is not JsonElement je)
            return value;

        return je.ValueKind switch
        {
            JsonValueKind.String  => je.GetString(),
            JsonValueKind.Number  => je.TryGetDecimal(out var d) ? d : (object?)je.GetDouble(),
            JsonValueKind.True    => true,
            JsonValueKind.False   => false,
            JsonValueKind.Null    => null,
            _                     => value  // arrays/objects — caller handles
        };
    }
}
