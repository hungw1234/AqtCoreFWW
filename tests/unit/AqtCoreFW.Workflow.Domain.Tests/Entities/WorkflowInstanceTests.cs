using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Events;
using AqtCoreFW.Workflow.Domain.Tests.Helpers;

namespace AqtCoreFW.Workflow.Domain.Tests.Entities;

/// <summary>
/// Unit tests for WorkflowInstance aggregate.
/// T2 entity: PublicId (Guid AK), Id (long bigint PK — 0 in unit tests without DB).
/// CurrentStateId/DefinitionId/TransitionId are long (T1 entity PKs).
/// ApplyTransition requires IGuidGenerator for WorkflowInstanceHistory.MessageId.
/// </summary>
public sealed class WorkflowInstanceTests
{
    // T1 WorkflowDefinition.Id and WorkflowState.Id are long (not Guid)
    private const long DefinitionId = 1L;
    private const long InitialStateId = 10L;
    private const long NextStateId = 20L;
    private static readonly Guid ActorId = Guid.NewGuid();
    private const string EntityType = "Case";
    private static readonly Guid TenantId = Guid.NewGuid();

    private static WorkflowInstance CreateInstance(int? slaHours = null) =>
        WorkflowInstance.Create(
            TestGuidGenerator.Create(),
            DefinitionId, InitialStateId, EntityType,
            Guid.NewGuid(), TenantId, ActorId, slaHours);

    // S3 Slice 4: requiredPermissionCode removed from WorkflowTransition entity
    private static WorkflowTransition BuildTransition(
        long fromStateId, long toStateId) =>
        WorkflowTransition.Create(
            DefinitionId, fromStateId, toStateId,
            "Approve", "Phê duyệt", "Approve",
            null, 1);

    // --- Create ---

    [Fact]
    public void Create_SetsInitialStateAndStartedAt()
    {
        var before = DateTimeOffset.UtcNow;

        var instance = CreateInstance();

        instance.CurrentStateId.Should().Be(InitialStateId);
        instance.StartedAt.Should().BeOnOrAfter(before);
        instance.DefinitionId.Should().Be(DefinitionId);
    }

    [Fact]
    public void Create_WithSlaHours_SetsDueAt()
    {
        var before = DateTimeOffset.UtcNow;

        var instance = CreateInstance(slaHours: 24);

        instance.DueAt.Should().NotBeNull();
        instance.DueAt!.Value.Should().BeOnOrAfter(before.AddHours(24));
        instance.DueAt!.Value.Should().BeBefore(before.AddHours(25));
    }

    [Fact]
    public void Create_WithoutSlaHours_DueAtIsNull()
    {
        var instance = CreateInstance(slaHours: null);

        instance.DueAt.Should().BeNull();
    }

    [Fact]
    public void Create_CompletedAtIsNull()
    {
        var instance = CreateInstance();

        instance.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Create_GeneratesNonEmptyPublicId()
    {
        // PublicId (T2 Guid AK) is the external identity for WorkflowInstance.
        // Id (long) is 0 in unit tests — DB assigns it.
        var i1 = CreateInstance();
        var i2 = CreateInstance();

        i1.PublicId.Should().NotBe(Guid.Empty);
        i1.PublicId.Should().NotBe(i2.PublicId);
    }

    [Fact]
    public void Create_CurrentStateEnteredAt_IsSetOnCreation()
    {
        var before = DateTimeOffset.UtcNow;

        var instance = CreateInstance();

        instance.CurrentStateEnteredAt.Should().BeOnOrAfter(before);
    }

    // --- ApplyTransition ---

    [Fact]
    public void ApplyTransition_UpdatesCurrentStateId()
    {
        var gen = TestGuidGenerator.Create();
        var instance = WorkflowInstance.Create(gen, DefinitionId, InitialStateId, EntityType, Guid.NewGuid(), TenantId, ActorId);
        var transition = BuildTransition(InitialStateId, NextStateId);

        instance.ApplyTransition(gen, transition, isFinalState: false, ActorId, comment: null);

        instance.CurrentStateId.Should().Be(NextStateId);
    }

    [Fact]
    public void ApplyTransition_RecordsHistoryEntry()
    {
        var gen = TestGuidGenerator.Create();
        var instance = WorkflowInstance.Create(gen, DefinitionId, InitialStateId, EntityType, Guid.NewGuid(), TenantId, ActorId);
        var transition = BuildTransition(InitialStateId, NextStateId);

        instance.ApplyTransition(gen, transition, isFinalState: false, ActorId, comment: "Moving forward");

        instance.History.Should().ContainSingle();
    }

    [Fact]
    public void ApplyTransition_HistoryHasCorrectFromAndToState()
    {
        var gen = TestGuidGenerator.Create();
        var instance = WorkflowInstance.Create(gen, DefinitionId, InitialStateId, EntityType, Guid.NewGuid(), TenantId, ActorId);
        var transition = BuildTransition(InitialStateId, NextStateId);

        instance.ApplyTransition(gen, transition, isFinalState: false, ActorId, comment: null);

        var entry = instance.History.Single();
        entry.FromStateId.Should().Be(InitialStateId);
        entry.ToStateId.Should().Be(NextStateId);
    }

    [Fact]
    public void ApplyTransition_RaisesWorkflowTransitionedEvent()
    {
        var gen = TestGuidGenerator.Create();
        var instance = WorkflowInstance.Create(gen, DefinitionId, InitialStateId, EntityType, Guid.NewGuid(), TenantId, ActorId);
        var transition = BuildTransition(InitialStateId, NextStateId);

        instance.ApplyTransition(gen, transition, isFinalState: false, ActorId, comment: null);

        instance.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<WorkflowTransitionedEvent>();
    }

    [Fact]
    public void ApplyTransition_WorkflowTransitionedEvent_HasCorrectData()
    {
        var gen = TestGuidGenerator.Create();
        var instance = WorkflowInstance.Create(gen, DefinitionId, InitialStateId, EntityType, Guid.NewGuid(), TenantId, ActorId);
        var transition = BuildTransition(InitialStateId, NextStateId);

        instance.ApplyTransition(gen, transition, isFinalState: false, ActorId, comment: null);

        var evt = instance.DomainEvents.OfType<WorkflowTransitionedEvent>().Single();
        // InstancePublicId = T2 Guid AK (not the long Id)
        evt.InstancePublicId.Should().Be(instance.PublicId);
        // NewStateInternalId = long (T1 WorkflowState.Id)
        evt.NewStateInternalId.Should().Be(NextStateId);
        evt.TransitionedBy.Should().Be(ActorId);
    }

    [Fact]
    public void ApplyTransition_FinalState_SetsCompletedAt()
    {
        var gen = TestGuidGenerator.Create();
        var instance = WorkflowInstance.Create(gen, DefinitionId, InitialStateId, EntityType, Guid.NewGuid(), TenantId, ActorId);
        var transition = BuildTransition(InitialStateId, NextStateId);
        var before = DateTimeOffset.UtcNow;

        instance.ApplyTransition(gen, transition, isFinalState: true, ActorId, comment: null);

        instance.CompletedAt.Should().NotBeNull();
        instance.CompletedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void ApplyTransition_NonFinalState_CompletedAtRemainsNull()
    {
        var gen = TestGuidGenerator.Create();
        var instance = WorkflowInstance.Create(gen, DefinitionId, InitialStateId, EntityType, Guid.NewGuid(), TenantId, ActorId);
        var transition = BuildTransition(InitialStateId, NextStateId);

        instance.ApplyTransition(gen, transition, isFinalState: false, ActorId, comment: null);

        instance.CompletedAt.Should().BeNull();
    }

    // --- ApplyParallelJoinTransition ---

    [Fact]
    public void ApplyParallelJoinTransition_FinalState_SetsCompletedAt()
    {
        var gen = TestGuidGenerator.Create();
        var instance = WorkflowInstance.Create(gen, DefinitionId, InitialStateId, EntityType, Guid.NewGuid(), TenantId, ActorId);
        var before = DateTimeOffset.UtcNow;

        instance.ApplyParallelJoinTransition(gen, NextStateId, isFinalState: true, ActorId);

        instance.CurrentStateId.Should().Be(NextStateId);
        instance.CompletedAt.Should().NotBeNull();
        instance.CompletedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void ApplyParallelJoinTransition_NonFinalState_CompletedAtRemainsNull()
    {
        var gen = TestGuidGenerator.Create();
        var instance = WorkflowInstance.Create(gen, DefinitionId, InitialStateId, EntityType, Guid.NewGuid(), TenantId, ActorId);

        instance.ApplyParallelJoinTransition(gen, NextStateId, isFinalState: false, ActorId);

        instance.CurrentStateId.Should().Be(NextStateId);
        instance.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void ApplyParallelJoinTransition_RaisesWorkflowTransitionedEvent()
    {
        var gen = TestGuidGenerator.Create();
        var instance = WorkflowInstance.Create(gen, DefinitionId, InitialStateId, EntityType, Guid.NewGuid(), TenantId, ActorId);

        instance.ApplyParallelJoinTransition(gen, NextStateId, isFinalState: false, ActorId);

        var evt = instance.DomainEvents.OfType<WorkflowTransitionedEvent>().Single();
        evt.NewStateInternalId.Should().Be(NextStateId);
        evt.TransitionedBy.Should().Be(ActorId);
    }

    [Fact]
    public void ApplyTransition_UpdatesCurrentStateEnteredAt()
    {
        var gen = TestGuidGenerator.Create();
        var instance = WorkflowInstance.Create(gen, DefinitionId, InitialStateId, EntityType, Guid.NewGuid(), TenantId, ActorId);
        var transition = BuildTransition(InitialStateId, NextStateId);
        var originalEnteredAt = instance.CurrentStateEnteredAt;

        // Small delay to ensure time advances
        System.Threading.Thread.Sleep(10);

        instance.ApplyTransition(gen, transition, isFinalState: false, ActorId, comment: null);

        instance.CurrentStateEnteredAt.Should().BeAfter(originalEnteredAt);
    }

    [Fact]
    public void ApplyTransition_HistoryEntry_RecordsComment()
    {
        var gen = TestGuidGenerator.Create();
        var instance = WorkflowInstance.Create(gen, DefinitionId, InitialStateId, EntityType, Guid.NewGuid(), TenantId, ActorId);
        var transition = BuildTransition(InitialStateId, NextStateId);

        instance.ApplyTransition(gen, transition, isFinalState: false, ActorId, "My comment");

        instance.History.Single().Comment.Should().Be("My comment");
    }

    // --- SLA breach ---

    [Fact]
    public void MarkSlaBreachNotified_SetsSlaBreachNotifiedAt()
    {
        var instance = CreateInstance();
        var before = DateTimeOffset.UtcNow;

        instance.MarkSlaBreachNotified();

        instance.SlaBreachNotifiedAt.Should().NotBeNull();
        instance.SlaBreachNotifiedAt!.Value.Should().BeOnOrAfter(before);
    }
}
