using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.Commands.SaveDefinitionGraph;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Errors;
using AqtCoreFW.Workflow.Domain.Validation;
using FluentAssertions;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands.SaveDefinitionGraph;

/// <summary>
/// Wiring tests: SaveDefinitionGraphCommandHandler calls IWorkflowGraphValidator.ValidateForSaveAsync
/// and propagates failures correctly.
///
/// W-S1: validator failure → handler returns GraphValidationFailed, graph NOT saved.
/// W-S2: validator success → handler proceeds to ReplaceGraphAsync.
///
/// S5 META-L (c): each fact asserts repository call count BEFORE behavior assert.
/// </summary>
public sealed class SaveDefinitionGraphHandlerValidatorWiringTests
{
    private readonly IWorkflowDefinitionRepository _repo = Substitute.For<IWorkflowDefinitionRepository>();
    private readonly IWorkflowGraphValidator _validator = Substitute.For<IWorkflowGraphValidator>();
    private readonly SaveDefinitionGraphCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid DefinitionId = Guid.NewGuid();

    public SaveDefinitionGraphHandlerValidatorWiringTests()
    {
        _handler = new SaveDefinitionGraphCommandHandler(_repo, _validator);
    }

    private static SaveDefinitionGraphCommand BuildCommand(params GraphStateInput[] states) =>
        new(DefinitionId, TenantId, ActorId, states.ToList(), []);

    private static WorkflowDefinition MakeDefinition()
    {
        // WorkflowDefinition has a private EF ctor. Use the static Create factory with a stub IGuidGenerator.
        var gen = Substitute.For<AqtCoreFW.SharedKernel.Abstractions.IGuidGenerator>();
        gen.NewIdMonotonic().Returns(DefinitionId);
        return WorkflowDefinition.Create(gen, "Test Def", "desc", TenantId, ActorId, "test-key", "Case");
    }

    // ── W-S1: validator returns violations → handler short-circuits ───────────

    [Fact]
    public async Task Handle_ValidatorReturnsViolations_ReturnsGraphValidationFailedError()
    {
        // Arrange
        var definition = MakeDefinition();
        _repo.GetWithStatesAndTransitionsAsync(DefinitionId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        var violation = new GraphRuleViolation("R1", "Draft", null, "Dead-end state.");
        _validator.ValidateForSaveAsync(
                Arg.Any<IReadOnlyList<WorkflowState>>(),
                Arg.Any<IReadOnlyList<NewTransitionSpec>>(),
                Arg.Any<IReadOnlyList<NewBranchSpec>>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(WorkflowGraphValidationReport.Fail([violation]));

        var command = BuildCommand(
            new GraphStateInput("Draft", "Nháp", "Draft", true, false, "#FFF", 1));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert — S5 META-L (c): precondition — validator WAS called exactly once.
        await _validator.Received(1).ValidateForSaveAsync(
            Arg.Any<IReadOnlyList<WorkflowState>>(),
            Arg.Any<IReadOnlyList<NewTransitionSpec>>(),
            Arg.Any<IReadOnlyList<NewBranchSpec>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());

        // Precondition — ReplaceGraphAsync was NOT called (graph not saved).
        await _repo.Received(0).ReplaceGraphAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<List<WorkflowState>>(),
            Arg.Any<IReadOnlyList<NewTransitionSpec>>(),
            Arg.Any<IReadOnlyList<NewBranchSpec>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());

        // Behavior: result must be failed with GraphValidationFailed error code.
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e =>
            e.Metadata.ContainsKey("WorkflowErrorCode") &&
            e.Metadata["WorkflowErrorCode"] as string == WorkflowErrorCodes.GraphValidationFailed,
            "handler must return GraphValidationFailed (GOV_WFL_019) on validator failure");
    }

    // ── W-S2: validator passes → ReplaceGraphAsync is called ─────────────────

    [Fact]
    public async Task Handle_ValidatorPasses_CallsReplaceGraphAsync()
    {
        // Arrange
        var definition = MakeDefinition();
        _repo.GetWithStatesAndTransitionsAsync(DefinitionId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        _validator.ValidateForSaveAsync(
                Arg.Any<IReadOnlyList<WorkflowState>>(),
                Arg.Any<IReadOnlyList<NewTransitionSpec>>(),
                Arg.Any<IReadOnlyList<NewBranchSpec>>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(WorkflowGraphValidationReport.Ok());

        _repo.ReplaceGraphAsync(
                Arg.Any<WorkflowDefinition>(),
                Arg.Any<List<WorkflowState>>(),
                Arg.Any<IReadOnlyList<NewTransitionSpec>>(),
                Arg.Any<IReadOnlyList<NewBranchSpec>>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var command = BuildCommand(
            new GraphStateInput("Draft", "Nháp", "Draft", true, false, "#FFF", 1));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert — S5 META-L (c): precondition — validator WAS called exactly once.
        await _validator.Received(1).ValidateForSaveAsync(
            Arg.Any<IReadOnlyList<WorkflowState>>(),
            Arg.Any<IReadOnlyList<NewTransitionSpec>>(),
            Arg.Any<IReadOnlyList<NewBranchSpec>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());

        // Precondition — ReplaceGraphAsync WAS called (graph was saved).
        await _repo.Received(1).ReplaceGraphAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<List<WorkflowState>>(),
            Arg.Any<IReadOnlyList<NewTransitionSpec>>(),
            Arg.Any<IReadOnlyList<NewBranchSpec>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());

        // Behavior: result must succeed.
        result.IsSuccess.Should().BeTrue("validator passed → handler should proceed to save");
    }
}
