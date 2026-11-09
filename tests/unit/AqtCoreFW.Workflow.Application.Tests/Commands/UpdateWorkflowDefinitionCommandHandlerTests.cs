using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowDefinition;
using AqtCoreFW.Workflow.Application.Commands.UpdateWorkflowDefinition;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using NSubstitute;
using System.Reflection;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for UpdateWorkflowDefinitionCommandHandler.
/// Validates: name/description updates, persistence, DTO mapping, and error handling.
/// </summary>
public sealed class UpdateWorkflowDefinitionCommandHandlerTests
{
    private static readonly Guid DefaultTenantId = Guid.NewGuid();
    private static readonly Guid DefaultDefinitionId = Guid.NewGuid();

    private readonly IWorkflowDefinitionRepository _repository;
    private readonly UpdateWorkflowDefinitionCommandHandler _handler;

    public UpdateWorkflowDefinitionCommandHandlerTests()
    {
        _repository = Substitute.For<IWorkflowDefinitionRepository>();
        _handler = new UpdateWorkflowDefinitionCommandHandler(_repository);
    }

    // --- Helpers ---

    private static long _idSeed;

    /// <summary>Assigns a unique test Id to a T1 entity via reflection (bypasses EF DB-assignment).</summary>
    private static long AssignTestId(AqtCoreFW.SharedKernel.Domain.Entity entity)
    {
        var id = Interlocked.Increment(ref _idSeed);
        typeof(AqtCoreFW.SharedKernel.Domain.Entity)
            .GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(entity, id);
        return id;
    }

    /// <summary>Builds a valid update command.</summary>
    private static UpdateWorkflowDefinitionCommand BuildValidCommand(
        Guid? definitionId = null,
        string? name = null,
        string? description = null) => new(
        Id: definitionId ?? DefaultDefinitionId,
        Name: name ?? "Updated Workflow",
        Description: description ?? "Updated description",
        TenantId: DefaultTenantId,
        ModifiedBy: Guid.NewGuid());

    /// <summary>Creates a definition with states and transitions for testing.</summary>
    private static WorkflowDefinition CreateDefinitionWithStates(Guid? id = null)
    {
        var defId = id ?? DefaultDefinitionId;
        var def = WorkflowDefinition.Create(TestGuidGenerator.Create(), "Original Name", "Original Desc", DefaultTenantId, Guid.NewGuid());

        // Add minimal graph for realistic testing.
        // AssignTestId sets a positive long Id via reflection — simulates EF IDENTITY assignment
        // so WorkflowTransition.Create's fromStateId > 0 guard does not throw in unit tests.
        var s1 = WorkflowState.Create(def.Id, "Draft", "Nháp", "Draft", true, false, "#808080", 1);
        var s2 = WorkflowState.Create(def.Id, "Approved", "Đã duyệt", "Approved", false, true, "#00FF00", 2);
        AssignTestId(s1);
        AssignTestId(s2);
        def.AddState(s1);
        def.AddState(s2);

        // S3 Slice 4: removed requiredPermissionCode param
        var t = WorkflowTransition.Create(def.Id, s1.Id, s2.Id,
            "Approve", "Phê duyệt", "Approve", null, 1);
        def.AddTransition(t);

        return def;
    }

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidCommand_UpdatesNameAndDescription()
    {
        var command = BuildValidCommand(
            name: "New Name",
            description: "New Description");
        var definition = CreateDefinitionWithStates(command.Id);

        _repository.GetWithStatesAndTransitionsAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("New Name");
        result.Value.Description.Should().Be("New Description");
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsViaRepository()
    {
        var command = BuildValidCommand();
        var definition = CreateDefinitionWithStates(command.Id);

        _repository.GetWithStatesAndTransitionsAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        await _handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).UpdateAsync(
            Arg.Is<WorkflowDefinition>(d => d.Name == "Updated Workflow"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsDtoWithStatesAndTransitions()
    {
        var command = BuildValidCommand();
        var definition = CreateDefinitionWithStates(command.Id);

        _repository.GetWithStatesAndTransitionsAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.States.Should().HaveCount(2);
        result.Value.Transitions.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_ValidCommand_PreservesVersionAndDefinitionKey()
    {
        var command = BuildValidCommand();
        var definition = WorkflowDefinition.Create(
            TestGuidGenerator.Create(), "Original", "Desc", DefaultTenantId, Guid.NewGuid(),
            definitionKey: "stable-key");
        definition.MarkNotLatest(Guid.NewGuid());  // Simulate older version

        _repository.GetWithStatesAndTransitionsAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.DefinitionKey.Should().Be("stable-key");
        result.Value.Version.Should().Be(1);
    }

    // --- Error scenarios ---

    [Fact]
    public async Task Handle_DefinitionNotFound_ReturnsNotFoundError()
    {
        var command = BuildValidCommand();

        _repository.GetWithStatesAndTransitionsAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Fail("Not found"));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_RepositoryFailure_PropagatesError()
    {
        var command = BuildValidCommand();

        _repository.GetWithStatesAndTransitionsAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<WorkflowDefinition>("Database error")));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DefinitionNotFound_DoesNotCallUpdateAsync()
    {
        var command = BuildValidCommand();

        _repository.GetWithStatesAndTransitionsAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Fail("Not found"));

        await _handler.Handle(command, CancellationToken.None);

        await _repository.DidNotReceive().UpdateAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<CancellationToken>());
    }

    // --- Edge cases ---

    [Fact]
    public async Task Handle_EmptyName_StillUpdates()
    {
        var command = new UpdateWorkflowDefinitionCommand(
            DefaultDefinitionId,
            "",
            "New Desc",
            DefaultTenantId,
            Guid.NewGuid());
        var definition = CreateDefinitionWithStates(command.Id);

        _repository.GetWithStatesAndTransitionsAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        var result = await _handler.Handle(command, CancellationToken.None);

        // Handler does not validate input — UpdateAsync is called regardless
        await _repository.Received(1).UpdateAsync(
            Arg.Any<WorkflowDefinition>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SameNameAndDescription_StillUpdates()
    {
        var definition = CreateDefinitionWithStates();
        // definition.PublicId = Guid (T2 AK); command.Id takes Guid (PublicId)
        var command = new UpdateWorkflowDefinitionCommand(
            definition.PublicId,
            definition.Name,
            definition.Description,
            DefaultTenantId,
            Guid.NewGuid());

        _repository.GetWithStatesAndTransitionsAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
