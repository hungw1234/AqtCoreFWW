using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Services;

/// <summary>
/// F1-F7 unit tests for <see cref="WorkflowTaskFactory"/> — TDD facts verifying
/// field-threading, override precedence, pool snapshot, and default values.
///
/// Anti-tautology discipline (S5/S6 META-L (c)):
/// Every fact asserts WHICH code path executed (Stage called 1×) before asserting
/// the resulting task shape. Capture pattern mirrors S10a A4 (NSubstitute .When/.Do).
///
/// PERMANENT INVARIANTS verified:
/// - S7 single-SaveChanges: factory never calls SaveChangesAsync (not exposed on mock).
/// - DASSIGN-a: override wins over resolver Assignee AND over empty pool.
/// - F5 dumb-pipe: factory stages with null assignee + null JSON when Empty + no override.
/// </summary>
public sealed class WorkflowTaskFactoryTests
{
    private readonly IWorkflowTaskRepository _taskRepository;
    private readonly IWorkflowTaskFactory _factory;

    private static readonly Guid TenantA = Guid.NewGuid();

    public WorkflowTaskFactoryTests()
    {
        _taskRepository = Substitute.For<IWorkflowTaskRepository>();
        _factory = new WorkflowTaskFactory(_taskRepository);
    }

    // ── F1 — override accepted: overrideAssignee wins over resolution.Assignee ──

    /// <summary>
    /// F1: given HandlerResolution.ToAssignee(userA) AND overrideAssignee=userB
    ///     → staged task has AssigneeId=userB AND CandidateRefsJson=null.
    ///
    /// Anti-tautology preconditions:
    /// (a) userA ≠ userB (distinct Guids prevent ctor-arg swap false-green)
    /// (b) resolution.Assignee == userA (confirms non-override-path input)
    /// (c) Stage called exactly 1× before field asserts
    /// (d) task.AssigneeId == userB (NOT userA — override wins)
    /// </summary>
    [Fact]
    public void StageFirstOrNextTask_OverrideAssignee_WinsOverResolutionAssignee()
    {
        // Arrange
        var userA = new Guid("11111111-1111-1111-1111-111111111111");
        var userB = new Guid("22222222-2222-2222-2222-222222222222");
        var resolution = HandlerResolution.ToAssignee(userA);
        var createdBy = new Guid("33333333-3333-3333-3333-333333333333");

        // Precondition (a+b)
        userA.Should().NotBe(userB, "F1 requires distinct Guids to prevent swap false-green");
        resolution.Assignee.Should().Be(userA, "F1 input resolution must have Assignee=userA");

        WorkflowTask? capturedTask = null;
        _taskRepository.When(r => r.Stage(Arg.Any<WorkflowTask>()))
            .Do(c => capturedTask = c.Arg<WorkflowTask>());

        // Act
        var result = _factory.StageFirstOrNextTask(
            instanceInternalId: 10L,
            title: "F1 Task",
            tenantId: TenantA,
            createdBy: createdBy,
            overrideAssignee: userB,
            resolution: resolution);

        // Precondition (c): Stage called exactly once
        _taskRepository.Received(1).Stage(Arg.Any<WorkflowTask>());
        capturedTask.Should().NotBeNull("Stage must have been called");

        // Assert (d): override wins
        capturedTask!.AssigneeId.Should().Be(userB,
            "DASSIGN-a: overrideAssignee wins over resolution.Assignee");
        capturedTask.AssigneeId.Should().NotBe(userA,
            "F1 anti-tautology: userA must NOT be the assignee when override=userB");
        capturedTask.CandidateRefsJson.Should().BeNull(
            "F1: override path does not set CandidateRefsJson");

        // Return value matches staged instance
        result.Should().BeSameAs(capturedTask, "factory returns the staged task");
    }

    // ── F2 — single assignee, no override ──

    /// <summary>
    /// F2: given HandlerResolution.ToAssignee(userA) + overrideAssignee=null
    ///     → staged task has AssigneeId=userA AND CandidateRefsJson=null.
    ///
    /// Anti-tautology: userA is distinct non-default Guid; confirms no swap.
    /// </summary>
    [Fact]
    public void StageFirstOrNextTask_SingleAssignee_NoOverride_SetsAssigneeId()
    {
        // Arrange
        var userA = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var resolution = HandlerResolution.ToAssignee(userA);

        // Precondition
        resolution.Assignee.Should().Be(userA, "F2 requires Assignee input");
        resolution.CandidatePredicates.Should().BeEmpty("F2 uses single-assignee resolution");

        WorkflowTask? capturedTask = null;
        _taskRepository.When(r => r.Stage(Arg.Any<WorkflowTask>()))
            .Do(c => capturedTask = c.Arg<WorkflowTask>());

        // Act
        _factory.StageFirstOrNextTask(
            instanceInternalId: 20L,
            title: "F2 Task",
            tenantId: TenantA,
            createdBy: Guid.NewGuid(),
            overrideAssignee: null,
            resolution: resolution);

        // Precondition: Stage called
        _taskRepository.Received(1).Stage(Arg.Any<WorkflowTask>());
        capturedTask.Should().NotBeNull();

        // Assert
        capturedTask!.AssigneeId.Should().Be(userA, "single-assignee path sets AssigneeId");
        capturedTask.CandidateRefsJson.Should().BeNull(
            "single-assignee path does not set CandidateRefsJson");
    }

    // ── F3 — pool snapshot: CandidateRefsJson round-trip ──

    /// <summary>
    /// F3: given HandlerResolution.Pool([predicateA, predicateB]) + overrideAssignee=null
    ///     → staged task has AssigneeId=null AND CandidateRefsJson = canonical JSON.
    ///
    /// Anti-tautology precondition: Deserialize(staged.CandidateRefsJson) must parse
    /// successfully AND contain exactly the two input predicates (defends against ad-hoc
    /// serialization drift — if factory calls .ToString() instead of CandidatePrincipalSerializer,
    /// round-trip will fail or produce wrong elements).
    /// </summary>
    [Fact]
    public void StageFirstOrNextTask_PoolResolution_NoOverride_SetsCandidateRefsJsonWithRoundTrip()
    {
        // Arrange
        var predicates = new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Permission, "PERM_ALPHA", AssignmentCombineMode.Union),
            new(CandidatePrincipalType.Permission, "PERM_BETA", AssignmentCombineMode.Union)
        };
        var resolution = HandlerResolution.Pool(predicates);

        // Precondition
        resolution.Assignee.Should().BeNull("F3 uses pool resolution (no single Assignee)");
        resolution.CandidatePredicates.Should().HaveCount(2, "F3 requires 2 distinct predicates");
        predicates[0].Ref.Should().NotBe(predicates[1].Ref, "F3: predicates must be distinct");

        WorkflowTask? capturedTask = null;
        _taskRepository.When(r => r.Stage(Arg.Any<WorkflowTask>()))
            .Do(c => capturedTask = c.Arg<WorkflowTask>());

        // Act
        _factory.StageFirstOrNextTask(
            instanceInternalId: 30L,
            title: "F3 Pool Task",
            tenantId: TenantA,
            createdBy: Guid.NewGuid(),
            overrideAssignee: null,
            resolution: resolution);

        // Precondition: Stage called
        _taskRepository.Received(1).Stage(Arg.Any<WorkflowTask>());
        capturedTask.Should().NotBeNull();

        // Assert: pool task has null assignee
        capturedTask!.AssigneeId.Should().BeNull("pool task has no direct assignee");

        // Assert: CandidateRefsJson is non-null and round-trips correctly
        capturedTask.CandidateRefsJson.Should().NotBeNull(
            "pool task must have CandidateRefsJson set");

        // F3 anti-tautology: round-trip deserialize to prove canonical serializer was used
        var deserialized = CandidatePrincipalSerializer.Deserialize(capturedTask.CandidateRefsJson);
        deserialized.Should().HaveCount(2,
            "F3 round-trip: deserialized predicates must match input count");
        deserialized.Should().Contain(p => p.Ref == "PERM_ALPHA" && p.Type == CandidatePrincipalType.Permission,
            "F3 round-trip: PERM_ALPHA predicate must survive serialization");
        deserialized.Should().Contain(p => p.Ref == "PERM_BETA" && p.Type == CandidatePrincipalType.Permission,
            "F3 round-trip: PERM_BETA predicate must survive serialization");

        // Also verify it matches the canonical serializer output exactly
        capturedTask.CandidateRefsJson.Should().Be(
            CandidatePrincipalSerializer.Serialize(predicates),
            "F3: CandidateRefsJson must equal canonical serializer output");
    }

    // ── F4 — empty + override: override wins over empty pool ──

    /// <summary>
    /// F4: HandlerResolution.Empty + overrideAssignee=userA
    ///     → staged task has AssigneeId=userA AND CandidateRefsJson=null.
    ///
    /// DASSIGN-a: override wins over empty — factory must not treat Empty as a
    /// blocking condition. Caller has already verified override is valid via DLIVE-b gate.
    /// </summary>
    [Fact]
    public void StageFirstOrNextTask_EmptyResolution_WithOverride_OverrideWins()
    {
        // Arrange
        var userA = new Guid("44444444-4444-4444-4444-444444444444");
        var resolution = HandlerResolution.Empty;

        // Precondition
        resolution.IsEmpty.Should().BeTrue("F4 requires Empty resolution");
        resolution.Assignee.Should().BeNull("Empty has no Assignee");
        resolution.CandidatePredicates.Should().BeEmpty("Empty has no predicates");

        WorkflowTask? capturedTask = null;
        _taskRepository.When(r => r.Stage(Arg.Any<WorkflowTask>()))
            .Do(c => capturedTask = c.Arg<WorkflowTask>());

        // Act
        _factory.StageFirstOrNextTask(
            instanceInternalId: 40L,
            title: "F4 Task",
            tenantId: TenantA,
            createdBy: Guid.NewGuid(),
            overrideAssignee: userA,
            resolution: resolution);

        // Precondition: Stage called
        _taskRepository.Received(1).Stage(Arg.Any<WorkflowTask>());
        capturedTask.Should().NotBeNull();

        // Assert: override wins
        capturedTask!.AssigneeId.Should().Be(userA,
            "DASSIGN-a: override wins even when resolution is Empty");
        capturedTask.CandidateRefsJson.Should().BeNull(
            "F4: override path does not set CandidateRefsJson");
    }

    // ── F5 — empty + no override: dumb-pipe contract ──

    /// <summary>
    /// F5: HandlerResolution.Empty + overrideAssignee=null
    ///     → staged task has AssigneeId=null AND CandidateRefsJson=null.
    ///
    /// Factory is dumb-pipe: it stages without throwing. Caller MUST have already
    /// returned WF_NO_HANDLER upstream (non-final target state guard in ETR).
    /// Factory does NOT enforce WF_NO_HANDLER — that is the caller's responsibility.
    /// </summary>
    [Fact]
    public void StageFirstOrNextTask_EmptyResolution_NoOverride_StagesTaskWithNullAssigneeAndNullJson()
    {
        // Arrange
        var resolution = HandlerResolution.Empty;

        // Precondition
        resolution.IsEmpty.Should().BeTrue("F5 requires Empty resolution");

        WorkflowTask? capturedTask = null;
        _taskRepository.When(r => r.Stage(Arg.Any<WorkflowTask>()))
            .Do(c => capturedTask = c.Arg<WorkflowTask>());

        // Act — must NOT throw (factory is dumb-pipe)
        var act = () => _factory.StageFirstOrNextTask(
            instanceInternalId: 50L,
            title: "F5 Task",
            tenantId: TenantA,
            createdBy: Guid.NewGuid(),
            overrideAssignee: null,
            resolution: resolution);

        act.Should().NotThrow("F5 dumb-pipe: factory must not throw on Empty + no override");

        // Precondition: Stage still called (factory stages even for empty resolution)
        _taskRepository.Received(1).Stage(Arg.Any<WorkflowTask>());
        capturedTask.Should().NotBeNull();

        // Assert: null assignee + null json
        capturedTask!.AssigneeId.Should().BeNull(
            "F5: empty resolution + no override yields null AssigneeId");
        capturedTask.CandidateRefsJson.Should().BeNull(
            "F5: empty resolution + no override yields null CandidateRefsJson");
    }

    // ── F6 — field threading: title/tenant/createdBy/instanceInternalId verbatim ──

    /// <summary>
    /// F6: specific (title, tenantId, createdBy, instanceInternalId)
    ///     → staged task carries all 4 values verbatim.
    ///
    /// Anti-tautology: each of 4 fields uses a unique value distinct from defaults
    /// (not Guid.Empty, not 0, not empty string) so false-green from default-init is impossible.
    /// </summary>
    [Fact]
    public void StageFirstOrNextTask_ThreadsAllCallerFields_Verbatim()
    {
        // Arrange — each value is unique and non-default
        const string expectedTitle = "My Unique Task Title F6";
        var expectedTenantId = new Guid("55555555-5555-5555-5555-555555555555");
        var expectedCreatedBy = new Guid("66666666-6666-6666-6666-666666666666");
        const long expectedInstanceId = 42L;

        // Sanity: none are defaults
        expectedTitle.Should().NotBeEmpty();
        expectedTenantId.Should().NotBe(Guid.Empty);
        expectedCreatedBy.Should().NotBe(Guid.Empty);
        expectedInstanceId.Should().NotBe(0L);

        WorkflowTask? capturedTask = null;
        _taskRepository.When(r => r.Stage(Arg.Any<WorkflowTask>()))
            .Do(c => capturedTask = c.Arg<WorkflowTask>());

        // Act
        _factory.StageFirstOrNextTask(
            instanceInternalId: expectedInstanceId,
            title: expectedTitle,
            tenantId: expectedTenantId,
            createdBy: expectedCreatedBy,
            overrideAssignee: null,
            resolution: HandlerResolution.Empty);

        // Precondition: Stage called
        _taskRepository.Received(1).Stage(Arg.Any<WorkflowTask>());
        capturedTask.Should().NotBeNull();

        // Assert all 4 fields threaded verbatim
        capturedTask!.Title.Should().Be(expectedTitle, "F6: title must be threaded verbatim");
        capturedTask.TenantId.Should().Be(expectedTenantId, "F6: tenantId must be threaded verbatim");
        capturedTask.WorkflowInstanceId.Should().Be(expectedInstanceId,
            "F6: instanceInternalId must be threaded verbatim");

        // createdBy surfaces via AuditableEntity.CreatedBy (public Guid { get; private set; })
        // Reflection BindingFlags.Public is sufficient — the property is not hidden.
        var createdByProp = capturedTask.GetType().GetProperty("CreatedBy",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
        var actualCreatedBy = (Guid)createdByProp.GetValue(capturedTask)!;
        actualCreatedBy.Should().Be(expectedCreatedBy,
            "F6: createdBy must be threaded verbatim via SetAuditCreate");
    }

    // ── F7 — priority + dueDate defaults ──

    /// <summary>
    /// F7: staged task has Priority=Normal AND DueDate=null.
    ///
    /// Phase 5 owns SLA/due-date wiring (per ETR comment :328).
    /// Factory MUST NOT compute or set DueDate. Priority is Normal for MVP.
    /// </summary>
    [Fact]
    public void StageFirstOrNextTask_DefaultsToNormalPriorityAndNullDueDate()
    {
        // Arrange
        WorkflowTask? capturedTask = null;
        _taskRepository.When(r => r.Stage(Arg.Any<WorkflowTask>()))
            .Do(c => capturedTask = c.Arg<WorkflowTask>());

        // Act
        _factory.StageFirstOrNextTask(
            instanceInternalId: 70L,
            title: "F7 Task",
            tenantId: TenantA,
            createdBy: Guid.NewGuid(),
            overrideAssignee: null,
            resolution: HandlerResolution.Empty);

        // Precondition: Stage called
        _taskRepository.Received(1).Stage(Arg.Any<WorkflowTask>());
        capturedTask.Should().NotBeNull();

        // Assert
        capturedTask!.Priority.Should().Be(WorkflowTaskPriority.Normal,
            "F7: factory sets Priority=Normal (MVP default)");
        capturedTask.DueDate.Should().BeNull(
            "F7: factory does not compute DueDate — Phase 5 owns SLA wiring");
    }

    // ── F8 — pool resolution materializes index rows ──

    /// <summary>
    /// F8: given HandlerResolution.Pool([p1, p2, p3]) + overrideAssignee=null
    ///     → StageCandidateIndex called 1× with exactly 3 rows, each matching the
    ///     corresponding predicate's Type/Ref/Combine/TenantId and with Trim applied to Ref.
    ///     CandidateRefsJson is also set (snapshot retained alongside index rows).
    ///
    /// Anti-tautology:
    /// (a) 3 predicates of 3 distinct types so Type field is load-bearing.
    /// (b) Ref values have leading/trailing whitespace → asserts Trim was applied.
    /// (c) StageCandidateIndex called exactly 1× (not 0, not 2).
    /// (d) Row count equals predicate count (not one-off or empty).
    /// </summary>
    [Fact]
    public void StageFirstOrNextTask_PoolResolution_StageCandidateIndex_CalledWithMatchingRows()
    {
        // Arrange — 3 predicates, distinct types, refs with surrounding whitespace
        var predicates = new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Permission, "  WORKFLOW.TASK.PROCESS  ", AssignmentCombineMode.Union),
            new(CandidatePrincipalType.Role,       " APPROVER_ROLE ",           AssignmentCombineMode.Union),
            new(CandidatePrincipalType.Group,      " FINANCE_GROUP ",           AssignmentCombineMode.Union)
        };
        var resolution = HandlerResolution.Pool(predicates);
        var tenantId = new Guid("88888888-8888-8888-8888-888888888888");

        // Precondition
        resolution.Assignee.Should().BeNull("F8 requires pool resolution");
        resolution.CandidatePredicates.Should().HaveCount(3, "F8 requires 3 predicates");

        WorkflowTask? capturedTask = null;
        IReadOnlyList<WorkflowTaskCandidateIndex>? capturedRows = null;

        _taskRepository.When(r => r.Stage(Arg.Any<WorkflowTask>()))
            .Do(c => capturedTask = c.Arg<WorkflowTask>());
        _taskRepository.When(r => r.StageCandidateIndex(Arg.Any<IReadOnlyList<WorkflowTaskCandidateIndex>>()))
            .Do(c => capturedRows = c.Arg<IReadOnlyList<WorkflowTaskCandidateIndex>>());

        // Act
        _factory.StageFirstOrNextTask(
            instanceInternalId: 80L,
            title: "F8 Pool Task",
            tenantId: tenantId,
            createdBy: Guid.NewGuid(),
            overrideAssignee: null,
            resolution: resolution);

        // Precondition (c): StageCandidateIndex called exactly once
        _taskRepository.Received(1).StageCandidateIndex(Arg.Any<IReadOnlyList<WorkflowTaskCandidateIndex>>());
        capturedRows.Should().NotBeNull("StageCandidateIndex must have been called");
        capturedTask.Should().NotBeNull("Stage must have been called");

        // Assert (d): row count equals predicate count
        capturedRows!.Should().HaveCount(3, "F8: one index row per predicate");

        // Assert: each row matches its predicate (Type/Ref/Combine/TenantId) with Trim applied
        capturedRows.Should().Contain(r =>
            r.PrincipalType == CandidatePrincipalType.Permission
            && r.PrincipalRefId == "WORKFLOW.TASK.PROCESS"  // Trimmed
            && r.CombineMode == AssignmentCombineMode.Union
            && r.TenantId == tenantId,
            "F8: Permission row must have trimmed ref, correct Type/Combine/Tenant");

        capturedRows.Should().Contain(r =>
            r.PrincipalType == CandidatePrincipalType.Role
            && r.PrincipalRefId == "APPROVER_ROLE"           // Trimmed
            && r.CombineMode == AssignmentCombineMode.Union
            && r.TenantId == tenantId,
            "F8: Role row must have trimmed ref, correct Type/Combine/Tenant");

        capturedRows.Should().Contain(r =>
            r.PrincipalType == CandidatePrincipalType.Group
            && r.PrincipalRefId == "FINANCE_GROUP"           // Trimmed
            && r.CombineMode == AssignmentCombineMode.Union
            && r.TenantId == tenantId,
            "F8: Group row must have trimmed ref, correct Type/Combine/Tenant");

        // CandidateRefsJson still set (snapshot retained alongside index rows)
        capturedTask!.CandidateRefsJson.Should().NotBeNull(
            "F8: pool task must retain CandidateRefsJson snapshot alongside index rows");

        // Each row's WorkflowTask nav-prop points to the staged task (EF fixup precondition)
        capturedRows.Should().OnlyContain(r => ReferenceEquals(r.WorkflowTask, capturedTask),
            "F8: each index row must reference the staged task via nav-prop for EF fixup");
    }

    // ── F9 — single-assignee: StageCandidateIndex NOT called ──

    /// <summary>
    /// F9: given HandlerResolution.ToAssignee(userA) + overrideAssignee=null
    ///     → StageCandidateIndex NOT called (zero index rows for single-assignee tasks).
    ///
    /// Anti-tautology: precondition verifies CandidatePredicates is empty on this resolution
    /// so the test cannot pass if factory ignores predicates.Count guard.
    /// </summary>
    [Fact]
    public void StageFirstOrNextTask_SingleAssignee_StageCandidateIndex_NotCalled()
    {
        // Arrange
        var userA = new Guid("99999999-9999-9999-9999-999999999999");
        var resolution = HandlerResolution.ToAssignee(userA);

        // Precondition
        resolution.CandidatePredicates.Should().BeEmpty(
            "F9: single-assignee resolution must have empty predicates");
        resolution.Assignee.Should().Be(userA, "F9: resolution has single assignee");

        // Act
        _factory.StageFirstOrNextTask(
            instanceInternalId: 90L,
            title: "F9 Single Task",
            tenantId: TenantA,
            createdBy: Guid.NewGuid(),
            overrideAssignee: null,
            resolution: resolution);

        // Assert: StageCandidateIndex never called
        _taskRepository.DidNotReceive().StageCandidateIndex(
            Arg.Any<IReadOnlyList<WorkflowTaskCandidateIndex>>());
    }
}
