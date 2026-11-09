using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Domain.Tests.Entities;

/// <summary>
/// Unit tests for WorkflowTransition value entity.
/// T1 entity: long Id (DB-assigned — 0 in unit tests without DB).
/// DefinitionId/FromStateId/ToStateId: long (T1 bigint PKs).
/// Covers factory method, state ID wiring, and optional conditions.
/// NOTE: RequiredPermissionCode was removed in S3 cutover Slice 4; handler authz gate is in Slice 5.
/// </summary>
public sealed class WorkflowTransitionTests
{
    // All IDs are long (T1 entity PKs)
    private const long DefinitionId = 1L;
    private const long FromStateId = 10L;
    private const long ToStateId = 20L;

    [Fact]
    public void Create_SetsFromAndToStateIds()
    {
        var transition = WorkflowTransition.Create(
            DefinitionId, FromStateId, ToStateId,
            "Approve", "Phê duyệt", "Approve",
            conditionsJson: null, sortOrder: 1);

        transition.FromStateId.Should().Be(FromStateId);
        transition.ToStateId.Should().Be(ToStateId);
        transition.DefinitionId.Should().Be(DefinitionId);
    }

    [Fact]
    public void Create_IdIsZeroInUnitTests_DbAssigned()
    {
        // T1 entity: Id (long) is DB-assigned (IDENTITY). Unit tests get 0.
        var t1 = WorkflowTransition.Create(DefinitionId, FromStateId, ToStateId,
            "Approve", "Phê duyệt", "Approve", null, 1);
        var t2 = WorkflowTransition.Create(DefinitionId, FromStateId, ToStateId,
            "Approve", "Phê duyệt", "Approve", null, 1);

        t1.Id.Should().Be(0L);
        t2.Id.Should().Be(0L);
    }

    [Fact]
    public void Create_SetsActionName()
    {
        var transition = WorkflowTransition.Create(
            DefinitionId, FromStateId, ToStateId,
            "Reject", "Từ chối", "Reject",
            conditionsJson: null, sortOrder: 2);

        transition.ActionName.Should().Be("Reject");
    }

    [Fact]
    public void Create_SetsConditionsJsonWhenProvided()
    {
        const string conditions = """{"Status":["Active"]}""";
        var transition = WorkflowTransition.Create(
            DefinitionId, FromStateId, ToStateId,
            "Approve", "Phê duyệt", "Approve",
            conditionsJson: conditions, sortOrder: 1);

        transition.ConditionsJson.Should().Be(conditions);
    }

    [Fact]
    public void Create_NullConditionsJson_IsNull()
    {
        var transition = WorkflowTransition.Create(
            DefinitionId, FromStateId, ToStateId,
            "Submit", "Nộp", "Submit",
            conditionsJson: null, sortOrder: 1);

        transition.ConditionsJson.Should().BeNull();
    }

    [Fact]
    public void Create_InvalidFromStateId_Throws()
    {
        var act = () => WorkflowTransition.Create(
            DefinitionId, fromStateId: 0, ToStateId,
            "Approve", "Phê duyệt", "Approve", null, 1);

        act.Should().Throw<ArgumentException>().WithParameterName("fromStateId");
    }

    [Fact]
    public void Create_InvalidToStateId_Throws()
    {
        var act = () => WorkflowTransition.Create(
            DefinitionId, FromStateId, toStateId: -1,
            "Approve", "Phê duyệt", "Approve", null, 1);

        act.Should().Throw<ArgumentException>().WithParameterName("toStateId");
    }
}
