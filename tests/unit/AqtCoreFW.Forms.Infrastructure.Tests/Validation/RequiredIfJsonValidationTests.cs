using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Services;

namespace AqtCoreFW.Forms.Infrastructure.Tests.Validation;

/// <summary>
/// Unit tests for FormValidationService.RequiredIfJson conditional validation.
/// FormValidationService is a pure stateless service — instantiated directly with no mocks.
/// Operators: eq, neq, gt, lt, gte, lte, contains, in (all AND logic).
/// </summary>
public sealed class RequiredIfJsonValidationTests
{
    private static readonly long TemplateId = 1L;

    // Shared SUT — no dependencies, pure logic
    private readonly FormValidationService _sut = new();

    // ─── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a Text field with optional RequiredIfJson and Required flag.</summary>
    private static FormField MakeField(
        string key,
        bool required = false,
        string? requiredIfJson = null) =>
        FormField.Create(
            TemplateId, key,
            FormFieldType.Text,
            "Label VI", "Label EN",
            displayOrder: 1,
            required: required,
            addedInVersion: 1,
            requiredIfJson: requiredIfJson);

    private static Dictionary<string, object?> Data(params (string key, object? val)[] pairs) =>
        pairs.ToDictionary(p => p.key, p => p.val);

    // ─── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequiredIfJson_ConditionMet_EmptyValue_ReturnsError()
    {
        // Condition: type == "urgent" — met; field value is empty → error expected
        var field = MakeField("priority",
            requiredIfJson: """[{"fieldKey":"type","operator":"eq","value":"urgent"}]""");
        var data = Data(("type", "urgent"), ("priority", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => FieldOf(e) == "priority");
    }

    [Fact]
    public async Task RequiredIfJson_ConditionNotMet_EmptyValue_NoError()
    {
        // Condition: type == "urgent" — not met (type = "normal"); field empty → no error
        var field = MakeField("priority",
            requiredIfJson: """[{"fieldKey":"type","operator":"eq","value":"urgent"}]""");
        var data = Data(("type", "normal"), ("priority", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task RequiredIfJson_ConditionMet_ValueProvided_NoError()
    {
        // Condition met but field has a value → no required-field error
        var field = MakeField("priority",
            requiredIfJson: """[{"fieldKey":"type","operator":"eq","value":"urgent"}]""");
        var data = Data(("type", "urgent"), ("priority", "high"));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task RequiredIfJson_MultipleConditions_AllMet_ReturnsError()
    {
        // AND logic: both conditions must be true; field empty → error
        var field = MakeField("notes",
            requiredIfJson: """[{"fieldKey":"type","operator":"eq","value":"urgent"},{"fieldKey":"level","operator":"eq","value":"high"}]""");
        var data = Data(("type", "urgent"), ("level", "high"), ("notes", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => FieldOf(e) == "notes");
    }

    [Fact]
    public async Task RequiredIfJson_MultipleConditions_OneFails_NoError()
    {
        // AND logic: second condition fails (level = "low") → field not required
        var field = MakeField("notes",
            requiredIfJson: """[{"fieldKey":"type","operator":"eq","value":"urgent"},{"fieldKey":"level","operator":"eq","value":"high"}]""");
        var data = Data(("type", "urgent"), ("level", "low"), ("notes", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task RequiredIfJson_NullJson_FallsBackToRequired_ReturnsError()
    {
        // No RequiredIfJson — Required=true is the sole gate
        var field = MakeField("name", required: true, requiredIfJson: null);
        var data = Data(("name", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => FieldOf(e) == "name");
    }

    [Fact]
    public async Task RequiredIfJson_NullJson_NotRequired_NoError()
    {
        // No RequiredIfJson, Required=false, empty → no error
        var field = MakeField("notes", required: false, requiredIfJson: null);
        var data = Data(("notes", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task RequiredIfJson_NeqOperator_Works()
    {
        // neq: status != "closed" → field required; status = "open" satisfies neq
        var field = MakeField("reason",
            requiredIfJson: """[{"fieldKey":"status","operator":"neq","value":"closed"}]""");
        var data = Data(("status", "open"), ("reason", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => FieldOf(e) == "reason");
    }

    [Fact]
    public async Task RequiredIfJson_GtOperator_Works()
    {
        // gt: amount > 100 → field required; amount = 200 satisfies gt
        var field = MakeField("approval",
            requiredIfJson: """[{"fieldKey":"amount","operator":"gt","value":"100"}]""");
        var data = Data(("amount", "200"), ("approval", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => FieldOf(e) == "approval");
    }

    [Fact]
    public async Task RequiredIfJson_GtOperator_NotMet_NoError()
    {
        // gt: amount > 100 — not met (amount = 50) → field not required
        var field = MakeField("approval",
            requiredIfJson: """[{"fieldKey":"amount","operator":"gt","value":"100"}]""");
        var data = Data(("amount", "50"), ("approval", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task RequiredIfJson_InOperator_Works()
    {
        // in: category in "vip,priority,express" → field required; category = "vip" satisfies in
        var field = MakeField("handler",
            requiredIfJson: """[{"fieldKey":"category","operator":"in","value":"vip,priority,express"}]""");
        var data = Data(("category", "vip"), ("handler", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => FieldOf(e) == "handler");
    }

    [Fact]
    public async Task RequiredIfJson_InOperator_NotInList_NoError()
    {
        // in: category not in list → field not required
        var field = MakeField("handler",
            requiredIfJson: """[{"fieldKey":"category","operator":"in","value":"vip,priority,express"}]""");
        var data = Data(("category", "standard"), ("handler", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    // ─── lt operator ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequiredIfJson_LtOperator_Works()
    {
        // lt: priority < 3 → field required; priority = 2 satisfies lt
        var field = MakeField("details",
            requiredIfJson: """[{"fieldKey":"priority","operator":"lt","value":"3"}]""");
        var data = Data(("priority", "2"), ("details", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => FieldOf(e) == "details");
    }

    [Fact]
    public async Task RequiredIfJson_LtOperator_NotMet_NoError()
    {
        // lt: priority < 3 — not met (priority = 3, boundary) → field not required
        var field = MakeField("details",
            requiredIfJson: """[{"fieldKey":"priority","operator":"lt","value":"3"}]""");
        var data = Data(("priority", "3"), ("details", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    // ─── contains operator ───────────────────────────────────────────────────────

    [Fact]
    public async Task RequiredIfJson_ContainsOperator_Works()
    {
        // contains: description contains "urgent" → field required
        var field = MakeField("escalation",
            requiredIfJson: """[{"fieldKey":"description","operator":"contains","value":"urgent"}]""");
        var data = Data(("description", "This is urgent request"), ("escalation", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => FieldOf(e) == "escalation");
    }

    [Fact]
    public async Task RequiredIfJson_ContainsOperator_NotMet_NoError()
    {
        // contains: description does not contain "urgent" → field not required
        var field = MakeField("escalation",
            requiredIfJson: """[{"fieldKey":"description","operator":"contains","value":"urgent"}]""");
        var data = Data(("description", "Normal request"), ("escalation", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    // ─── gte operator ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequiredIfJson_GteOperator_Works()
    {
        // gte: amount >= 100 → field required; amount = 100 (boundary) satisfies gte
        var field = MakeField("approval",
            requiredIfJson: """[{"fieldKey":"amount","operator":"gte","value":"100"}]""");
        var data = Data(("amount", "100"), ("approval", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => FieldOf(e) == "approval");
    }

    [Fact]
    public async Task RequiredIfJson_GteOperator_NotMet_NoError()
    {
        // gte: amount >= 100 — not met (amount = 99) → field not required
        var field = MakeField("approval",
            requiredIfJson: """[{"fieldKey":"amount","operator":"gte","value":"100"}]""");
        var data = Data(("amount", "99"), ("approval", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    // ─── lte operator ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequiredIfJson_LteOperator_Works()
    {
        // lte: score <= 50 → field required; score = 50 (boundary) satisfies lte
        var field = MakeField("justification",
            requiredIfJson: """[{"fieldKey":"score","operator":"lte","value":"50"}]""");
        var data = Data(("score", "50"), ("justification", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => FieldOf(e) == "justification");
    }

    [Fact]
    public async Task RequiredIfJson_LteOperator_NotMet_NoError()
    {
        // lte: score <= 50 — not met (score = 51) → field not required
        var field = MakeField("justification",
            requiredIfJson: """[{"fieldKey":"score","operator":"lte","value":"50"}]""");
        var data = Data(("score", "51"), ("justification", ""));

        var result = await _sut.ValidateAsync([field], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    // ─── Assertion helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the field key stored as metadata "Property" on a ValidationError
    /// (set via error.WithMetadata("Property", fieldKey) in the ValidationError constructor).
    /// </summary>
    private static string? FieldOf(AqtCoreFW.SharedKernel.Errors.ValidationError error) =>
        error.Metadata.TryGetValue("Property", out var v) ? v?.ToString() : null;
}
