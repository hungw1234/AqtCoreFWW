using AqtCoreFW.Workflow.Application.Commands.SaveDefinitionGraph;
using AqtCoreFW.Workflow.Application.Validation;
using FluentAssertions;
using FluentValidation.TestHelper;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands.SaveDefinitionGraph;

/// <summary>
/// Unit tests for SaveDefinitionGraphCommandValidator.
/// Validates state + transition names against entity's allowed states (case-sensitive).
/// The EntityType is resolved from the existing WorkflowDefinition row.
/// Uses a local fake registry to avoid Infrastructure reference (layering).
/// </summary>
public sealed class SaveDefinitionGraphCommandValidatorTests
{
    // Local test-double — avoids coupling Application tests to Infrastructure concrete.
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

    private readonly IEntityStatusEnumRegistry _registry;
    private readonly IWorkflowDefinitionEntityTypeLookup _lookup;
    private readonly SaveDefinitionGraphCommandValidator _validator;

    public SaveDefinitionGraphCommandValidatorTests()
    {
        _registry = new FakeEntityStatusEnumRegistry(new(StringComparer.Ordinal)
        {
            ["Case"] = new[] { "Draft", "Submitted", "UnderReview", "ReturnedForRevision", "Approved", "Rejected", "Closed" }
        });
        _lookup = Substitute.For<IWorkflowDefinitionEntityTypeLookup>();
        _validator = new SaveDefinitionGraphCommandValidator(_registry, _lookup);
    }

    private static GraphStateInput ValidState(string name) => new(
        Name: name,
        DisplayNameVi: "Tiếng Việt",
        DisplayNameEn: "English",
        IsInitial: false,
        IsFinal: false,
        Color: "#FFFFFF",
        SortOrder: 1);

    private static GraphTransitionInput ValidTransition(string from, string to) => new(
        FromStateName: from,
        ToStateName: to,
        ActionName: "TestAction",
        ActionLabelVi: "Hành động",
        ActionLabelEn: "Action",
        ConditionsJson: null,
        SortOrder: 1);

    // -----------------------------------------------------------------
    // Happy path — valid states and transitions for Case entity type
    // -----------------------------------------------------------------
    [Fact]
    public async Task ValidateAsync_HappyPath_ValidStatesAndTransitions_IsValid()
    {
        var definitionId = Guid.NewGuid();
        _lookup.GetEntityTypeAsync(definitionId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns("Case");

        var command = new SaveDefinitionGraphCommand(
            DefinitionId: definitionId,
            TenantId: Guid.NewGuid(),
            ModifiedBy: Guid.NewGuid(),
            States: [ValidState("Draft"), ValidState("Submitted"), ValidState("Approved")],
            Transitions: [ValidTransition("Draft", "Submitted"), ValidTransition("Submitted", "Approved")]);

        var result = await _validator.TestValidateAsync(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    // -----------------------------------------------------------------
    // Unknown state name → invalid
    // -----------------------------------------------------------------
    [Fact]
    public async Task ValidateAsync_UnknownStateName_ReturnsError()
    {
        var definitionId = Guid.NewGuid();
        _lookup.GetEntityTypeAsync(definitionId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns("Case");

        var command = new SaveDefinitionGraphCommand(
            DefinitionId: definitionId,
            TenantId: Guid.NewGuid(),
            ModifiedBy: Guid.NewGuid(),
            States: [ValidState("Draft"), ValidState("Bogus")], // "Bogus" is invalid
            Transitions: []);

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor("States[1].Name");
        var error = result.Errors.First(e => e.PropertyName == "States[1].Name");
        error.ErrorMessage.Should().Contain("Bogus");
    }

    // -----------------------------------------------------------------
    // Unknown transition FromStateName → invalid
    // -----------------------------------------------------------------
    [Fact]
    public async Task ValidateAsync_UnknownTransitionFromStateName_ReturnsError()
    {
        var definitionId = Guid.NewGuid();
        _lookup.GetEntityTypeAsync(definitionId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns("Case");

        var command = new SaveDefinitionGraphCommand(
            DefinitionId: definitionId,
            TenantId: Guid.NewGuid(),
            ModifiedBy: Guid.NewGuid(),
            States: [ValidState("Draft"), ValidState("Submitted")],
            Transitions: [ValidTransition("InvalidFrom", "Submitted")]);

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor("Transitions[0].FromStateName");
    }

    // -----------------------------------------------------------------
    // Unknown transition ToStateName → invalid
    // -----------------------------------------------------------------
    [Fact]
    public async Task ValidateAsync_UnknownTransitionToStateName_ReturnsError()
    {
        var definitionId = Guid.NewGuid();
        _lookup.GetEntityTypeAsync(definitionId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns("Case");

        var command = new SaveDefinitionGraphCommand(
            DefinitionId: definitionId,
            TenantId: Guid.NewGuid(),
            ModifiedBy: Guid.NewGuid(),
            States: [ValidState("Draft"), ValidState("Submitted")],
            Transitions: [ValidTransition("Draft", "InvalidTo")]);

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor("Transitions[0].ToStateName");
    }

    // -----------------------------------------------------------------
    // Case-mismatch — lowercase when enum expects PascalCase
    // -----------------------------------------------------------------
    [Fact]
    public async Task ValidateAsync_CaseMismatch_ReturnsError()
    {
        var definitionId = Guid.NewGuid();
        _lookup.GetEntityTypeAsync(definitionId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns("Case");

        var command = new SaveDefinitionGraphCommand(
            DefinitionId: definitionId,
            TenantId: Guid.NewGuid(),
            ModifiedBy: Guid.NewGuid(),
            States: [ValidState("draft")], // lowercase should fail
            Transitions: []);

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor("States[0].Name");
        var error = result.Errors.First(e => e.PropertyName == "States[0].Name");
        error.ErrorMessage.Should().Contain("draft");
        error.ErrorMessage.Should().Contain("Draft"); // expected casing in allowed list
    }

    // -----------------------------------------------------------------
    // Definition not found — lookup returns null
    // -----------------------------------------------------------------
    [Fact]
    public async Task ValidateAsync_DefinitionNotFound_ReturnsError()
    {
        var definitionId = Guid.NewGuid();
        _lookup.GetEntityTypeAsync(definitionId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var command = new SaveDefinitionGraphCommand(
            DefinitionId: definitionId,
            TenantId: Guid.NewGuid(),
            ModifiedBy: Guid.NewGuid(),
            States: [ValidState("Draft")],
            Transitions: []);

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.DefinitionId);
        var error = result.Errors.First(e => e.PropertyName == "DefinitionId");
        error.ErrorMessage.Should().Contain("not found");
    }

    // -----------------------------------------------------------------
    // Basic field validations still work
    // -----------------------------------------------------------------
    [Fact]
    public async Task ValidateAsync_EmptyDefinitionId_ReturnsError()
    {
        var command = new SaveDefinitionGraphCommand(
            DefinitionId: Guid.Empty,
            TenantId: Guid.NewGuid(),
            ModifiedBy: Guid.NewGuid(),
            States: [],
            Transitions: []);

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.DefinitionId);
    }
}
