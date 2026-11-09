using AqtCoreFW.SharedKernel.Application.Workflow;

namespace AqtCoreFW.Regression.Tests.Core;

/// <summary>
/// Regression tests for WorkflowEngine — invariants that must never break.
/// TC-REG-CORE-004 supplementary (engine-level, not module-specific).
/// </summary>
public sealed class WorkflowEngineRegressionTests
{
    private enum TestState { New, Active, Done }
    private enum TestAction { Start, Complete }

    private static WorkflowEngine<TestState, TestAction> CreateEngine() =>
        new WorkflowEngine<TestState, TestAction>()
            .Allow(TestState.New, TestAction.Start, TestState.Active)
            .Allow(TestState.Active, TestAction.Complete, TestState.Done);

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Module", "Core")]
    public void Execute_ValidTransition_ReturnsNewState()
    {
        var engine = CreateEngine();
        var result = engine.Execute(TestState.New, TestAction.Start);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(TestState.Active);
    }

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Module", "Core")]
    public void Execute_InvalidTransition_ReturnsFail()
    {
        var engine = CreateEngine();
        var result = engine.Execute(TestState.New, TestAction.Complete);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Module", "Core")]
    public void CanExecute_ReflectsRegisteredTransitions()
    {
        var engine = CreateEngine();

        engine.CanExecute(TestState.New, TestAction.Start).Should().BeTrue();
        engine.CanExecute(TestState.New, TestAction.Complete).Should().BeFalse();
        engine.CanExecute(TestState.Active, TestAction.Complete).Should().BeTrue();
        engine.CanExecute(TestState.Done, TestAction.Start).Should().BeFalse();
    }
}
