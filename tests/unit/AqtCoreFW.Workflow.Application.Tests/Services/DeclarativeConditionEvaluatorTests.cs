using System.Text.Json;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.ValueObjects;

namespace AqtCoreFW.Workflow.Application.Tests.Services;

/// <summary>
/// Unit tests for DeclarativeConditionEvaluator.
/// Covers all 11 whitelisted operators, backward-compatibility, guard rails, and edge cases.
/// </summary>
public sealed class DeclarativeConditionEvaluatorTests
{
    private readonly DeclarativeConditionEvaluator _evaluator = new();

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>Serializes a list of WorkflowCondition to JSON string for Evaluate input.</summary>
    private static string ToJson(params WorkflowCondition[] conditions) =>
        JsonSerializer.Serialize(conditions);

    /// <summary>Builds a single-condition JSON with a raw object value (e.g. array).</summary>
    private static string ToJsonRaw(string field, string op, object? value)
    {
        var list = new[] { new { field, @operator = op, value } };
        return JsonSerializer.Serialize(list);
    }

    private static IReadOnlyDictionary<string, object?> Meta(string key, object? value) =>
        new Dictionary<string, object?> { [key] = value };

    private static IReadOnlyDictionary<string, object?> EmptyMeta() =>
        new Dictionary<string, object?>();

    // ---------------------------------------------------------------------------
    // Backward compatibility — null / empty JSON
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_NullConditionsJson_ReturnsOk()
    {
        var result = _evaluator.Evaluate(null, EmptyMeta());
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_EmptyConditionsJson_ReturnsOk()
    {
        var result = _evaluator.Evaluate("   ", EmptyMeta());
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_EmptyArrayJson_ReturnsOk()
    {
        var result = _evaluator.Evaluate("[]", EmptyMeta());
        result.IsSuccess.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // equals
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_Equals_StringMatch_Passes()
    {
        var json = ToJson(new WorkflowCondition("status", "equals", "approved"));
        var result = _evaluator.Evaluate(json, Meta("status", "approved"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Equals_CaseInsensitive_Passes()
    {
        var json = ToJson(new WorkflowCondition("status", "equals", "APPROVED"));
        var result = _evaluator.Evaluate(json, Meta("status", "approved"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Equals_NumericMatch_Passes()
    {
        var json = ToJsonRaw("amount", "equals", 100);
        var result = _evaluator.Evaluate(json, Meta("amount", "100"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Equals_DifferentValues_Fails()
    {
        var json = ToJson(new WorkflowCondition("status", "equals", "approved"));
        var result = _evaluator.Evaluate(json, Meta("status", "pending"));
        result.IsFailed.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // notEquals
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_NotEquals_DifferentValues_Passes()
    {
        var json = ToJson(new WorkflowCondition("status", "notEquals", "rejected"));
        var result = _evaluator.Evaluate(json, Meta("status", "approved"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NotEquals_SameValue_Fails()
    {
        var json = ToJson(new WorkflowCondition("status", "notEquals", "approved"));
        var result = _evaluator.Evaluate(json, Meta("status", "approved"));
        result.IsFailed.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // greaterThan
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_GreaterThan_Passes()
    {
        var json = ToJsonRaw("amount", "greaterThan", 50);
        var result = _evaluator.Evaluate(json, Meta("amount", "100"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_GreaterThan_Fails_WhenLess()
    {
        var json = ToJsonRaw("amount", "greaterThan", 200);
        var result = _evaluator.Evaluate(json, Meta("amount", "100"));
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_GreaterThan_Fails_WhenEqual()
    {
        var json = ToJsonRaw("amount", "greaterThan", 100);
        var result = _evaluator.Evaluate(json, Meta("amount", "100"));
        result.IsFailed.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // lessThan
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_LessThan_Passes()
    {
        var json = ToJsonRaw("amount", "lessThan", 200);
        var result = _evaluator.Evaluate(json, Meta("amount", "100"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_LessThan_Fails_WhenGreater()
    {
        var json = ToJsonRaw("amount", "lessThan", 50);
        var result = _evaluator.Evaluate(json, Meta("amount", "100"));
        result.IsFailed.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // greaterThanOrEqual
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_GreaterThanOrEqual_Passes_WhenGreater()
    {
        var json = ToJsonRaw("amount", "greaterThanOrEqual", 50);
        var result = _evaluator.Evaluate(json, Meta("amount", "100"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_GreaterThanOrEqual_Passes_WhenEqual()
    {
        var json = ToJsonRaw("amount", "greaterThanOrEqual", 100);
        var result = _evaluator.Evaluate(json, Meta("amount", "100"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_GreaterThanOrEqual_Fails_WhenLess()
    {
        var json = ToJsonRaw("amount", "greaterThanOrEqual", 200);
        var result = _evaluator.Evaluate(json, Meta("amount", "100"));
        result.IsFailed.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // lessThanOrEqual
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_LessThanOrEqual_Passes_WhenLess()
    {
        var json = ToJsonRaw("amount", "lessThanOrEqual", 200);
        var result = _evaluator.Evaluate(json, Meta("amount", "100"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_LessThanOrEqual_Passes_WhenEqual()
    {
        var json = ToJsonRaw("amount", "lessThanOrEqual", 100);
        var result = _evaluator.Evaluate(json, Meta("amount", "100"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_LessThanOrEqual_Fails_WhenGreater()
    {
        var json = ToJsonRaw("amount", "lessThanOrEqual", 50);
        var result = _evaluator.Evaluate(json, Meta("amount", "100"));
        result.IsFailed.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // in
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_In_ValueInArray_Passes()
    {
        var json = ToJsonRaw("role", "in", new[] { "Admin", "Supervisor" });
        var result = _evaluator.Evaluate(json, Meta("role", "Admin"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_In_ValueInArray_CaseInsensitive_Passes()
    {
        var json = ToJsonRaw("role", "in", new[] { "admin", "supervisor" });
        var result = _evaluator.Evaluate(json, Meta("role", "ADMIN"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_In_ValueNotInArray_Fails()
    {
        var json = ToJsonRaw("role", "in", new[] { "Admin", "Supervisor" });
        var result = _evaluator.Evaluate(json, Meta("role", "Clerk"));
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_In_NonArrayValue_Fails()
    {
        var json = ToJson(new WorkflowCondition("role", "in", "Admin"));
        var result = _evaluator.Evaluate(json, Meta("role", "Admin"));
        result.IsFailed.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // notIn
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_NotIn_Passes()
    {
        var json = ToJsonRaw("role", "notIn", new[] { "Admin", "Supervisor" });
        var result = _evaluator.Evaluate(json, Meta("role", "Clerk"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NotIn_ValueInArray_Fails()
    {
        var json = ToJsonRaw("role", "notIn", new[] { "Admin", "Supervisor" });
        var result = _evaluator.Evaluate(json, Meta("role", "Admin"));
        result.IsFailed.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // contains
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_Contains_SubstringMatch_Passes()
    {
        var json = ToJson(new WorkflowCondition("notes", "contains", "urgent"));
        var result = _evaluator.Evaluate(json, Meta("notes", "This is URGENT case"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Contains_NoMatch_Fails()
    {
        var json = ToJson(new WorkflowCondition("notes", "contains", "urgent"));
        var result = _evaluator.Evaluate(json, Meta("notes", "Normal case"));
        result.IsFailed.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // isNull
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_IsNull_NullField_Passes()
    {
        var json = ToJson(new WorkflowCondition("comment", "isNull", null));
        var result = _evaluator.Evaluate(json, Meta("comment", null));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_IsNull_MissingField_Passes()
    {
        var json = ToJson(new WorkflowCondition("comment", "isNull", null));
        var result = _evaluator.Evaluate(json, EmptyMeta());
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_IsNull_NonNullField_Fails()
    {
        var json = ToJson(new WorkflowCondition("comment", "isNull", null));
        var result = _evaluator.Evaluate(json, Meta("comment", "some comment"));
        result.IsFailed.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // isNotNull
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_IsNotNull_Passes()
    {
        var json = ToJson(new WorkflowCondition("approver", "isNotNull", null));
        var result = _evaluator.Evaluate(json, Meta("approver", "user-123"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_IsNotNull_NullField_Fails()
    {
        var json = ToJson(new WorkflowCondition("approver", "isNotNull", null));
        var result = _evaluator.Evaluate(json, Meta("approver", null));
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_IsNotNull_MissingField_Fails()
    {
        var json = ToJson(new WorkflowCondition("approver", "isNotNull", null));
        var result = _evaluator.Evaluate(json, EmptyMeta());
        result.IsFailed.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // Multiple conditions (AND logic)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_MultipleConditions_AllPass_ReturnsOk()
    {
        var json = ToJson(
            new WorkflowCondition("status", "equals", "approved"),
            new WorkflowCondition("approver", "isNotNull", null));

        var meta = new Dictionary<string, object?>
        {
            ["status"]   = "approved",
            ["approver"] = "user-123"
        };

        var result = _evaluator.Evaluate(json, meta);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_MultipleConditions_OneFails_ReturnsFail()
    {
        var json = ToJson(
            new WorkflowCondition("status", "equals", "approved"),
            new WorkflowCondition("approver", "isNotNull", null));

        var meta = new Dictionary<string, object?>
        {
            ["status"]   = "approved",
            ["approver"] = null          // this one fails
        };

        var result = _evaluator.Evaluate(json, meta);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_MultipleConditions_AllFail_CollectsAllErrors()
    {
        var json = ToJson(
            new WorkflowCondition("status", "equals", "approved"),
            new WorkflowCondition("amount", "greaterThan", "500"));

        var meta = new Dictionary<string, object?>
        {
            ["status"] = "pending",
            ["amount"] = "100"
        };

        var result = _evaluator.Evaluate(json, meta);
        result.IsFailed.Should().BeTrue();
        // Both failures should be aggregated into a single error message
        result.Errors[0].Message.Should().Contain("status");
        result.Errors[0].Message.Should().Contain("amount");
    }

    // ---------------------------------------------------------------------------
    // Guard rails
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_MissingField_FailsClosed()
    {
        // Security: unknown field = denied (fail closed)
        var json = ToJson(new WorkflowCondition("secret_field", "equals", "value"));
        var result = _evaluator.Evaluate(json, EmptyMeta());
        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("secret_field");
    }

    [Fact]
    public void Evaluate_InvalidOperator_ReturnsFail()
    {
        var json = ToJson(new WorkflowCondition("status", "eval()", "anything"));
        var result = _evaluator.Evaluate(json, Meta("status", "value"));
        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("eval()");
    }

    [Fact]
    public void Evaluate_MaxConditionsExceeded_ReturnsFail()
    {
        // 21 conditions — exceeds the max of 20
        var conditions = Enumerable
            .Range(1, 21)
            .Select(i => new WorkflowCondition($"field{i}", "equals", "x"))
            .ToArray();
        var json = ToJson(conditions);

        var result = _evaluator.Evaluate(json, EmptyMeta());
        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("Too many conditions");
    }

    [Fact]
    public void Evaluate_InvalidJson_ReturnsFail()
    {
        var result = _evaluator.Evaluate("{not valid json", EmptyMeta());
        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().Contain("invalid JSON");
    }

    // ---------------------------------------------------------------------------
    // Exact boundary — 20 conditions allowed
    // ---------------------------------------------------------------------------

    [Fact]
    public void Evaluate_ExactlyMaxConditions_IsAllowed()
    {
        // 20 isNull conditions on missing fields → all pass (missing = null = isNull passes)
        var conditions = Enumerable
            .Range(1, 20)
            .Select(i => new WorkflowCondition($"missing{i}", "isNull", null))
            .ToArray();
        var json = ToJson(conditions);

        var result = _evaluator.Evaluate(json, EmptyMeta());
        result.IsSuccess.Should().BeTrue();
    }
}
