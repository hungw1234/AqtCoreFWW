using AqtCoreFW.Workflow.Application.Commands.DeleteWorkflowDefinition;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for DeleteWorkflowDefinitionCommandHandler.
/// Validates: soft-delete, active instance checks, error codes, and repository calls.
/// </summary>
public sealed class DeleteWorkflowDefinitionCommandHandlerTests
{
    private static readonly Guid DefaultTenantId = Guid.NewGuid();
    private static readonly Guid DefaultDefinitionId = Guid.NewGuid();

    private readonly IWorkflowDefinitionRepository _repository;
    private readonly DeleteWorkflowDefinitionCommandHandler _handler;

    public DeleteWorkflowDefinitionCommandHandlerTests()
    {
        _repository = Substitute.For<IWorkflowDefinitionRepository>();
        _handler = new DeleteWorkflowDefinitionCommandHandler(_repository);
    }

    // --- Helpers ---

    /// <summary>Builds a valid delete command.</summary>
    private static DeleteWorkflowDefinitionCommand BuildValidCommand(
        Guid? definitionId = null) => new(
        Id: definitionId ?? DefaultDefinitionId,
        TenantId: DefaultTenantId,
        DeletedBy: Guid.NewGuid());

    /// <summary>Creates a definition for testing.</summary>
    private static WorkflowDefinition CreateDefinition(Guid? id = null) =>
        WorkflowDefinition.Create(TestGuidGenerator.Create(), "Test Workflow", "Test", DefaultTenantId, Guid.NewGuid());

    // --- Success path ---

    [Fact]
    public async Task Handle_NoActiveInstances_DeletesSuccessfully()
    {
        var command = BuildValidCommand();
        var definition = CreateDefinition(command.Id);

        _repository.GetByIdAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));
        _repository.HasActiveInstancesAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NoActiveInstances_CallsRepositoryDelete()
    {
        var command = BuildValidCommand();
        var definition = CreateDefinition(command.Id);

        _repository.GetByIdAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));
        _repository.HasActiveInstancesAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(false);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).DeleteAsync(
            command.Id,
            Arg.Any<CancellationToken>());
    }

    // --- Active instances conflict ---

    [Fact]
    public async Task Handle_HasActiveInstances_ReturnsConflictError()
    {
        var command = BuildValidCommand();
        var definition = CreateDefinition(command.Id);

        _repository.GetByIdAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));
        _repository.HasActiveInstancesAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("active", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_HasActiveInstances_DoesNotCallDeleteAsync()
    {
        var command = BuildValidCommand();
        var definition = CreateDefinition(command.Id);

        _repository.GetByIdAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));
        _repository.HasActiveInstancesAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(true);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.DidNotReceive().DeleteAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    // --- Definition not found ---

    [Fact]
    public async Task Handle_DefinitionNotFound_ReturnsNotFoundError()
    {
        var command = BuildValidCommand();

        _repository.GetByIdAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Fail("Not found"));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_DefinitionNotFound_DoesNotCheckActiveInstances()
    {
        var command = BuildValidCommand();

        _repository.GetByIdAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Fail("Not found"));

        await _handler.Handle(command, CancellationToken.None);

        await _repository.DidNotReceive().HasActiveInstancesAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DefinitionNotFound_DoesNotCallDeleteAsync()
    {
        var command = BuildValidCommand();

        _repository.GetByIdAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Fail("Not found"));

        await _handler.Handle(command, CancellationToken.None);

        await _repository.DidNotReceive().DeleteAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    // --- Edge cases ---

    [Fact]
    public async Task Handle_DeleteOrderOfOperations_ChecksExistenceBeforeActiveInstances()
    {
        var command = BuildValidCommand();
        var definition = CreateDefinition(command.Id);

        _repository.GetByIdAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));
        _repository.HasActiveInstancesAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(false);

        await _handler.Handle(command, CancellationToken.None);

        // Verify GetByIdAsync called first, then HasActiveInstancesAsync
        Received.InOrder(async () =>
        {
            await _repository.GetByIdAsync(command.Id, Arg.Any<CancellationToken>());
            await _repository.HasActiveInstancesAsync(command.Id, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_DeleteOrderOfOperations_DeletesAfterChecks()
    {
        var command = BuildValidCommand();
        var definition = CreateDefinition(command.Id);

        _repository.GetByIdAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));
        _repository.HasActiveInstancesAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(false);

        await _handler.Handle(command, CancellationToken.None);

        // Verify checks happen before delete
        Received.InOrder(async () =>
        {
            await _repository.GetByIdAsync(command.Id, Arg.Any<CancellationToken>());
            await _repository.HasActiveInstancesAsync(command.Id, Arg.Any<CancellationToken>());
            await _repository.DeleteAsync(command.Id, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_MultipleCallsWithSameId_AllExecuted()
    {
        var command1 = BuildValidCommand(Guid.NewGuid());
        var command2 = BuildValidCommand(Guid.NewGuid());
        var def1 = CreateDefinition(command1.Id);
        var def2 = CreateDefinition(command2.Id);

        _repository.GetByIdAsync(command1.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(def1));
        _repository.GetByIdAsync(command2.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(def2));

        _repository.HasActiveInstancesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await _handler.Handle(command1, CancellationToken.None);
        await _handler.Handle(command2, CancellationToken.None);

        await _repository.Received(2).DeleteAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ActiveInstancesCheckReturnsFalse_DefinitionDeletedEvenIfRecentlyCreated()
    {
        var command = BuildValidCommand();
        var definition = CreateDefinition(command.Id);
        // Even if just created, if no active instances, should delete
        definition.Activate(Guid.NewGuid());

        _repository.GetByIdAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));
        _repository.HasActiveInstancesAsync(command.Id, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
