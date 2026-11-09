using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Events;
using AqtCoreFW.Workflow.Domain.Tests.Helpers;

namespace AqtCoreFW.Workflow.Domain.Tests.Entities;

/// <summary>
/// Unit tests for WorkflowDefinition aggregate root.
/// T2 entity: PublicId (Guid AK), Id (long bigint PK — 0 in unit tests without DB).
/// States/Transitions use long IDs (T1 entity PKs — 0 in unit tests without DB).
/// </summary>
public sealed class WorkflowDefinitionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static WorkflowDefinition CreateDefinition(string name = "My Workflow") =>
        WorkflowDefinition.Create(TestGuidGenerator.Create(), name, "Test description", TenantId, ActorId);

    // --- Factory ---

    [Fact]
    public void Create_ValidInput_SetsNameAndDescription()
    {
        var def = WorkflowDefinition.Create(TestGuidGenerator.Create(), "Approval Flow", "Handles approvals", TenantId, ActorId);

        def.Name.Should().Be("Approval Flow");
        def.Description.Should().Be("Handles approvals");
    }

    [Fact]
    public void Create_SetsIsActiveFalse()
    {
        var def = CreateDefinition();

        def.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Create_GeneratesNonEmptyPublicId()
    {
        // PublicId (T2 Guid AK) is the external identity.
        // Id (long) stays 0 in unit tests — assigned by DB.
        var def1 = CreateDefinition();
        var def2 = CreateDefinition();

        def1.PublicId.Should().NotBe(Guid.Empty);
        def2.PublicId.Should().NotBe(Guid.Empty);
        def1.PublicId.Should().NotBe(def2.PublicId);
    }

    [Fact]
    public void Create_SetsTenantId()
    {
        var id = Guid.NewGuid();
        var def = WorkflowDefinition.Create(TestGuidGenerator.Create(), "Flow", "Desc", id, ActorId);

        def.TenantId.Should().Be(id);
    }

    // --- State management ---

    [Fact]
    public void AddState_AppendsToStatesCollection()
    {
        var def = CreateDefinition();
        // WorkflowState.Create takes long DefinitionId (T1 PK); def.Id = 0 in unit test (no DB)
        var state = WorkflowState.Create(def.Id, "InReview", "Đang xét duyệt", "In Review",
            isInitial: true, isFinal: false, "#FF9900", 1);

        def.AddState(state);

        def.States.Should().ContainSingle()
            .Which.Name.Should().Be("InReview");
    }

    [Fact]
    public void AddState_MultipleStates_AllAppended()
    {
        var def = CreateDefinition();
        var s1 = WorkflowState.Create(def.Id, "Draft", "Nháp", "Draft", true, false, "#808080", 1);
        var s2 = WorkflowState.Create(def.Id, "Approved", "Đã duyệt", "Approved", false, true, "#00FF00", 2);

        def.AddState(s1);
        def.AddState(s2);

        def.States.Should().HaveCount(2);
    }

    // --- Transition management ---

    [Fact]
    public void AddTransition_AppendsToTransitionsCollection()
    {
        var def = CreateDefinition();
        // WorkflowTransition.Create takes long IDs (T1 PKs)
        const long fromId = 10L;
        const long toId = 20L;
        // S3 Slice 4: removed requiredPermissionCode param
        var transition = WorkflowTransition.Create(def.Id, fromId, toId,
            "Approve", "Phê duyệt", "Approve", null, 1);

        def.AddTransition(transition);

        def.Transitions.Should().ContainSingle()
            .Which.ActionName.Should().Be("Approve");
    }

    // --- Lifecycle ---

    [Fact]
    public void Activate_SetsIsActiveTrue()
    {
        var def = CreateDefinition();

        def.Activate(ActorId);

        def.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_AfterActivate_SetsIsActiveFalse()
    {
        var def = CreateDefinition();
        def.Activate(ActorId);

        def.Deactivate(ActorId);

        def.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Update_ChangesNameAndDescription()
    {
        var def = CreateDefinition("Old Name");

        def.Update("New Name", "New Description", ActorId);

        def.Name.Should().Be("New Name");
        def.Description.Should().Be("New Description");
    }

    // --- Domain events ---

    [Fact]
    public void AddDomainEvent_AppendsEvent()
    {
        var def = CreateDefinition();
        // WorkflowTransitionedEvent: (Guid InstancePublicId, long NewStateInternalId, Guid TransitionedBy)
        var evt = new WorkflowTransitionedEvent(Guid.NewGuid(), 10L, ActorId, Guid.NewGuid());

        def.AddDomainEvent(evt);

        def.DomainEvents.Should().ContainSingle();
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var def = CreateDefinition();
        def.AddDomainEvent(new WorkflowTransitionedEvent(Guid.NewGuid(), 10L, ActorId, Guid.NewGuid()));
        def.AddDomainEvent(new WorkflowTransitionedEvent(Guid.NewGuid(), 20L, ActorId, Guid.NewGuid()));

        def.ClearDomainEvents();

        def.DomainEvents.Should().BeEmpty();
    }
}
