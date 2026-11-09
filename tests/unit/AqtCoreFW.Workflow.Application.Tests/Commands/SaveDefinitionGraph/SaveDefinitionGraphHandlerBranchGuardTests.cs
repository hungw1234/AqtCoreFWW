using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.SaveDefinitionGraph;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Validation;
using FluentAssertions;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands.SaveDefinitionGraph;

/// <summary>
/// Save-handler structural guards for the parallel-branch payload. An unknown branch state name must
/// short-circuit with a <see cref="ValidationError"/> (422) BEFORE the graph validator runs and BEFORE
/// ReplaceGraphAsync persists anything — mirroring the transition name guards.
/// </summary>
public sealed class SaveDefinitionGraphHandlerBranchGuardTests
{
    private readonly IWorkflowDefinitionRepository _repo = Substitute.For<IWorkflowDefinitionRepository>();
    private readonly IWorkflowGraphValidator _validator = Substitute.For<IWorkflowGraphValidator>();
    private readonly SaveDefinitionGraphCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid DefinitionId = Guid.NewGuid();

    public SaveDefinitionGraphHandlerBranchGuardTests()
    {
        _handler = new SaveDefinitionGraphCommandHandler(_repo, _validator);

        // Build the definition BEFORE the .Returns() call — configuring the IGuidGenerator substitute
        // inside another substitute's Returns() misroutes the stub (NSubstitute pending-call hazard).
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
    }

    private static WorkflowDefinition MakeDefinition()
    {
        var gen = Substitute.For<AqtCoreFW.SharedKernel.Abstractions.IGuidGenerator>();
        gen.NewIdMonotonic().Returns(DefinitionId);
        return WorkflowDefinition.Create(gen, "Test Def", "desc", TenantId, ActorId, "test-key", "Generic");
    }

    [Fact]
    public async Task Handle_BranchWithUnknownBranchingState_ReturnsValidationError_DoesNotSave()
    {
        var command = new SaveDefinitionGraphCommand(
            DefinitionId, TenantId, ActorId,
            States: [new GraphStateInput("Draft", "Nháp", "Draft", true, false, "#FFF", 1)],
            Transitions: [])
        {
            Branches =
            [
                new GraphBranchInput(
                    BranchingStateName: "Ghost",          // not in the graph
                    ContinuationStateName: "Draft",
                    BranchType: ParallelBranchType.And,
                    AllowCancelOnAnyReject: false,
                    TimeoutMinutes: 0,
                    Children: [new GraphBranchChildInput("Draft", "c", "C", 1, [])]),
            ],
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainItemsAssignableTo<ValidationError>(
            "an unknown branch state name is a structural 422, not a graph-validation failure");

        // Short-circuits before validation + persistence.
        await _validator.DidNotReceive().ValidateForSaveAsync(
            Arg.Any<IReadOnlyList<WorkflowState>>(),
            Arg.Any<IReadOnlyList<NewTransitionSpec>>(),
            Arg.Any<IReadOnlyList<NewBranchSpec>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().ReplaceGraphAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<List<WorkflowState>>(),
            Arg.Any<IReadOnlyList<NewTransitionSpec>>(),
            Arg.Any<IReadOnlyList<NewBranchSpec>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    // Two resolutions of the same type on one child would violate
    // UX_WorkflowBranchResolutions_ChildId_ResolutionType → must 422, not reach the repo (500 guard).
    [Fact]
    public async Task Handle_BranchChildWithDuplicateResolutionType_ReturnsValidationError_DoesNotSave()
    {
        var command = CommandWithBranch(
            [new GraphBranchChildInput("Approved", "child-a", "A", 1,
                [
                    new GraphBranchResolutionInput(BranchResolutionType.Approved, null),
                    new GraphBranchResolutionInput(BranchResolutionType.Approved, null),
                ])]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Errors.Should().ContainItemsAssignableTo<ValidationError>();
        await AssertNotPersisted();
    }

    // Two children with the same machine name on one fork would violate
    // UX_WorkflowBranchChildren_ParallelBranchId_Name → must 422, not reach the repo.
    [Fact]
    public async Task Handle_BranchWithDuplicateChildName_ReturnsValidationError_DoesNotSave()
    {
        var command = CommandWithBranch(
            [
                new GraphBranchChildInput("Approved", "dup", "A", 1, []),
                new GraphBranchChildInput("Approved", "dup", "B", 2, []),
            ]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Errors.Should().ContainItemsAssignableTo<ValidationError>();
        await AssertNotPersisted();
    }

    // Branch on Draft → Approved with the supplied children; states Draft (initial) + Approved exist.
    private static SaveDefinitionGraphCommand CommandWithBranch(IReadOnlyList<GraphBranchChildInput> children) =>
        new(
            DefinitionId, TenantId, ActorId,
            States:
            [
                new GraphStateInput("Draft", "Nháp", "Draft", true, false, "#FFF", 1),
                new GraphStateInput("Approved", "Duyệt", "Approved", false, true, "#FFF", 2),
            ],
            Transitions: [])
        {
            Branches =
            [
                new GraphBranchInput("Draft", "Approved", ParallelBranchType.And, false, 0, children),
            ],
        };

    private async Task AssertNotPersisted() =>
        await _repo.DidNotReceive().ReplaceGraphAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<List<WorkflowState>>(),
            Arg.Any<IReadOnlyList<NewTransitionSpec>>(),
            Arg.Any<IReadOnlyList<NewBranchSpec>>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
}
