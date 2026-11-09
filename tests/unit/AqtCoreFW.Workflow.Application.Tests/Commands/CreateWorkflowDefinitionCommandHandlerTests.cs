using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowDefinition;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using NSubstitute;
using System.Reflection;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateWorkflowDefinitionCommandHandler.
/// Validates: initial-state count, duplicate state names, transition reference integrity,
/// duplicate transitions, persistence, and DTO mapping.
/// </summary>
public sealed class CreateWorkflowDefinitionCommandHandlerTests
{
    private static readonly Guid DefaultTenantId = Guid.NewGuid();

    private static long _idSeed;

    /// <summary>Assigns a unique test Id to a T1 entity via reflection (bypasses EF DB-assignment).</summary>
    private static void AssignTestId(AqtCoreFW.SharedKernel.Domain.Entity entity)
    {
        var id = Interlocked.Increment(ref _idSeed);
        typeof(AqtCoreFW.SharedKernel.Domain.Entity)
            .GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(entity, id);
    }

    private readonly IWorkflowDefinitionRepository _repository;
    private readonly CreateWorkflowDefinitionCommandHandler _handler;

    public CreateWorkflowDefinitionCommandHandlerTests()
    {
        _repository = Substitute.For<IWorkflowDefinitionRepository>();

        // Simulate EF IDENTITY assignment: when AddAsync is called, assign positive IDs
        // to all states on the definition so that Pass-3 transition wiring (fromStateId > 0)
        // does not throw the WorkflowTransition.Create guard in unit tests.
        _repository
            .When(r => r.AddAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>()))
            .Do(ci =>
            {
                var def = ci.Arg<WorkflowDefinition>();
                foreach (var state in def.States)
                    AssignTestId(state);
            });

        _handler = new CreateWorkflowDefinitionCommandHandler(_repository, TestGuidGenerator.Create());
    }

    // --- Helpers ---

    /// <summary>Builds a minimal valid command: Draft → Approved with one Approve transition.</summary>
    private static CreateWorkflowDefinitionCommand BuildValidCommand(
        Guid? tenantId = null) => new(
        Name: "Test Workflow",
        Description: "A test workflow",
        TenantId: tenantId ?? DefaultTenantId,
        CreatedBy: Guid.NewGuid(),
        States:
        [
            new CreateStateRequest("Draft", "Nháp", "Draft", IsInitial: true, IsFinal: false, "#808080", 1),
            new CreateStateRequest("Approved", "Đã duyệt", "Approved", IsInitial: false, IsFinal: true, "#00FF00", 2)
        ],
        Transitions:
        [
            new CreateTransitionRequest("Draft", "Approved", "Approve", "Phê duyệt", "Approve",
                null, 1)
        ]);

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidDefinition_ReturnsSuccessWithDto()
    {
        var command = BuildValidCommand();

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Test Workflow");
        result.Value.TenantId.Should().Be(DefaultTenantId);
    }

    [Fact]
    public async Task Handle_ValidDefinition_PersistsViaRepository()
    {
        var command = BuildValidCommand();

        await _handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<WorkflowDefinition>(d => d.Name == "Test Workflow"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DefinitionIsInactiveByDefault()
    {
        var command = BuildValidCommand();

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_StatesAndTransitions_WiredByNameCorrectly()
    {
        var command = BuildValidCommand();

        var result = await _handler.Handle(command, CancellationToken.None);

        var dto = result.Value;
        dto.States.Should().HaveCount(2);
        dto.Transitions.Should().ContainSingle();

        // Transition FromStateId must point to Draft state
        var draftState = dto.States.Single(s => s.Name == "Draft");
        var approvedState = dto.States.Single(s => s.Name == "Approved");
        var transition = dto.Transitions.Single();

        transition.FromStateId.Should().Be(draftState.Id);
        transition.ToStateId.Should().Be(approvedState.Id);
    }

    // --- Validation failures ---

    [Fact]
    public async Task Handle_NoInitialState_ReturnsNoInitialStateError()
    {
        var command = new CreateWorkflowDefinitionCommand(
            "Flow", "Desc", DefaultTenantId, Guid.NewGuid(),
            States:
            [
                new CreateStateRequest("Draft", "Nháp", "Draft", IsInitial: false, IsFinal: false, "#808080", 1)
            ],
            Transitions: []);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("initial state"));
    }

    [Fact]
    public async Task Handle_MultipleInitialStates_ReturnsNoInitialStateError()
    {
        var command = new CreateWorkflowDefinitionCommand(
            "Flow", "Desc", DefaultTenantId, Guid.NewGuid(),
            States:
            [
                new CreateStateRequest("Draft", "Nháp", "Draft", IsInitial: true, IsFinal: false, "#808080", 1),
                new CreateStateRequest("New", "Mới", "New", IsInitial: true, IsFinal: false, "#AAAAAA", 2)
            ],
            Transitions: []);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("initial state"));
    }

    [Fact]
    public async Task Handle_DuplicateStateName_ReturnsDuplicateStateNameError()
    {
        var command = new CreateWorkflowDefinitionCommand(
            "Flow", "Desc", DefaultTenantId, Guid.NewGuid(),
            States:
            [
                new CreateStateRequest("Draft", "Nháp", "Draft", IsInitial: true, IsFinal: false, "#808080", 1),
                new CreateStateRequest("Draft", "Nháp 2", "Draft 2", IsInitial: false, IsFinal: true, "#FFFFFF", 2)
            ],
            Transitions: []);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("Draft"));
    }

    [Fact]
    public async Task Handle_InvalidFromStateName_ReturnsError()
    {
        var command = new CreateWorkflowDefinitionCommand(
            "Flow", "Desc", DefaultTenantId, Guid.NewGuid(),
            States:
            [
                new CreateStateRequest("Draft", "Nháp", "Draft", IsInitial: true, IsFinal: false, "#808080", 1),
                new CreateStateRequest("Approved", "Đã duyệt", "Approved", IsInitial: false, IsFinal: true, "#00FF00", 2)
            ],
            Transitions:
            [
                new CreateTransitionRequest("NonExistent", "Approved", "Approve",
                    "Phê duyệt", "Approve", null, 1)
            ]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("NonExistent"));
    }

    [Fact]
    public async Task Handle_InvalidToStateName_ReturnsError()
    {
        var command = new CreateWorkflowDefinitionCommand(
            "Flow", "Desc", DefaultTenantId, Guid.NewGuid(),
            States:
            [
                new CreateStateRequest("Draft", "Nháp", "Draft", IsInitial: true, IsFinal: false, "#808080", 1)
            ],
            Transitions:
            [
                new CreateTransitionRequest("Draft", "NonExistent", "Approve",
                    "Phê duyệt", "Approve", null, 1)
            ]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("NonExistent"));
    }

    [Fact]
    public async Task Handle_DuplicateTransition_ReturnsDuplicateTransitionError()
    {
        var command = new CreateWorkflowDefinitionCommand(
            "Flow", "Desc", DefaultTenantId, Guid.NewGuid(),
            States:
            [
                new CreateStateRequest("Draft", "Nháp", "Draft", IsInitial: true, IsFinal: false, "#808080", 1),
                new CreateStateRequest("Approved", "Đã duyệt", "Approved", IsInitial: false, IsFinal: true, "#00FF00", 2)
            ],
            Transitions:
            [
                new CreateTransitionRequest("Draft", "Approved", "Approve", "Phê duyệt", "Approve", null, 1),
                new CreateTransitionRequest("Draft", "Approved", "Approve", "Phê duyệt", "Approve", null, 2)
            ]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("Approve"));
    }

    [Fact]
    public async Task Handle_NoInitialState_DoesNotCallRepository()
    {
        var command = new CreateWorkflowDefinitionCommand(
            "Flow", "Desc", DefaultTenantId, Guid.NewGuid(),
            States:
            [
                new CreateStateRequest("Draft", "Nháp", "Draft", IsInitial: false, IsFinal: false, "#808080", 1)
            ],
            Transitions: []);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.DidNotReceive().AddAsync(
            Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>());
    }
}
