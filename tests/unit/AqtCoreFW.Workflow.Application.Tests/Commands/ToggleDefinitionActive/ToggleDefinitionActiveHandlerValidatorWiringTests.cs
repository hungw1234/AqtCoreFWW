using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.Commands.ToggleDefinitionActive;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Validation;
using FluentAssertions;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands.ToggleDefinitionActive;

/// <summary>
/// Wiring tests: ToggleDefinitionActiveCommandHandler calls IWorkflowGraphValidator.ValidateForActivateAsync
/// when IsActive=true and propagates failures correctly.
///
/// Activate flow (Phase 5c F1 canonicalization writeback):
///   1. GetWithStatesAndTransitionsAsync → guard checks (tenant + initial state).
///   2. GetForValidationAsync(Id) → full tracking load (States+Transitions+Assignments+Clauses).
///   3. ValidateForActivateAsync → R3 canonicalization mutates clause.RefsJson in-memory.
///   4. On success: UpdateAsync → SaveChanges persists Activate mutation + canonical rewrites.
///   4. On failure: short-circuits BEFORE UpdateAsync (Option A — persist only on success).
///
/// W-T1: activate with validator failure → returns GraphValidationFailed, UpdateAsync NOT called.
/// W-T2: activate with validator success → GetForValidationAsync called, UpdateAsync called.
/// Deactivate path (IsActive=false) never calls validator (validator not invoked).
///
/// S5 META-L (c): each fact asserts repository call count BEFORE behavior assert.
/// </summary>
public sealed class ToggleDefinitionActiveHandlerValidatorWiringTests
{
    private readonly IWorkflowDefinitionRepository _repo = Substitute.For<IWorkflowDefinitionRepository>();
    private readonly IWorkflowGraphValidator _validator = Substitute.For<IWorkflowGraphValidator>();
    private readonly ToggleDefinitionActiveCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid DefinitionId = Guid.NewGuid();

    public ToggleDefinitionActiveHandlerValidatorWiringTests()
    {
        _handler = new ToggleDefinitionActiveCommandHandler(_repo, _validator);
    }

    private static WorkflowDefinition MakeDefinitionWithInitialState(Guid tenantId, Guid publicId)
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(publicId);
        var def = WorkflowDefinition.Create(gen, "Test", "desc", tenantId, ActorId, "key", "Case");
        // Add an initial state (Id=0 pre-persist) so the NoInitialState guard passes.
        var initialState = WorkflowState.Create(
            def.Id, "Draft", "Nháp", "Draft", isInitial: true, isFinal: false, "#FFF", 1);
        def.AddState(initialState);
        return def;
    }

    // Stubs both loads required by the activate path.
    // GetWithStatesAndTransitionsAsync → guard checks.
    // GetForValidationAsync(Id) → full tracking load for validator.
    private void SetupActivateRepoStubs(WorkflowDefinition definition)
    {
        _repo.GetWithStatesAndTransitionsAsync(DefinitionId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        // GetForValidationAsync returns definition + empty assignment lookup.
        // Tests that focus on validator wiring don't need real assignments — validator is mocked.
        _repo.GetForValidationAsync(definition.Id, Arg.Any<CancellationToken>())
            .Returns((
                definition,
                (IReadOnlyDictionary<long, OutgoingAssignment?>)new Dictionary<long, OutgoingAssignment?>()));

        // Branch-aware activate also loads parallel branches — empty for wiring tests (validator mocked).
        _repo.GetParallelBranchesForValidationAsync(definition.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WorkflowParallelBranch>)[]);
    }

    // ── W-T1: validator returns violations on activate → handler short-circuits ──

    [Fact]
    public async Task Handle_ActivateWithValidatorFailure_ReturnsGraphValidationFailedError()
    {
        // Arrange
        var definition = MakeDefinitionWithInitialState(TenantId, DefinitionId);
        SetupActivateRepoStubs(definition);

        var violation = new GraphRuleViolation("R2", "Draft", "Submit", "Missing assignment.");
        _validator.ValidateForActivateAsync(
                Arg.Any<IReadOnlyList<WorkflowState>>(),
                Arg.Any<IReadOnlyList<WorkflowTransition>>(),
                Arg.Any<IReadOnlyDictionary<long, OutgoingAssignment?>>(),
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<WorkflowParallelBranch>?>(),
                Arg.Any<CancellationToken>())
            .Returns(WorkflowGraphValidationReport.Fail([violation]));

        var command = new ToggleDefinitionActiveCommand(DefinitionId, TenantId, ActorId, IsActive: true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert — S5 META-L (c): precondition — validator WAS called exactly once.
        await _validator.Received(1).ValidateForActivateAsync(
            Arg.Any<IReadOnlyList<WorkflowState>>(),
            Arg.Any<IReadOnlyList<WorkflowTransition>>(),
            Arg.Any<IReadOnlyDictionary<long, OutgoingAssignment?>>(),
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyList<WorkflowParallelBranch>?>(),
            Arg.Any<CancellationToken>());

        // Precondition — UpdateAsync was NOT called (Option A: no persist on failure).
        await _repo.Received(0).UpdateAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<CancellationToken>());

        // Behavior: result must be failed with GraphValidationFailed error code.
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Metadata.ContainsKey("WorkflowErrorCode") &&
            e.Metadata["WorkflowErrorCode"] as string == WorkflowErrorCodes.GraphValidationFailed,
            "handler must return GraphValidationFailed (GOV_WFL_019) on validator failure");
    }

    // ── W-T2: validator passes on activate → GetForValidationAsync + UpdateAsync called ──

    [Fact]
    public async Task Handle_ActivateWithValidatorSuccess_CallsGetForValidationAndUpdateAsync()
    {
        // Arrange
        var definition = MakeDefinitionWithInitialState(TenantId, DefinitionId);
        SetupActivateRepoStubs(definition);

        _validator.ValidateForActivateAsync(
                Arg.Any<IReadOnlyList<WorkflowState>>(),
                Arg.Any<IReadOnlyList<WorkflowTransition>>(),
                Arg.Any<IReadOnlyDictionary<long, OutgoingAssignment?>>(),
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<WorkflowParallelBranch>?>(),
                Arg.Any<CancellationToken>())
            .Returns(WorkflowGraphValidationReport.Ok());

        _repo.UpdateAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var command = new ToggleDefinitionActiveCommand(DefinitionId, TenantId, ActorId, IsActive: true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert — S5 META-L (c): precondition — GetForValidationAsync was called with correct internal Id.
        await _repo.Received(1).GetForValidationAsync(definition.Id, Arg.Any<CancellationToken>());

        // Precondition — validator WAS called exactly once.
        await _validator.Received(1).ValidateForActivateAsync(
            Arg.Any<IReadOnlyList<WorkflowState>>(),
            Arg.Any<IReadOnlyList<WorkflowTransition>>(),
            Arg.Any<IReadOnlyDictionary<long, OutgoingAssignment?>>(),
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyList<WorkflowParallelBranch>?>(),
            Arg.Any<CancellationToken>());

        // Precondition — UpdateAsync WAS called (definition activated + canonical rewrites persist).
        await _repo.Received(1).UpdateAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<CancellationToken>());

        // Behavior: result must succeed.
        result.IsSuccess.Should().BeTrue("validator passed → handler should activate definition");
    }

    // ── Deactivate path never invokes validator or GetForValidationAsync ────────

    [Fact]
    public async Task Handle_Deactivate_DoesNotCallValidatorOrGetForValidation()
    {
        // Arrange
        var definition = MakeDefinitionWithInitialState(TenantId, DefinitionId);
        _repo.GetWithStatesAndTransitionsAsync(DefinitionId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        _repo.UpdateAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var command = new ToggleDefinitionActiveCommand(DefinitionId, TenantId, ActorId, IsActive: false);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — validator NOT called for deactivation.
        await _validator.Received(0).ValidateForActivateAsync(
            Arg.Any<IReadOnlyList<WorkflowState>>(),
            Arg.Any<IReadOnlyList<WorkflowTransition>>(),
            Arg.Any<IReadOnlyDictionary<long, OutgoingAssignment?>>(),
            Arg.Any<Guid>(),
            Arg.Any<IReadOnlyList<WorkflowParallelBranch>?>(),
            Arg.Any<CancellationToken>());

        // Assert — GetForValidationAsync NOT called for deactivation.
        await _repo.Received(0).GetForValidationAsync(
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }
}
