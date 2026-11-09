using FluentResults;

namespace AqtCoreFW.SharedKernel.Application.Workflow;

/// <summary>
/// Generic configurable state machine for domain workflow enforcement.
/// Modules define their own transitions via Allow(); engine enforces invariants.
/// Usage: new WorkflowEngine&lt;CaseStatus, CaseAction&gt;()
///            .Allow(CaseStatus.Draft, CaseAction.Submit, CaseStatus.Submitted)
///            .Execute(current, action);
/// </summary>
public sealed class WorkflowEngine<TState, TAction>
    where TState : struct, Enum
    where TAction : struct, Enum
{
    private readonly Dictionary<(TState From, TAction Action), TState> _transitions = new();

    /// <summary>Register a valid state transition.</summary>
    public WorkflowEngine<TState, TAction> Allow(TState from, TAction action, TState to)
    {
        _transitions[(from, action)] = to;
        return this;
    }

    /// <summary>Execute transition. Returns new state or Fail if transition not allowed.</summary>
    public Result<TState> Execute(TState current, TAction action)
    {
        if (_transitions.TryGetValue((current, action), out var next))
            return Result.Ok(next);

        return Result.Fail(
            $"Transition '{action}' is not allowed from state '{current}'.");
    }

    /// <summary>Check if a transition is valid without executing it.</summary>
    public bool CanExecute(TState current, TAction action) =>
        _transitions.ContainsKey((current, action));
}
