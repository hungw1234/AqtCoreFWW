using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Domain.Tests.Entities;

/// <summary>
/// Unit tests for WorkflowState value entity.
/// T1 entity: long Id (DB-assigned — 0 in unit tests without DB).
/// DefinitionId: long (FK to T2 WorkflowDefinition.Id).
/// Covers factory method, and flag assignments.
/// </summary>
public sealed class WorkflowStateTests
{
    // DefinitionId is long (T2 WorkflowDefinition.Id FK)
    private const long DefinitionId = 1L;

    [Fact]
    public void Create_SetsAllProperties()
    {
        var state = WorkflowState.Create(
            DefinitionId, "InReview", "Đang xét duyệt", "In Review",
            isInitial: false, isFinal: false, "#FF9900", sortOrder: 3);

        state.DefinitionId.Should().Be(DefinitionId);
        state.Name.Should().Be("InReview");
        state.DisplayNameVi.Should().Be("Đang xét duyệt");
        state.DisplayNameEn.Should().Be("In Review");
        state.IsInitial.Should().BeFalse();
        state.IsFinal.Should().BeFalse();
        state.Color.Should().Be("#FF9900");
        state.SortOrder.Should().Be(3);
    }

    [Fact]
    public void Create_IdIsZeroInUnitTests_DbAssigned()
    {
        // T1 entity: Id (long) is DB-assigned (IDENTITY). Unit tests get 0.
        // Two states created without DB share Id=0 — use Name to distinguish.
        var s1 = WorkflowState.Create(DefinitionId, "Draft", "Nháp", "Draft", true, false, "#808080", 1);
        var s2 = WorkflowState.Create(DefinitionId, "Approved", "Đã duyệt", "Approved", false, true, "#00FF00", 2);

        s1.Name.Should().NotBe(s2.Name);
        // Id is 0 for both in unit tests — DB assigns sequential bigint on insert
        s1.Id.Should().Be(0L);
        s2.Id.Should().Be(0L);
    }

    [Fact]
    public void Create_SetsIsInitialAndIsFinalFlags()
    {
        var initial = WorkflowState.Create(DefinitionId, "Draft", "Nháp", "Draft",
            isInitial: true, isFinal: false, "#808080", 1);
        var terminal = WorkflowState.Create(DefinitionId, "Closed", "Đóng", "Closed",
            isInitial: false, isFinal: true, "#00FF00", 99);

        initial.IsInitial.Should().BeTrue();
        initial.IsFinal.Should().BeFalse();

        terminal.IsInitial.Should().BeFalse();
        terminal.IsFinal.Should().BeTrue();
    }
}
