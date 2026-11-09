using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Domain.Tests.Entities;

/// <summary>
/// Unit tests for WorkflowTask delegation methods: Delegate() and ReturnDelegation().
/// T1 entity: WorkflowTask.Id=long (DB-assigned — 0 in unit tests).
/// WorkflowInstanceId: long (FK to T2 WorkflowInstance.Id).
/// AssigneeId/DelegatedFromUserId: Guid? (identity FK exceptions).
/// Covers: happy path, guard clauses, idempotency (F08), state invariants.
/// </summary>
public sealed class WorkflowTaskDelegationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    // WorkflowTask.WorkflowInstanceId is long (T2 WorkflowInstance.Id FK)
    private const long InstanceId = 1L;
    private static readonly Guid OriginalAssignee = Guid.NewGuid();
    private static readonly Guid DelegateUser = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static WorkflowTask CreateTask(Guid? assigneeId = null) =>
        WorkflowTask.Create(
            InstanceId, "Test Task", null,
            assigneeId ?? OriginalAssignee,
            WorkflowTaskPriority.Normal, null, TenantId, ActorId);

    // --- Delegate() ---

    [Fact]
    public void Delegate_SetsAssigneeToDelegate()
    {
        var task = CreateTask(OriginalAssignee);

        task.Delegate(OriginalAssignee, DelegateUser, ActorId);

        task.AssigneeId.Should().Be(DelegateUser);
    }

    [Fact]
    public void Delegate_StoresDelegatedFromUserId()
    {
        var task = CreateTask(OriginalAssignee);

        task.Delegate(OriginalAssignee, DelegateUser, ActorId);

        task.DelegatedFromUserId.Should().Be(OriginalAssignee);
    }

    [Fact]
    public void Delegate_WhenAlreadyDelegated_Throws()
    {
        var task = CreateTask(OriginalAssignee);
        task.Delegate(OriginalAssignee, DelegateUser, ActorId);

        // Attempt second delegation without returning first
        var act = () => task.Delegate(DelegateUser, Guid.NewGuid(), ActorId);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already delegated*");
    }

    [Fact]
    public void Delegate_InitiallyNotDelegated_DelegatedFromUserIdIsNull()
    {
        var task = CreateTask(OriginalAssignee);

        task.DelegatedFromUserId.Should().BeNull();
    }

    // --- ReturnDelegation() ---

    [Fact]
    public void ReturnDelegation_RestoresOriginalAssignee()
    {
        var task = CreateTask(OriginalAssignee);
        task.Delegate(OriginalAssignee, DelegateUser, ActorId);

        task.ReturnDelegation(ActorId);

        task.AssigneeId.Should().Be(OriginalAssignee);
    }

    [Fact]
    public void ReturnDelegation_ClearsDelegatedFromUserId()
    {
        var task = CreateTask(OriginalAssignee);
        task.Delegate(OriginalAssignee, DelegateUser, ActorId);

        task.ReturnDelegation(ActorId);

        task.DelegatedFromUserId.Should().BeNull();
    }

    [Fact]
    public void ReturnDelegation_WhenNotDelegated_IsIdempotent_F08Guard()
    {
        // F08: ReturnDelegation must be a no-op when DelegatedFromUserId is null
        var task = CreateTask(OriginalAssignee);

        // Should not throw, should not change state
        var act = () => task.ReturnDelegation(ActorId);

        act.Should().NotThrow();
        task.AssigneeId.Should().Be(OriginalAssignee);
        task.DelegatedFromUserId.Should().BeNull();
    }

    [Fact]
    public void ReturnDelegation_ThenDelegate_CanRedelegate()
    {
        var task = CreateTask(OriginalAssignee);
        var secondDelegate = Guid.NewGuid();

        task.Delegate(OriginalAssignee, DelegateUser, ActorId);
        task.ReturnDelegation(ActorId);
        task.Delegate(OriginalAssignee, secondDelegate, ActorId);

        task.AssigneeId.Should().Be(secondDelegate);
        task.DelegatedFromUserId.Should().Be(OriginalAssignee);
    }

    [Fact]
    public void ReturnDelegation_CalledTwice_SecondCallIsNoOp()
    {
        var task = CreateTask(OriginalAssignee);
        task.Delegate(OriginalAssignee, DelegateUser, ActorId);
        task.ReturnDelegation(ActorId);

        // Second return — already cleared
        var act = () => task.ReturnDelegation(ActorId);

        act.Should().NotThrow();
        task.AssigneeId.Should().Be(OriginalAssignee);
    }
}
