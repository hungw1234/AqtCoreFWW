using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowDefinition;
using AqtCoreFW.Workflow.Application.Validation;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace AqtCoreFW.Workflow.Application.Tests.Commands.CreateWorkflowDefinition;

/// <summary>
/// TDD-first validator tests for CreateWorkflowDefinitionCommand.
/// Covers 6 must-have branches per Phase 02 requirements:
/// 1. Happy path: valid EntityType + matching state names → valid
/// 2. Unknown state name → invalid with allowed list
/// 3. Unknown transition FromStateName → invalid
/// 4. Unknown transition ToStateName → invalid
/// 5. Case-mismatch (e.g., "draft" vs "Draft") → invalid
/// 6. Unknown EntityType → invalid with known types list
/// </summary>
public sealed class CreateWorkflowDefinitionCommandValidatorTests
{
    // Local fake registry — avoids Infrastructure reference (layering hygiene).
    private sealed class FakeEntityStatusEnumRegistry : IEntityStatusEnumRegistry
    {
        private readonly Dictionary<string, IReadOnlyCollection<string>> _map;
        public FakeEntityStatusEnumRegistry(Dictionary<string, IReadOnlyCollection<string>> map)
            => _map = map;

        public bool IsKnownEntityType(string entityType) =>
            !string.IsNullOrWhiteSpace(entityType) && _map.ContainsKey(entityType);

        public IReadOnlyCollection<string> GetAllowedStateNames(string entityType) =>
            !string.IsNullOrWhiteSpace(entityType) && _map.TryGetValue(entityType, out var v)
                ? v
                : Array.Empty<string>();

        public IReadOnlyCollection<string> GetKnownEntityTypes() => _map.Keys.ToArray();
    }

    private readonly CreateWorkflowDefinitionCommandValidator _validator;

    public CreateWorkflowDefinitionCommandValidatorTests()
    {
        var registry = new FakeEntityStatusEnumRegistry(new(StringComparer.Ordinal)
        {
            ["Case"] = new[] { "Draft", "Submitted", "UnderReview", "ReturnedForRevision", "Approved", "Rejected", "Closed" }
        });
        _validator = new CreateWorkflowDefinitionCommandValidator(registry);
    }

    private static CreateStateRequest ValidState(string name) => new(
        Name: name,
        DisplayNameVi: "Tiếng Việt",
        DisplayNameEn: "English",
        IsInitial: false,
        IsFinal: false,
        Color: "#FFFFFF",
        SortOrder: 1);

    private static CreateTransitionRequest ValidTransition(string from, string to) => new(
        FromStateName: from,
        ToStateName: to,
        ActionName: "TestAction",
        ActionLabelVi: "Hành động",
        ActionLabelEn: "Action",
        ConditionsJson: null,
        SortOrder: 1);

    // -----------------------------------------------------------------
    // Test 1: Happy path — EntityType="Case", all state names match CaseStatus enum → valid
    // -----------------------------------------------------------------
    [Fact]
    public void Validate_HappyPath_ValidEntityTypeAndMatchingStateNames_IsValid()
    {
        var command = new CreateWorkflowDefinitionCommand(
            Name: "Test Workflow",
            Description: "Test description",
            TenantId: Guid.NewGuid(),
            CreatedBy: Guid.NewGuid(),
            States: [ValidState("Draft"), ValidState("Submitted"), ValidState("Approved")],
            Transitions: [ValidTransition("Draft", "Submitted"), ValidTransition("Submitted", "Approved")],
            EntityType: "Case");

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    // -----------------------------------------------------------------
    // Test 2: Unknown state name → invalid, error payload contains allowed list
    // -----------------------------------------------------------------
    [Fact]
    public void Validate_UnknownStateName_ReturnsErrorWithAllowedList()
    {
        var command = new CreateWorkflowDefinitionCommand(
            Name: "Test Workflow",
            Description: "Test description",
            TenantId: Guid.NewGuid(),
            CreatedBy: Guid.NewGuid(),
            States: [ValidState("Draft"), ValidState("Foo")], // "Foo" is invalid
            Transitions: [],
            EntityType: "Case");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("States[1].Name");
        var error = result.Errors.First(e => e.PropertyName == "States[1].Name");
        error.ErrorMessage.Should().Contain("Foo");
        error.ErrorMessage.Should().Contain("Draft"); // allowed list should be in message
    }

    // -----------------------------------------------------------------
    // Test 3: Unknown transition FromStateName → invalid
    // -----------------------------------------------------------------
    [Fact]
    public void Validate_UnknownTransitionFromStateName_ReturnsError()
    {
        var command = new CreateWorkflowDefinitionCommand(
            Name: "Test Workflow",
            Description: "Test description",
            TenantId: Guid.NewGuid(),
            CreatedBy: Guid.NewGuid(),
            States: [ValidState("Draft"), ValidState("Submitted")],
            Transitions: [ValidTransition("bar", "Submitted")], // "bar" is invalid
            EntityType: "Case");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Transitions[0].FromStateName");
        var error = result.Errors.First(e => e.PropertyName == "Transitions[0].FromStateName");
        error.ErrorMessage.Should().Contain("bar");
    }

    // -----------------------------------------------------------------
    // Test 4: Unknown transition ToStateName → invalid
    // -----------------------------------------------------------------
    [Fact]
    public void Validate_UnknownTransitionToStateName_ReturnsError()
    {
        var command = new CreateWorkflowDefinitionCommand(
            Name: "Test Workflow",
            Description: "Test description",
            TenantId: Guid.NewGuid(),
            CreatedBy: Guid.NewGuid(),
            States: [ValidState("Draft"), ValidState("Submitted")],
            Transitions: [ValidTransition("Draft", "baz")], // "baz" is invalid
            EntityType: "Case");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Transitions[0].ToStateName");
        var error = result.Errors.First(e => e.PropertyName == "Transitions[0].ToStateName");
        error.ErrorMessage.Should().Contain("baz");
    }

    // -----------------------------------------------------------------
    // Test 5: Case-mismatch — "draft" (lowercase) when enum has "Draft" → invalid (case-sensitive)
    // -----------------------------------------------------------------
    [Fact]
    public void Validate_CaseMismatch_LowercaseStateName_ReturnsError()
    {
        var command = new CreateWorkflowDefinitionCommand(
            Name: "Test Workflow",
            Description: "Test description",
            TenantId: Guid.NewGuid(),
            CreatedBy: Guid.NewGuid(),
            States: [ValidState("draft")], // lowercase "draft" should fail
            Transitions: [],
            EntityType: "Case");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("States[0].Name");
        var error = result.Errors.First(e => e.PropertyName == "States[0].Name");
        error.ErrorMessage.Should().Contain("draft");
        error.ErrorMessage.Should().Contain("Draft"); // should show correct casing
    }

    // -----------------------------------------------------------------
    // Test 6: Unknown EntityType → invalid, error lists known entity types
    // -----------------------------------------------------------------
    [Fact]
    public void Validate_UnknownEntityType_ReturnsErrorWithKnownTypes()
    {
        var command = new CreateWorkflowDefinitionCommand(
            Name: "Test Workflow",
            Description: "Test description",
            TenantId: Guid.NewGuid(),
            CreatedBy: Guid.NewGuid(),
            States: [ValidState("Draft")],
            Transitions: [],
            EntityType: "FooBar"); // unknown entity type

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.EntityType);
        var error = result.Errors.First(e => e.PropertyName == "EntityType");
        error.ErrorMessage.Should().Contain("FooBar");
        error.ErrorMessage.Should().Contain("Case"); // known types list
    }

    // -----------------------------------------------------------------
    // Additional edge cases
    // -----------------------------------------------------------------
    [Fact]
    public void Validate_EmptyEntityType_ReturnsError()
    {
        var command = new CreateWorkflowDefinitionCommand(
            Name: "Test Workflow",
            Description: "Test description",
            TenantId: Guid.NewGuid(),
            CreatedBy: Guid.NewGuid(),
            States: [],
            Transitions: [],
            EntityType: "");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.EntityType);
    }

    [Fact]
    public void Validate_MultipleInvalidStates_ReturnsAllErrors()
    {
        var command = new CreateWorkflowDefinitionCommand(
            Name: "Test Workflow",
            Description: "Test description",
            TenantId: Guid.NewGuid(),
            CreatedBy: Guid.NewGuid(),
            States: [ValidState("Bad1"), ValidState("Bad2"), ValidState("Draft")],
            Transitions: [],
            EntityType: "Case");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("States[0].Name");
        result.ShouldHaveValidationErrorFor("States[1].Name");
        result.Errors.Should().HaveCount(2); // Only 2 errors (Bad1, Bad2), not Draft
    }
}
