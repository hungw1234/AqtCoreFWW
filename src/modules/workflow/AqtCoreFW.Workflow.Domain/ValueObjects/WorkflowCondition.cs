using System.Text.Json.Serialization;

namespace AqtCoreFW.Workflow.Domain.ValueObjects;

/// <summary>
/// Single declarative condition rule. AND logic — all conditions in a list must pass.
/// Stored as JSON array in WorkflowTransition.ConditionsJson.
/// </summary>
public sealed record WorkflowCondition(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("operator")] string Operator,
    [property: JsonPropertyName("value")] object? Value);
