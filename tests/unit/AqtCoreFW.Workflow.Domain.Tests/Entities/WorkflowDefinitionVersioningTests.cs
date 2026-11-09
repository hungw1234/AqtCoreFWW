using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Tests.Helpers;

namespace AqtCoreFW.Workflow.Domain.Tests.Entities;

/// <summary>
/// Unit tests for WorkflowDefinition versioning features.
/// T2 entity: PublicId (Guid AK), Id (long — 0 in unit tests without DB).
/// Clone requires IGuidGenerator to generate PublicId + new state/transition IDs.
/// State/Transition IDs are long (T1 PKs — 0 in unit tests without DB).
/// </summary>
public sealed class WorkflowDefinitionVersioningTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static WorkflowDefinition CreateDefinition(
        string name = "Flow", string desc = "Desc",
        string? definitionKey = null) =>
        WorkflowDefinition.Create(TestGuidGenerator.Create(), name, desc, TenantId, ActorId, definitionKey);

    // --- DefinitionKey & Version initialization ---

    [Fact]
    public void Create_SetsDefinitionKey_FromSlugifiedName()
    {
        var def = WorkflowDefinition.Create(
            TestGuidGenerator.Create(),
            "My Test Workflow", "Test", TenantId, ActorId);

        def.DefinitionKey.Should().Be("my-test-workflow");
    }

    [Fact]
    public void Create_WithExplicitKey_UsesProvidedKey()
    {
        var def = WorkflowDefinition.Create(
            TestGuidGenerator.Create(),
            "My Workflow", "Test", TenantId, ActorId,
            definitionKey: "custom-key");

        def.DefinitionKey.Should().Be("custom-key");
    }

    [Fact]
    public void Create_SetsVersion1_AndIsLatest()
    {
        var def = CreateDefinition();

        def.Version.Should().Be(1);
        def.IsLatest.Should().BeTrue();
    }

    // --- Clone behavior ---

    [Fact]
    public void Clone_IncrementsVersion()
    {
        var def = CreateDefinition();
        // Add dummy state so clone has something to copy
        def.AddState(WorkflowState.Create(def.Id, "Draft", "Nháp", "Draft", true, false, "#808080", 1));

        var clone = def.Clone(TestGuidGenerator.Create(), ActorId, def.Version + 1);

        clone.Version.Should().Be(2);
    }

    [Fact]
    public void Clone_SetsIsLatest_True()
    {
        var def = CreateDefinition();

        var clone = def.Clone(TestGuidGenerator.Create(), ActorId, def.Version + 1);

        clone.IsLatest.Should().BeTrue();
    }

    [Fact]
    public void Clone_DeepCopiesStates()
    {
        var def = CreateDefinition();
        var state1 = WorkflowState.Create(def.Id, "Draft", "Nháp", "Draft", true, false, "#808080", 1);
        var state2 = WorkflowState.Create(def.Id, "Approved", "Đã duyệt", "Approved", false, true, "#00FF00", 2);

        def.AddState(state1);
        def.AddState(state2);

        var clone = def.Clone(TestGuidGenerator.Create(), ActorId, def.Version + 1);

        clone.States.Should().HaveCount(2);
        // In unit tests state IDs are 0 (DB-assigned); just verify count and structure
        clone.States.Select(s => s.Name).Should().Contain("Draft").And.Contain("Approved");
    }

    [Fact]
    public void Clone_DoesNotCopyTransitions_TransitionsWiredByHandlerPostSave()
    {
        // BUG2-CLONE fix 2026-05-18: Clone() deliberately copies states ONLY.
        // Transition FK wiring (FromStateId/ToStateId) requires persisted state IDs (SQL Server
        // IDENTITY) which are assigned only on SaveChanges. The handler wires transitions in
        // Phase 2 after AddAsync. This test verifies the domain Clone() contract is states-only.
        var def = CreateDefinition();
        const long s1Id = 100L;
        const long s2Id = 200L;
        var s1 = WorkflowState.Create(def.Id, "Draft", "Nháp", "Draft", true, false, "#808080", 1);
        var s2 = WorkflowState.Create(def.Id, "Approved", "Đã duyệt", "Approved", false, true, "#00FF00", 2);
        def.AddState(s1);
        def.AddState(s2);

        // S3 Slice 4: removed requiredPermissionCode param
        var t1 = WorkflowTransition.Create(def.Id, s1Id, s2Id,
            "Approve", "Phê duyệt", "Approve", null, 1);
        def.AddTransition(t1);

        var clone = def.Clone(TestGuidGenerator.Create(), ActorId, def.Version + 1);

        // Clone has the states (2) but no transitions — handler wires them post-persist.
        clone.States.Should().HaveCount(2);
        clone.Transitions.Should().BeEmpty(
            "Clone() copies states only; transition FK wiring requires DB-assigned state IDs " +
            "and is performed by CloneWorkflowDefinitionCommandHandler after AddAsync.");
    }

    [Fact]
    public void Clone_DeepCopiesStateVisualDesignerPositions()
    {
        var def = CreateDefinition();
        def.AddState(WorkflowState.Create(
            def.Id, "Draft", "Nháp", "Draft", true, false, "#808080", 1,
            positionX: 120.5, positionY: 340.25));
        def.AddState(WorkflowState.Create(
            def.Id, "Approved", "Đã duyệt", "Approved", false, true, "#00FF00", 2,
            positionX: -42.0, positionY: 0.0));

        var clone = def.Clone(TestGuidGenerator.Create(), ActorId, def.Version + 1);

        var draft = clone.States.Single(s => s.Name == "Draft");
        draft.PositionX.Should().Be(120.5);
        draft.PositionY.Should().Be(340.25);

        var approved = clone.States.Single(s => s.Name == "Approved");
        approved.PositionX.Should().Be(-42.0);
        approved.PositionY.Should().Be(0.0);
    }

    [Fact]
    public void Clone_SourceHandles_WiredByHandler_CloneHasNoTransitionsFromDomainClone()
    {
        // BUG2-CLONE fix: Clone() copies states only; SourceHandle/TargetHandle are preserved
        // on the source transitions, and the handler copies them when re-creating transitions post-save.
        // This test verifies that Clone() (domain layer) produces no transitions — handler wires them.
        var def = CreateDefinition();
        def.AddState(WorkflowState.Create(def.Id, "Draft", "Nháp", "Draft", true, false, "#808080", 1));
        def.AddState(WorkflowState.Create(def.Id, "Approved", "Đã duyệt", "Approved", false, true, "#00FF00", 2));
        // S3 Slice 4: removed requiredPermissionCode param
        def.AddTransition(WorkflowTransition.Create(
            def.Id, 100L, 200L, "Approve", "Phê duyệt", "Approve", null, 1,
            sourceHandle: "right", targetHandle: "left"));

        var clone = def.Clone(TestGuidGenerator.Create(), ActorId, def.Version + 1);

        // Domain Clone() does NOT copy transitions (see BUG2-CLONE fix comment in WorkflowDefinition.Clone).
        // SourceHandle/TargetHandle fidelity is covered by CloneWorkflowDefinitionCommandHandlerTests
        // which exercises the full handler two-phase path including t.SourceHandle / t.TargetHandle copy.
        clone.Transitions.Should().BeEmpty(
            "domain Clone() copies states only; transition wiring (including SourceHandle/TargetHandle) " +
            "is performed by the handler after AddAsync assigns DB state IDs.");

        // Source transitions are preserved unchanged (source is not mutated by Clone).
        var srcT = def.Transitions.Single();
        srcT.SourceHandle.Should().Be("right");
        srcT.TargetHandle.Should().Be("left");
    }

    [Fact]
    public void Clone_DoesNotModifySource()
    {
        var def = CreateDefinition();
        def.AddState(WorkflowState.Create(def.Id, "Draft", "Nháp", "Draft", true, false, "#808080", 1));

        var originalStateCount = def.States.Count;
        var originalTransitionCount = def.Transitions.Count;
        var originalVersion = def.Version;
        var originalIsLatest = def.IsLatest;

        def.Clone(TestGuidGenerator.Create(), ActorId, def.Version + 1);

        // Source unchanged
        def.States.Count.Should().Be(originalStateCount);
        def.Transitions.Count.Should().Be(originalTransitionCount);
        def.Version.Should().Be(originalVersion);
        def.IsLatest.Should().Be(originalIsLatest);
    }

    [Fact]
    public void Clone_PreservesDefinitionKey()
    {
        var def = CreateDefinition(definitionKey: "my-key");

        var clone = def.Clone(TestGuidGenerator.Create(), ActorId, def.Version + 1);

        clone.DefinitionKey.Should().Be("my-key");
    }

    [Fact]
    public void Clone_PreservesName_And_Description()
    {
        var def = CreateDefinition("Test Workflow", "Test Description");

        var clone = def.Clone(TestGuidGenerator.Create(), ActorId, def.Version + 1);

        clone.Name.Should().Be("Test Workflow");
        clone.Description.Should().Be("Test Description");
    }

    [Fact]
    public void Clone_CreatedBySetCorrectly()
    {
        var def = CreateDefinition();
        var newActor = Guid.NewGuid();

        var clone = def.Clone(TestGuidGenerator.Create(), newActor, def.Version + 1);

        clone.CreatedBy.Should().Be(newActor);
    }

    // --- MarkNotLatest ---

    [Fact]
    public void MarkNotLatest_SetsIsLatestFalse()
    {
        var def = CreateDefinition();
        def.IsLatest.Should().BeTrue();

        def.MarkNotLatest(ActorId);

        def.IsLatest.Should().BeFalse();
    }

    [Fact]
    public void MarkNotLatest_UpdatesAuditTimestamp()
    {
        var def = CreateDefinition();
        def.UpdatedAt.Should().BeNull();  // Initially no update timestamp

        // Small delay to ensure timestamp changes
        System.Threading.Thread.Sleep(10);

        def.MarkNotLatest(ActorId);

        def.UpdatedAt.Should().NotBeNull();
    }

    // --- TenantId isolation ---

    [Fact]
    public void Create_PreservesTenantIdForVersioning()
    {
        var tenantA = Guid.NewGuid();
        var def = WorkflowDefinition.Create(TestGuidGenerator.Create(), "Flow", "Desc", tenantA, ActorId);

        var clone = def.Clone(TestGuidGenerator.Create(), ActorId, def.Version + 1);

        clone.TenantId.Should().Be(tenantA);
    }
}
