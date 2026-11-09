using AqtCoreFW.Workflow.Application.Commands.BatchExecuteTransition;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Validator tests for BatchExecuteTransitionCommand.
/// Rules: 1-50 items, no duplicate instanceIds, required actor, non-empty GUIDs.
/// </summary>
public sealed class BatchExecuteTransitionCommandValidatorTests
{
    private readonly BatchExecuteTransitionCommandValidator _validator = new();

    // BatchTransitionItem(Guid InstanceId, long TransitionId)
    private static BatchTransitionItem Item() => new(Guid.NewGuid(), 1L);

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        var cmd = new BatchExecuteTransitionCommand(
            [Item(), Item()], Guid.NewGuid(), "ok");
        var result = _validator.Validate(cmd);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyItems_Fails()
    {
        var cmd = new BatchExecuteTransitionCommand([], Guid.NewGuid(), null);
        var result = _validator.Validate(cmd);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Items");
    }

    [Fact]
    public void Validate_ExceedsMaxBatchSize_Fails()
    {
        var items = Enumerable.Range(0, 51).Select(_ => Item()).ToList();
        var cmd = new BatchExecuteTransitionCommand(items, Guid.NewGuid(), null);
        var result = _validator.Validate(cmd);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Items");
    }

    [Fact]
    public void Validate_ExactlyMaxBatchSize_Passes()
    {
        var items = Enumerable.Range(0, 50).Select(_ => Item()).ToList();
        var cmd = new BatchExecuteTransitionCommand(items, Guid.NewGuid(), null);
        var result = _validator.Validate(cmd);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_DuplicateInstanceIds_Fails()
    {
        var dupId = Guid.NewGuid();
        var items = new List<BatchTransitionItem>
        {
            new(dupId, 1L),
            new(dupId, 2L),
        };
        var cmd = new BatchExecuteTransitionCommand(items, Guid.NewGuid(), null);
        var result = _validator.Validate(cmd);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Items");
    }

    [Fact]
    public void Validate_EmptyActorId_Fails()
    {
        var cmd = new BatchExecuteTransitionCommand([Item()], Guid.Empty, null);
        var result = _validator.Validate(cmd);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ActorId");
    }

    [Fact]
    public void Validate_EmptyItemInstanceId_Fails()
    {
        var items = new List<BatchTransitionItem> { new(Guid.Empty, 1L) };
        var cmd = new BatchExecuteTransitionCommand(items, Guid.NewGuid(), null);
        var result = _validator.Validate(cmd);
        Assert.False(result.IsValid);
    }
}
