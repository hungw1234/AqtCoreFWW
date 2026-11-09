using System.Text.Json;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Services;

/// <summary>
/// Model B resolver (Phase-2, plan 260519-0421 v2 + addendum).
/// Pure function: target state + pre-loaded outgoing (transition, assignment?) set + context.
/// MVP modes SpecificUser/Initiator/ByPermission impl; the other 4 + Intersect throw.
/// Candidate model is binary: exactly-one-user ⇒ Assignee XOR predicate pool XOR IsEmpty
/// (addendum §3 — multi-user / mixed has no MVP producer ⇒ NotSupported).
/// </summary>
public sealed class WorkflowHandlerResolverTests
{
    private const long DefId = 100L;
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Initiator = Guid.NewGuid();

    // RT2-15 Phase 01: resolver now depends on IWorkflowTaskRepository for PreviousHandler mode.
    // Tests in this file cover SpecificUser / Initiator / ByPermission / non-deferred edge cases
    // — none exercise PreviousHandler — so a no-op stubbed repo suffices. PreviousHandler mode
    // coverage lives in RT2_15_PreviousHandlerProducerTests integration suite.
    private readonly IWorkflowTaskRepository _taskRepository = Substitute.For<IWorkflowTaskRepository>();
    private readonly IWorkflowHandlerResolver _sut;

    public WorkflowHandlerResolverTests()
    {
        _sut = new WorkflowHandlerResolver(_taskRepository);
    }

    // ── helpers ────────────────────────────────────────────────────────────────
    private static void SetId(Entity e, long id) =>
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(e, id);

    private static WorkflowState State(long id, long? autoTimeoutId = null)
    {
        var s = WorkflowState.Create(DefId, $"S{id}", "vi", "en", false, false, "#fff", 0,
            autoTransitionOnTimeoutId: autoTimeoutId);
        SetId(s, id);
        return s;
    }

    private static WorkflowTransition Transition(long id, long toStateId, bool isRecall = false)
    {
        // S3 Slice 4: removed requiredPermissionCode param
        var t = WorkflowTransition.Create(DefId, 1L, toStateId, "Act", "vi", "en",
            null, 0, isRecall: isRecall);
        SetId(t, id);
        return t;
    }

    private static WorkflowTransitionAssignment Assignment(
        long transitionId,
        AssignmentClauseMode mode,
        string? refsJson,
        AssignmentCombineMode combine = AssignmentCombineMode.Union)
    {
        var a = WorkflowTransitionAssignment.Create(transitionId, combine);
        a.AddClause(mode, refsJson);
        return a;
    }

    private async Task<HandlerResolution> ResolveAsync(
        WorkflowState target,
        IReadOnlyList<OutgoingAssignment> outgoing) =>
        await _sut.ResolveAsync(target, outgoing, new WorkflowResolutionContext(Tenant, Initiator));

    // RT2-15 Phase 01 — PreviousHandler mode requires a non-zero WorkflowInstanceId in context.
    private async Task<HandlerResolution> ResolveAsync(
        WorkflowState target,
        IReadOnlyList<OutgoingAssignment> outgoing,
        long workflowInstanceId) =>
        await _sut.ResolveAsync(target, outgoing,
            new WorkflowResolutionContext(Tenant, Initiator, workflowInstanceId));

    private static string JsonArr(params string[] v) => JsonSerializer.Serialize(v);

    // ── SpecificUser / Initiator → Assignee ────────────────────────────────────
    [Fact]
    public async Task SpecificUser_SingleId_ResolvesToAssignee()
    {
        var target = State(2);
        var user = Guid.NewGuid();
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.SpecificUser, JsonArr(user.ToString()));

        var r = await ResolveAsync(target, [new OutgoingAssignment(t, a)]);

        r.Assignee.Should().Be(user);
        r.CandidatePredicates.Should().BeEmpty();
        r.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task Initiator_ResolvesToInitiatorAsAssignee()
    {
        var target = State(2);
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.Initiator, null);

        (await ResolveAsync(target, [new OutgoingAssignment(t, a)])).Assignee.Should().Be(Initiator);
    }

    // ── ByPermission → predicate pool ──────────────────────────────────────────
    [Fact]
    public async Task ByPermission_ResolvesToCandidatePredicatePool()
    {
        var target = State(2);
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.ByPermission, JsonArr("WORKFLOW.TASK.PROCESS"));

        var r = await ResolveAsync(target, [new OutgoingAssignment(t, a)]);

        r.Assignee.Should().BeNull();
        r.CandidatePredicates.Should().ContainSingle(p =>
            p.Type == CandidatePrincipalType.Permission && p.Ref == "WORKFLOW.TASK.PROCESS");
        r.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task MultiOutgoing_UnionsPredicatesAcrossTransitions()
    {
        var target = State(2);
        var o1 = new OutgoingAssignment(Transition(10, 2),
            Assignment(10, AssignmentClauseMode.ByPermission, JsonArr("P.A")));
        var o2 = new OutgoingAssignment(Transition(11, 3),
            Assignment(11, AssignmentClauseMode.ByPermission, JsonArr("P.B")));

        var r = await ResolveAsync(target, [o1, o2]);

        r.CandidatePredicates.Select(p => p.Ref).Should().BeEquivalentTo(["P.A", "P.B"]);
    }

    [Fact]
    public async Task DuplicatePermissionAcrossTransitions_DedupedByUnion()
    {
        var target = State(2);
        var o1 = new OutgoingAssignment(Transition(10, 2),
            Assignment(10, AssignmentClauseMode.ByPermission, JsonArr("P.SAME")));
        var o2 = new OutgoingAssignment(Transition(11, 3),
            Assignment(11, AssignmentClauseMode.ByPermission, JsonArr("P.SAME")));

        (await ResolveAsync(target, [o1, o2])).CandidatePredicates.Should().ContainSingle();
    }

    // ── exclusions ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task AutoTimeoutTransition_ExcludedFromPool()
    {
        var target = State(2, autoTimeoutId: 99);
        var auto = new OutgoingAssignment(Transition(99, 5),
            Assignment(99, AssignmentClauseMode.ByPermission, JsonArr("P.AUTO")));
        var normal = new OutgoingAssignment(Transition(10, 2),
            Assignment(10, AssignmentClauseMode.ByPermission, JsonArr("P.NORMAL")));

        var r = await ResolveAsync(target, [auto, normal]);

        r.CandidatePredicates.Should().ContainSingle(p => p.Ref == "P.NORMAL");
    }

    [Fact]
    public async Task RecallTransition_ExcludedFromPool()
    {
        var target = State(2);
        var recall = new OutgoingAssignment(Transition(20, 1, isRecall: true),
            Assignment(20, AssignmentClauseMode.ByPermission, JsonArr("P.RECALL")));
        var normal = new OutgoingAssignment(Transition(10, 2),
            Assignment(10, AssignmentClauseMode.ByPermission, JsonArr("P.NORMAL")));

        (await ResolveAsync(target, [recall, normal]))
            .CandidatePredicates.Should().ContainSingle(p => p.Ref == "P.NORMAL");
    }

    // ── empty ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task NoOutgoing_IsEmpty()
    {
        (await ResolveAsync(State(2), [])).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task OutgoingWithoutAssignment_IsEmpty()
    {
        var r = await ResolveAsync(State(2), [new OutgoingAssignment(Transition(10, 2), null)]);

        r.IsEmpty.Should().BeTrue();
        r.Assignee.Should().BeNull();
        r.CandidatePredicates.Should().BeEmpty();
    }

    [Fact]
    public async Task ByPermission_EmptyRefsArray_ContributesNothing_IsEmpty()
    {
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.ByPermission, "[]");

        (await ResolveAsync(State(2), [new OutgoingAssignment(t, a)])).IsEmpty.Should().BeTrue();
    }

    // ── fail-loud: deferred modes / Intersect / unsupported combos ──────────────
    // RT2-15 Phase 01 (2026-05-24): PreviousHandler shipped — removed from this Theory.
    // RT2-15 Phase 03 (2026-05-24): ByRole shipped as predicate-pool — removed from this Theory.
    // RT2-15 Phase 02 (2026-05-25): ByGroup shipped as predicate-pool — removed from this Theory.
    // RT2-15 Phase 04 (2026-05-26): ByOrgUnit shipped as predicate-pool — test converted below.
    // All MVP modes now implemented; Intersect combine remains the only fail-loud stub.

    // ── ByOrgUnit (mode 5) — RT2-15 Phase 04 predicate-pool producer ──────────────
    // PQ-3: resolve to CandidatePredicate(OrgUnit, code) — mirror ByGroup/ByRole pattern.
    // Predicate-pool path (Option A): push predicates, NOT eager user-resolution.

    [Fact]
    public async Task ByOrgUnit_SingleCode_ResolvesToOrgUnitPredicatePool()
    {
        var target = State(2);
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.ByOrgUnit, JsonArr("OU_PHONG_TIEP_NHAN"));

        var r = await ResolveAsync(target, [new OutgoingAssignment(t, a)]);

        r.Assignee.Should().BeNull();
        r.CandidatePredicates.Should().ContainSingle(p =>
            p.Type == CandidatePrincipalType.OrgUnit && p.Ref == "OU_PHONG_TIEP_NHAN");
    }

    [Fact]
    public async Task ByOrgUnit_MultipleCodes_EachBecomesPredicateEntry()
    {
        var target = State(2);
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.ByOrgUnit, JsonArr("OU_BAN_GIAM_DOC", "OU_PHONG_KE_TOAN"));

        var r = await ResolveAsync(target, [new OutgoingAssignment(t, a)]);

        r.Assignee.Should().BeNull();
        r.CandidatePredicates.Should().HaveCount(2);
        r.CandidatePredicates.Should().Contain(p => p.Type == CandidatePrincipalType.OrgUnit && p.Ref == "OU_BAN_GIAM_DOC");
        r.CandidatePredicates.Should().Contain(p => p.Type == CandidatePrincipalType.OrgUnit && p.Ref == "OU_PHONG_KE_TOAN");
    }

    // ── ByRole (mode 4) — RT2-15 Phase 03 predicate-pool producer ──────────────
    // PQ-1 A: resolve to CandidatePredicate(Role, roleName) — mirror ByPermission
    // semantic. Claim/inbox read-side extension shipped via T3-WF-CANDIDATEINDEX-
    // READSIDE-EXTENSION (P0). Until that lands, ByRole-pooled tasks are unclaimable —
    // do NOT seed ByRole assignments in production-touching test seeders.

    [Fact]
    public async Task ByRole_SingleRole_ResolvesToRolePredicatePool()
    {
        var target = State(2);
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.ByRole, JsonArr("ROLE_REVIEWER"));

        var r = await ResolveAsync(target, [new OutgoingAssignment(t, a)]);

        r.Assignee.Should().BeNull();
        r.CandidatePredicates.Should().ContainSingle(p =>
            p.Type == CandidatePrincipalType.Role && p.Ref == "ROLE_REVIEWER");
        r.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task ByRole_MultipleRoles_UnionsPredicates()
    {
        var target = State(2);
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.ByRole, JsonArr("ROLE_A", "ROLE_B"));

        var r = await ResolveAsync(target, [new OutgoingAssignment(t, a)]);

        r.CandidatePredicates.Select(p => p.Ref).Should().BeEquivalentTo(["ROLE_A", "ROLE_B"]);
        r.CandidatePredicates.Should().OnlyContain(p => p.Type == CandidatePrincipalType.Role);
    }

    [Fact]
    public async Task ByRole_DuplicateAcrossTransitions_DedupedByUnion()
    {
        var target = State(2);
        var o1 = new OutgoingAssignment(Transition(10, 2),
            Assignment(10, AssignmentClauseMode.ByRole, JsonArr("ROLE_SAME")));
        var o2 = new OutgoingAssignment(Transition(11, 3),
            Assignment(11, AssignmentClauseMode.ByRole, JsonArr("ROLE_SAME")));

        (await ResolveAsync(target, [o1, o2])).CandidatePredicates.Should().ContainSingle();
    }

    [Fact]
    public async Task ByRole_EmptyRefsArray_ContributesNothing_IsEmpty()
    {
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.ByRole, "[]");

        (await ResolveAsync(State(2), [new OutgoingAssignment(t, a)])).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task ByRole_MixedWithByPermission_BothPredicatesPresent()
    {
        // Anti-tautology lock: two-mode union proves Role + Permission predicates coexist
        // in the same pool — not mutually exclusive code paths.
        var target = State(2);
        var aRole = Assignment(10, AssignmentClauseMode.ByRole, JsonArr("ROLE_X"));
        var aPerm = Assignment(11, AssignmentClauseMode.ByPermission, JsonArr("P.Y"));

        var r = await ResolveAsync(target, [
            new OutgoingAssignment(Transition(10, 2), aRole),
            new OutgoingAssignment(Transition(11, 3), aPerm)
        ]);

        r.CandidatePredicates.Should().HaveCount(2);
        r.CandidatePredicates.Should().Contain(p =>
            p.Type == CandidatePrincipalType.Role && p.Ref == "ROLE_X");
        r.CandidatePredicates.Should().Contain(p =>
            p.Type == CandidatePrincipalType.Permission && p.Ref == "P.Y");
    }

    // ── ByGroup (mode 5) — RT2-15 Phase 02 predicate-pool producer ───────────
    // PQ-2 B: resolve to CandidatePredicate(Group, groupCode) — mirror ByRole pattern.
    // READ-SIDE GAP: claim/inbox filter Permission+Role only; Group-pooled tasks unclaimable
    // until T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP (P0 CRITICAL) ships.

    [Fact]
    public async Task ByGroup_SingleGroup_ResolvesToGroupPredicatePool()
    {
        var target = State(2);
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.ByGroup, JsonArr("GROUP_FINANCE"));

        var r = await ResolveAsync(target, [new OutgoingAssignment(t, a)]);

        r.Assignee.Should().BeNull();
        r.CandidatePredicates.Should().ContainSingle(p =>
            p.Type == CandidatePrincipalType.Group && p.Ref == "GROUP_FINANCE");
        r.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task ByGroup_MultipleGroups_UnionsPredicates()
    {
        var target = State(2);
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.ByGroup, JsonArr("GROUP_A", "GROUP_B"));

        var r = await ResolveAsync(target, [new OutgoingAssignment(t, a)]);

        r.CandidatePredicates.Select(p => p.Ref).Should().BeEquivalentTo(["GROUP_A", "GROUP_B"]);
        r.CandidatePredicates.Should().OnlyContain(p => p.Type == CandidatePrincipalType.Group);
    }

    [Fact]
    public async Task ByGroup_DuplicateAcrossTransitions_DedupedByUnion()
    {
        var target = State(2);
        var o1 = new OutgoingAssignment(Transition(10, 2),
            Assignment(10, AssignmentClauseMode.ByGroup, JsonArr("GROUP_SAME")));
        var o2 = new OutgoingAssignment(Transition(11, 3),
            Assignment(11, AssignmentClauseMode.ByGroup, JsonArr("GROUP_SAME")));

        (await ResolveAsync(target, [o1, o2])).CandidatePredicates.Should().ContainSingle();
    }

    [Fact]
    public async Task ByGroup_EmptyRefsArray_ContributesNothing_IsEmpty()
    {
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.ByGroup, "[]");

        (await ResolveAsync(State(2), [new OutgoingAssignment(t, a)])).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task ByGroup_MixedWithByRoleAndByPermission_AllPredicatesPresent()
    {
        // Anti-tautology lock: three-mode union proves Group + Role + Permission predicates
        // coexist in the same pool — not mutually exclusive code paths.
        var target = State(2);
        var aGroup = Assignment(10, AssignmentClauseMode.ByGroup, JsonArr("GROUP_X"));
        var aRole = Assignment(11, AssignmentClauseMode.ByRole, JsonArr("ROLE_Y"));
        var aPerm = Assignment(12, AssignmentClauseMode.ByPermission, JsonArr("P.Z"));

        var r = await ResolveAsync(target, [
            new OutgoingAssignment(Transition(10, 2), aGroup),
            new OutgoingAssignment(Transition(11, 3), aRole),
            new OutgoingAssignment(Transition(12, 4), aPerm)
        ]);

        r.CandidatePredicates.Should().HaveCount(3);
        r.CandidatePredicates.Should().Contain(p =>
            p.Type == CandidatePrincipalType.Group && p.Ref == "GROUP_X");
        r.CandidatePredicates.Should().Contain(p =>
            p.Type == CandidatePrincipalType.Role && p.Ref == "ROLE_Y");
        r.CandidatePredicates.Should().Contain(p =>
            p.Type == CandidatePrincipalType.Permission && p.Ref == "P.Z");
    }

    // ── PreviousHandler (mode 2) — RT2-15 Phase 01 producer ──────────────────

    [Fact]
    public async Task PreviousHandler_WithPriorCompletor_ResolvesToCompletor()
    {
        var instanceId = 123L;
        var completor = Guid.NewGuid();
        var priorTask = WorkflowTask.Create(
            workflowInstanceId: instanceId, title: "act", description: null,
            assigneeId: completor, priority: WorkflowTaskPriority.Normal, dueDate: null,
            tenantId: Tenant, createdBy: Initiator);
        priorTask.Complete(completor);
        _taskRepository
            .GetLastCompletedTaskAsync(instanceId, Arg.Any<CancellationToken>())
            .Returns(priorTask);

        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.PreviousHandler, null);

        var r = await ResolveAsync(State(2), [new OutgoingAssignment(t, a)], instanceId);

        r.Assignee.Should().Be(completor,
            "PreviousHandler must resolve to the user who completed the most-recent prior task");
        await _taskRepository.Received(1)
            .GetLastCompletedTaskAsync(instanceId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviousHandler_WithZeroInstanceId_NoRepoCall_ResolvesEmpty()
    {
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.PreviousHandler, null);

        var r = await ResolveAsync(State(2), [new OutgoingAssignment(t, a)], workflowInstanceId: 0L);

        r.IsEmpty.Should().BeTrue(
            "InstanceId=0 (first-task-on-create path) → no repo lookup → empty pool → fail-CLOSED");
        await _taskRepository.DidNotReceiveWithAnyArgs()
            .GetLastCompletedTaskAsync(default, default);
    }

    [Fact]
    public async Task PreviousHandler_WithNoPriorCompletedTask_ResolvesEmpty()
    {
        var instanceId = 456L;
        _taskRepository
            .GetLastCompletedTaskAsync(instanceId, Arg.Any<CancellationToken>())
            .Returns((WorkflowTask?)null);

        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.PreviousHandler, null);

        var r = await ResolveAsync(State(2), [new OutgoingAssignment(t, a)], instanceId);

        r.IsEmpty.Should().BeTrue(
            "no prior completed task on instance → empty pool → fail-CLOSED");
    }

    [Fact]
    public async Task PreviousHandler_PriorTaskWithNullCompletedBy_ResolvesEmpty()
    {
        var instanceId = 789L;
        // Build a task that's still in OPEN state (CompletedBy is null until Complete() is called)
        var orphanTask = WorkflowTask.Create(
            workflowInstanceId: instanceId, title: "act", description: null,
            assigneeId: Guid.NewGuid(), priority: WorkflowTaskPriority.Normal, dueDate: null,
            tenantId: Tenant, createdBy: Initiator);
        _taskRepository
            .GetLastCompletedTaskAsync(instanceId, Arg.Any<CancellationToken>())
            .Returns(orphanTask);

        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.PreviousHandler, null);

        var r = await ResolveAsync(State(2), [new OutgoingAssignment(t, a)], instanceId);

        r.IsEmpty.Should().BeTrue(
            "task without CompletedBy (orphan / pre-completion) contributes no user → empty pool");
    }

    [Fact]
    public async Task IntersectCombineMode_ThrowsNotSupported()
    {
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.ByPermission, JsonArr("P.A"),
            combine: AssignmentCombineMode.Intersect);

        var act = async () => await ResolveAsync(State(2), [new OutgoingAssignment(t, a)]);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task MultipleConcreteUsers_NoMvpProducer_ThrowsNotSupported()
    {
        var t = Transition(10, 2);
        var a = Assignment(10, AssignmentClauseMode.SpecificUser,
            JsonArr(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()));

        var act = async () => await ResolveAsync(State(2), [new OutgoingAssignment(t, a)]);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task MixedConcreteUserAndPredicate_NoMvpProducer_ThrowsNotSupported()
    {
        var aUser = WorkflowTransitionAssignment.Create(10);
        aUser.AddClause(AssignmentClauseMode.SpecificUser, JsonSerializer.Serialize(new[] { Guid.NewGuid().ToString() }));
        aUser.AddClause(AssignmentClauseMode.ByPermission, JsonSerializer.Serialize(new[] { "P.X" }));

        var act = async () => await ResolveAsync(State(2), [new OutgoingAssignment(Transition(10, 2), aUser)]);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    // ── ResolveAssignmentAsync (single-assignment overload) — RT5, parallel-branch child entry ──
    // Decoupled from any concrete clause entity: caller passes (Mode, RefsJson) shapes. Shares the
    // exact clause-mode semantics + Finalize() of ResolveAsync. These facts prove the per-child entry
    // point resolves identically; the transition path's full mode matrix is covered above.

    private async Task<HandlerResolution> ResolveAssignmentAsync(
        IReadOnlyList<(AssignmentClauseMode Mode, string? RefsJson)> clauses,
        AssignmentCombineMode combine = AssignmentCombineMode.Union) =>
        await _sut.ResolveAssignmentAsync(clauses, combine, new WorkflowResolutionContext(Tenant, Initiator));

    [Fact]
    public async Task ResolveAssignment_ByPermission_ResolvesToPredicatePool()
    {
        var r = await ResolveAssignmentAsync([(AssignmentClauseMode.ByPermission, JsonArr("WORKFLOW.TASK.PROCESS"))]);

        r.Assignee.Should().BeNull();
        r.CandidatePredicates.Should().ContainSingle(p =>
            p.Type == CandidatePrincipalType.Permission && p.Ref == "WORKFLOW.TASK.PROCESS");
        r.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAssignment_Initiator_ResolvesToInitiatorAssignee()
    {
        (await ResolveAssignmentAsync([(AssignmentClauseMode.Initiator, null)]))
            .Assignee.Should().Be(Initiator);
    }

    [Fact]
    public async Task ResolveAssignment_RoleAndPermission_BothPredicatesPresent()
    {
        // Anti-tautology lock: a child carrying two clause modes yields both predicates in one pool.
        var r = await ResolveAssignmentAsync([
            (AssignmentClauseMode.ByRole, JsonArr("ROLE_X")),
            (AssignmentClauseMode.ByPermission, JsonArr("P.Y")),
        ]);

        r.CandidatePredicates.Should().HaveCount(2);
        r.CandidatePredicates.Should().Contain(p => p.Type == CandidatePrincipalType.Role && p.Ref == "ROLE_X");
        r.CandidatePredicates.Should().Contain(p => p.Type == CandidatePrincipalType.Permission && p.Ref == "P.Y");
    }

    [Fact]
    public async Task ResolveAssignment_NoClauses_IsEmpty()
    {
        (await ResolveAssignmentAsync([])).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAssignment_IntersectCombine_ThrowsNotSupported()
    {
        var act = async () => await ResolveAssignmentAsync(
            [(AssignmentClauseMode.ByPermission, JsonArr("P.A"))],
            combine: AssignmentCombineMode.Intersect);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
