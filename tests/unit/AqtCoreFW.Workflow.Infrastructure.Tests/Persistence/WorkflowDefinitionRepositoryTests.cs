using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace AqtCoreFW.Workflow.Infrastructure.Tests.Persistence;

/// <summary>
/// TC-WF-I003: WorkflowDefinitionRepository persists and retrieves WorkflowDefinition with
/// states and transitions intact (EF Core in-memory database).
///
/// Phase-4b B6:
///   - WorkflowDefinition.Create requires IGuidGenerator as first param (T2 entity).
///   - WorkflowState.Create takes long definitionId (T2 internal PK, available after SaveChangesAsync).
///   - WorkflowTransition.Create takes long definitionId, fromStateId, toStateId (T1 long PKs).
///   - GetWithStatesAndTransitionsAsync takes Guid publicId (T2 AK).
///   - Save defs first, then build states (so definition.Id is DB-assigned long).
/// </summary>
public sealed class WorkflowDefinitionRepositoryTests : IDisposable
{
    private readonly WorkflowDbContext _ctx;
    private readonly WorkflowDefinitionRepository _sut;
    private readonly TestGuidGenerator _gen = TestGuidGenerator.Create();

    public WorkflowDefinitionRepositoryTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(Guid.Empty);

        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase($"WorkflowDefTests_{Guid.NewGuid()}")
            .Options;

        _ctx = new WorkflowDbContext(options, tenantCtx);
        _sut = new WorkflowDefinitionRepository(_ctx);
    }

    public void Dispose() => _ctx.Dispose();

    /// <summary>
    /// WorkflowState.Create: first param is long definitionId (T2 WorkflowDefinition.Id bigint PK).
    /// Must be called after SaveChangesAsync so definition.Id is DB-assigned.
    /// </summary>
    private static WorkflowState MakeState(long definitionId, string name, string displayVi, bool isInitial) =>
        WorkflowState.Create(definitionId, name, displayVi, name, isInitial, false, "#808080", 0);

    /// <summary>
    /// WorkflowTransition.Create: definitionId, fromId, toId are all long (T1/T2 internal PKs).
    /// Must be called after SaveChangesAsync for states so their IDs are DB-assigned.
    /// </summary>
    private static WorkflowTransition MakeTransition(
        long definitionId, long fromId, long toId, string actionName) =>
        WorkflowTransition.Create(definitionId, fromId, toId, actionName, actionName, actionName,
            conditionsJson: null, sortOrder: 0);

    [Fact]
    public async Task GetWithStatesAndTransitionsAsync_ExistingDefinition_ReturnsWithStates()
    {
        // TC-WF-I003: definition with two states → repository returns both states intact
        var createdBy = Guid.NewGuid();
        var definition = WorkflowDefinition.Create(_gen, "Quy trinh 1", "Mo ta", Guid.NewGuid(), createdBy);

        // Save definition first — EF InMemory assigns long Id (bigint PK)
        _ctx.WorkflowDefinitions.Add(definition);
        await _ctx.SaveChangesAsync();

        // States use definition.Id (long) now available after save
        var stateA = MakeState(definition.Id, "TiepNhan", "Tiep nhan", isInitial: true);
        var stateB = MakeState(definition.Id, "XuLy", "Xu ly", isInitial: false);
        definition.AddState(stateA);
        definition.AddState(stateB);
        await _ctx.SaveChangesAsync();

        // GetWithStatesAndTransitionsAsync uses Guid publicId (T2 AK)
        var result = await _sut.GetWithStatesAndTransitionsAsync(definition.PublicId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(definition.Id);
        result.Value.Name.Should().Be("Quy trinh 1");
        result.Value.States.Should().HaveCount(2);
        result.Value.States.Select(s => s.Name).Should().Contain(["TiepNhan", "XuLy"]);
    }

    [Fact]
    public async Task GetWithStatesAndTransitionsAsync_NotFound_ReturnsFail()
    {
        // TC-WF-I003: non-existent PublicId → result is failed
        var result = await _sut.GetWithStatesAndTransitionsAsync(Guid.NewGuid());

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task GetWithStatesAndTransitionsAsync_WithTransition_ReturnsTransition()
    {
        // TC-WF-I003: definition with a transition → repository returns transition
        var createdBy = Guid.NewGuid();
        var definition = WorkflowDefinition.Create(_gen, "Quy trinh 2", "Mo ta 2", Guid.NewGuid(), createdBy);

        _ctx.WorkflowDefinitions.Add(definition);
        await _ctx.SaveChangesAsync();

        var stateA = MakeState(definition.Id, "TiepNhan", "Tiep nhan", isInitial: true);
        var stateB = MakeState(definition.Id, "HoanTat", "Hoan tat", isInitial: false);
        definition.AddState(stateA);
        definition.AddState(stateB);
        await _ctx.SaveChangesAsync();

        // Transition uses long IDs — available after state save
        var transition = MakeTransition(definition.Id, stateA.Id, stateB.Id, "Duyet");
        definition.AddTransition(transition);
        await _ctx.SaveChangesAsync();

        var result = await _sut.GetWithStatesAndTransitionsAsync(definition.PublicId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Transitions.Should().HaveCount(1);
        result.Value.Transitions[0].ActionName.Should().Be("Duyet");
    }

    /// <summary>
    /// TC-WF-I003-J: GetOutgoingWithAssignmentsAsync returns one OutgoingAssignment per
    /// outgoing transition. Transitions with an assignment rule include it; transitions
    /// without a rule have Assignment == null. Clauses are eagerly loaded.
    /// Phase-3 Slice-5: validates the 2-query + in-memory left-join repo impl.
    /// </summary>
    [Fact]
    public async Task GetOutgoingWithAssignmentsAsync_returns_pairs_incl_null_assignment_clauses_eager()
    {
        // Arrange: definition + two states + two transitions from stateA
        var createdBy = Guid.NewGuid();
        var definition = WorkflowDefinition.Create(_gen, "GetOutgoing Test", "desc", Guid.NewGuid(), createdBy);
        _ctx.WorkflowDefinitions.Add(definition);
        await _ctx.SaveChangesAsync();

        var stateA = MakeState(definition.Id, "StateA", "State A", isInitial: true);
        var stateB = MakeState(definition.Id, "StateB", "State B", isInitial: false);
        var stateC = MakeState(definition.Id, "StateC", "State C", isInitial: false);
        definition.AddState(stateA);
        definition.AddState(stateB);
        definition.AddState(stateC);
        await _ctx.SaveChangesAsync();

        // Transition A→B (will get an assignment rule) and A→C (no assignment rule)
        var tAB = MakeTransition(definition.Id, stateA.Id, stateB.Id, "ToB");
        var tAC = MakeTransition(definition.Id, stateA.Id, stateC.Id, "ToC");
        definition.AddTransition(tAB);
        definition.AddTransition(tAC);
        await _ctx.SaveChangesAsync();

        // Add an assignment rule for A→B only, with one ByPermission clause
        var asg = WorkflowTransitionAssignment.Create(tAB.Id, AssignmentCombineMode.Union);
        asg.AddClause(AssignmentClauseMode.ByPermission, """["CASES.WORKFLOW.APPROVE"]""");
        _ctx.Set<WorkflowTransitionAssignment>().Add(asg);
        await _ctx.SaveChangesAsync();

        // Act
        var result = await _sut.GetOutgoingWithAssignmentsAsync(stateA.Id);

        // Assert
        result.Should().HaveCount(2, "two transitions from stateA");

        var withRule = result.FirstOrDefault(o => o.Transition.ActionName == "ToB");
        withRule.Should().NotBeNull("A→B has an assignment rule");
        withRule!.Assignment.Should().NotBeNull("assignment was configured");
        withRule.Assignment!.Clauses.Should().HaveCount(1, "one clause added");

        var noRule = result.FirstOrDefault(o => o.Transition.ActionName == "ToC");
        noRule.Should().NotBeNull("A→C has no assignment rule");
        noRule!.Assignment.Should().BeNull("no assignment configured for A→C");
    }

    [Fact]
    public async Task WorkflowDefinition_Activate_SetsIsActiveTrueAndPersists()
    {
        // TC-WF-I003: Activate changes IsActive and is persisted via EF
        var createdBy = Guid.NewGuid();
        var definition = WorkflowDefinition.Create(_gen, "Quy trinh 3", "Mo ta 3", Guid.NewGuid(), createdBy);
        definition.IsActive.Should().BeFalse();

        _ctx.WorkflowDefinitions.Add(definition);
        await _ctx.SaveChangesAsync();

        definition.Activate(createdBy);
        await _ctx.SaveChangesAsync();

        var reloaded = await _ctx.WorkflowDefinitions.FindAsync(definition.Id);
        reloaded!.IsActive.Should().BeTrue();
    }
}
