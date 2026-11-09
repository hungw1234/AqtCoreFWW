using AqtCoreFW.Workflow.Application.Commands.BatchExecuteTransition;
using AqtCoreFW.Workflow.Application.Commands.ExecuteTransition;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Tests for BatchExecuteTransitionCommandHandler.
/// Verifies partial-failure semantics: each item succeeds or fails independently.
/// Handler delegates to mediator.Send(ExecuteTransitionCommand) per item.
/// Phase-4b B4c: BatchTransitionItem(Guid InstanceId, long TransitionId).
/// WorkflowInstanceDto: DefinitionId=long, CurrentStateId=long.
/// </summary>
public sealed class BatchExecuteTransitionCommandHandlerTests
{
    private readonly IMediator _mediator;
    private readonly BatchExecuteTransitionCommandHandler _handler;
    private static readonly Guid ActorId = Guid.NewGuid();

    public BatchExecuteTransitionCommandHandlerTests()
    {
        _mediator = Substitute.For<IMediator>();
        _handler = new BatchExecuteTransitionCommandHandler(_mediator);
    }

    // BatchTransitionItem(Guid InstanceId, long TransitionId)
    private static BatchTransitionItem Item() => new(Guid.NewGuid(), 1L);

    private static WorkflowInstanceDto Dto(Guid instanceId) => new(
        instanceId, 1L, "Case", Guid.NewGuid(), 10L, Guid.NewGuid(),
        DateTimeOffset.UtcNow, null, null, DateTimeOffset.UtcNow,
        []);

    [Fact]
    public async Task Handle_AllItemsSucceed_ReturnsAllSuccess()
    {
        var items = new List<BatchTransitionItem> { Item(), Item(), Item() };
        _mediator.Send(Arg.Any<ExecuteTransitionCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result.Ok(Dto(((ExecuteTransitionCommand)ci[0]).InstanceId)));

        var cmd = new BatchExecuteTransitionCommand(items, ActorId, "ok");
        var result = await _handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(3, result.Value.SuccessCount);
        Assert.Equal(0, result.Value.FailureCount);
        Assert.All(result.Value.Items, i => Assert.True(i.Success));
    }

    [Fact]
    public async Task Handle_PartialFailure_ReturnsMixedResults()
    {
        var okItem = Item();
        var failItem = Item();
        _mediator.Send(
                Arg.Is<ExecuteTransitionCommand>(c => c.InstanceId == okItem.InstanceId),
                Arg.Any<CancellationToken>())
            .Returns(Result.Ok(Dto(okItem.InstanceId)));
        _mediator.Send(
                Arg.Is<ExecuteTransitionCommand>(c => c.InstanceId == failItem.InstanceId),
                Arg.Any<CancellationToken>())
            .Returns(Result.Fail<WorkflowInstanceDto>("Invalid transition from current state"));

        var cmd = new BatchExecuteTransitionCommand([okItem, failItem], ActorId, null);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Equal(1, result.Value.FailureCount);
        var ok = result.Value.Items.First(i => i.InstanceId == okItem.InstanceId);
        var fail = result.Value.Items.First(i => i.InstanceId == failItem.InstanceId);
        Assert.True(ok.Success);
        Assert.False(fail.Success);
        Assert.Contains("Invalid transition", fail.Error);
    }

    [Fact]
    public async Task Handle_SingleItemThrowsException_RecordedAsFailure_BatchContinues()
    {
        var item1 = Item();
        var item2 = Item();
        _mediator.Send(
                Arg.Is<ExecuteTransitionCommand>(c => c.InstanceId == item1.InstanceId),
                Arg.Any<CancellationToken>())
            .Returns<Task<Result<WorkflowInstanceDto>>>(_ => throw new InvalidOperationException("boom"));
        _mediator.Send(
                Arg.Is<ExecuteTransitionCommand>(c => c.InstanceId == item2.InstanceId),
                Arg.Any<CancellationToken>())
            .Returns(Result.Ok(Dto(item2.InstanceId)));

        var cmd = new BatchExecuteTransitionCommand([item1, item2], ActorId, null);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Equal(1, result.Value.FailureCount);
        var fail = result.Value.Items.First(i => i.InstanceId == item1.InstanceId);
        Assert.False(fail.Success);
        // Error must be sanitized — no raw exception message leaked to clients (M2 security fix)
        Assert.DoesNotContain("boom", fail.Error);
        Assert.Contains("InvalidOperationException", fail.Error);
    }

    [Fact]
    public async Task Handle_SequentialProcessing_CallsMediatorForEachItem()
    {
        var items = new List<BatchTransitionItem> { Item(), Item(), Item() };
        _mediator.Send(Arg.Any<ExecuteTransitionCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result.Ok(Dto(((ExecuteTransitionCommand)ci[0]).InstanceId)));

        var cmd = new BatchExecuteTransitionCommand(items, ActorId, null);
        await _handler.Handle(cmd, CancellationToken.None);

        await _mediator.Received(3).Send(Arg.Any<ExecuteTransitionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PassesActorIdAndCommentToEachItem()
    {
        var items = new List<BatchTransitionItem> { Item() };
        ExecuteTransitionCommand? captured = null;
        _mediator.Send(Arg.Do<ExecuteTransitionCommand>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(Dto(items[0].InstanceId)));

        var cmd = new BatchExecuteTransitionCommand(items, ActorId, "batch comment");
        await _handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(ActorId, captured.ActorId);
        Assert.Equal("batch comment", captured.Comment);
    }
}
