using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.Commands.ExecuteTransition;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Reflection;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// A0, A0b, A0c unit tests — Slice 10a task-creation seam.
///
/// Verifies that <see cref="ExecuteTransitionCommandHandler"/> materialises a
/// <see cref="WorkflowTask"/> for non-branching, non-final, non-recall transitions
/// (the dominant production path that was a 6-slice latent defect: S2→S9 all shipped
/// without ever writing a WorkflowTask for linear state transitions).
///
/// DSEAM-a: task creation is the else-branch of <c>if (branch is not null)</c>.
/// DRESOLVE-a: nohResolution hoisted to outer HandleCore scope; reused here.
/// DCAND-(b): CandidateRefsJson set for pool tasks; index rows deferred to Phase 4
///            (DCAND-IDFIX-b nav-prop triggers EF migration → fallback per planner).
/// DASSIGN-a: override replaces pool (covered by A4 in override-gate tests).
///
/// Precondition-assert discipline (S5/S6 META-L (c) preventive):
/// each test MUST assert WHICH code path ran, not just the final result.
/// </summary>
public sealed class ExecuteTransitionTaskCreationTests
{
    private static long _idSeed;
    private static long NextId() => Interlocked.Increment(ref _idSeed);

    private static void AssignId(AqtCoreFW.SharedKernel.Domain.Entity e, long id) =>
        typeof(AqtCoreFW.SharedKernel.Domain.Entity)
            .GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(e, id);

    // ── shared mocks ─────────────────────────────────────────────────────────────
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IWorkflowDefinitionRepository _definitionRepo;
    private readonly IConditionEvaluator _conditionEvaluator;
    private readonly IParallelBranchRepository _branchRepo;
    private readonly IParallelBranchService _branchService;
    private readonly ITransitionAttachmentRepository _attachmentRepo;
    private readonly IIdentityModuleClient _identityClient;
    private readonly IWorkflowHandlerResolver _resolver;
    private readonly IWorkflowTaskRepository _taskRepository;
    private readonly ExecuteTransitionCommandHandler _handler;

    private static readonly Guid DefaultTenantId = Guid.NewGuid();

    public ExecuteTransitionTaskCreationTests()
    {
        _instanceRepo     = Substitute.For<IWorkflowInstanceRepository>();
        _definitionRepo   = Substitute.For<IWorkflowDefinitionRepository>();
        _conditionEvaluator = Substitute.For<IConditionEvaluator>();
        _branchRepo       = Substitute.For<IParallelBranchRepository>();
        _branchService    = Substitute.For<IParallelBranchService>();
        _attachmentRepo   = Substitute.For<ITransitionAttachmentRepository>();
        _identityClient   = Substitute.For<IIdentityModuleClient>();
        _resolver         = Substitute.For<IWorkflowHandlerResolver>();
        _taskRepository   = Substitute.For<IWorkflowTaskRepository>();

        // Default stubs
        _conditionEvaluator.Evaluate(Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, object?>>())
            .Returns(Result.Ok());
        _branchRepo.GetBranchByStateIdAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowParallelBranch?)null);
        _identityClient.GetEffectivePermissionCodesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<string>)new HashSet<string>(["ANY_PERMISSION"]));
        _definitionRepo.GetOutgoingWithAssignmentsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>([]));
        _resolver.ResolveAsync(Arg.Any<WorkflowState>(), Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Empty);
        _instanceRepo.HasAmbientTransaction().Returns(false);
        _instanceRepo.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<IWorkflowTransaction>()));
        _instanceRepo.UpdateWithStateGuardNoSaveAsync(Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok()));
        _instanceRepo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(1)));

        _handler = new ExecuteTransitionCommandHandler(
            _instanceRepo, _definitionRepo, _conditionEvaluator,
            _branchRepo, _branchService, _attachmentRepo, _identityClient,
            _resolver, NullLogger<ExecuteTransitionCommandHandler>.Instance,
            TestGuidGenerator.Create(),
            // Phase 5a: wrap mock repo in real factory so Stage assertions on _taskRepository remain valid.
            new WorkflowTaskFactory(_taskRepository),
            _taskRepository, // source-state task completion uses the same mock (auto-empty open-task list)
            Substitute.For<IOrgUnitModuleClient>()); // org-unit codes for authz parity (auto-empty set)
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a two-state non-final scenario (Draft → InReview) and wires all mocks.
    /// Returns all domain objects + the target-state resolver result.
    /// </summary>
    private (
        WorkflowInstance instance,
        WorkflowDefinition definition,
        WorkflowState draftState,
        WorkflowState inReviewState,
        WorkflowTransition transition)
        BuildNonBranchingScenario(HandlerResolution targetResolution)
    {
        var gen = TestGuidGenerator.Create();
        var actor = Guid.NewGuid();

        var definition = WorkflowDefinition.Create(gen, "TestFlow", "Desc", DefaultTenantId, actor);

        var draft = WorkflowState.Create(definition.Id, "Draft", "Nháp", "Draft",
            isInitial: true, isFinal: false, "#808080", 1);
        AssignId(draft, NextId());

        var inReview = WorkflowState.Create(definition.Id, "InReview", "Đang duyệt", "InReview",
            isInitial: false, isFinal: false, "#1890ff", 2);
        AssignId(inReview, NextId());

        definition.AddState(draft);
        definition.AddState(inReview);

        var transition = WorkflowTransition.Create(
            definition.Id, draft.Id, inReview.Id,
            "Submit", "Nộp", "Submit",
            conditionsJson: null, sortOrder: 1);
        AssignId(transition, NextId());
        definition.AddTransition(transition);

        var instance = WorkflowInstance.Create(gen, definition.Id, draft.Id,
            "Case", Guid.NewGuid(), DefaultTenantId, actor);

        // Wire loads
        _instanceRepo.GetWithHistoryAsync(instance.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(instance));
        _definitionRepo.GetWithStatesAndTransitionsByInternalIdAsync(definition.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        // Authz-of-T gate: wire a OA for the current state (draft) with non-empty pool so actor passes
        var draftOA = new OutgoingAssignment(transition, null);
        _definitionRepo.GetOutgoingWithAssignmentsAsync(draft.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new[] { draftOA }));

        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == draft.Id),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Pool(new List<CandidatePredicate>
            {
                new(CandidatePrincipalType.Permission, "ANY_PERMISSION", AssignmentCombineMode.Union)
            }));

        // WF_NO_HANDLER pre-check + task-creation seam: wire inReview outgoing
        var fakeNextTransition = WorkflowTransition.Create(
            definition.Id, inReview.Id, inReview.Id,
            "FakeNext", "Fake", "FakeNext", conditionsJson: null, sortOrder: 99);
        AssignId(fakeNextTransition, NextId());
        var inReviewOA = new OutgoingAssignment(fakeNextTransition, null);
        _definitionRepo.GetOutgoingWithAssignmentsAsync(inReview.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new[] { inReviewOA }));

        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == inReview.Id),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(targetResolution);

        return (instance, definition, draft, inReview, transition);
    }

    // ── A0 — non-branching single-handler resolver creates one WorkflowTask ──────

    /// <summary>
    /// A0 — non-branching, non-final, non-recall transition with single-user resolver result
    /// (Assignee path) creates exactly one <see cref="WorkflowTask"/> via Stage().
    ///
    /// Precondition asserts (S5 META-L (c) preventive):
    /// (a) targetState.IsFinal == false
    /// (b) branchRepository returns null (non-branching path)
    /// (c) nohResolution.Assignee.HasValue == true (Assignee resolver result)
    /// (d) taskRepository.Stage called with task matching expected shape
    /// (e) task.AssigneeId == nohResolution.Assignee, task.Status == Open, task.CandidateRefsJson == null
    /// (f) result.IsSuccess
    /// </summary>
    [Fact]
    public async Task Handle_NonBranchingTransition_SingleHandlerResolver_CreatesSingleWorkflowTask()
    {
        // Arrange
        var assigneeId = Guid.NewGuid();
        var targetResolution = HandlerResolution.ToAssignee(assigneeId);

        var (instance, _, draftState, inReviewState, transition) =
            BuildNonBranchingScenario(targetResolution);

        // Precondition: target is non-final
        inReviewState.IsFinal.Should().BeFalse("A0 requires non-final target state");

        // Precondition: branch path returns null
        _branchRepo.GetBranchByStateIdAsync(
                Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowParallelBranch?)null);

        // Precondition: nohResolution will have Assignee
        targetResolution.Assignee.Should().Be(assigneeId, "A0 requires single-user resolver result");
        targetResolution.Assignee.HasValue.Should().BeTrue();

        WorkflowTask? capturedTask = null;
        _taskRepository.When(r => r.Stage(Arg.Any<WorkflowTask>()))
            .Do(c => capturedTask = c.Arg<WorkflowTask>());

        var cmd = new ExecuteTransitionCommand(
            InstanceId: instance.PublicId,
            TransitionId: transition.Id,
            ActorId: Guid.NewGuid(),
            Comment: null);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert — precondition (d): Stage was called
        _taskRepository.Received(1).Stage(Arg.Any<WorkflowTask>());

        // Assert (e): task shape
        capturedTask.Should().NotBeNull("Stage must have been called with a WorkflowTask");
        capturedTask!.AssigneeId.Should().Be(assigneeId,
            "single-handler path sets AssigneeId from nohResolution.Assignee");
        capturedTask.Status.Should().Be(WorkflowTaskStatus.Open,
            "newly-created tasks are Open");
        capturedTask.CandidateRefsJson.Should().BeNull(
            "single-handler path does not set CandidateRefsJson");
        capturedTask.TenantId.Should().Be(instance.TenantId,
            "task must carry the instance tenant");
        capturedTask.WorkflowInstanceId.Should().Be(instance.Id,
            "task must link to the instance's internal long Id");

        // Assert (f): success
        result.IsSuccess.Should().BeTrue("non-branching transition with valid pool must succeed");

        // Assert: UpdateWithStateGuardNoSaveAsync called (state persisted in same SaveChanges)
        await _instanceRepo.Received(1).UpdateWithStateGuardNoSaveAsync(instance, Arg.Any<CancellationToken>());
    }

    // ── A0b — pool resolver creates task with CandidateRefsJson (DCAND-b JSON-only) ──

    /// <summary>
    /// A0b — pool resolver result creates task with <c>CandidateRefsJson</c> set
    /// (DCAND-(b) fallback: index rows deferred to Phase 4; only JSON snapshot written now).
    ///
    /// Precondition asserts:
    /// (a) targetState.IsFinal == false
    /// (b) branch == null
    /// (c) nohResolution.CandidatePredicates has 2 Permission predicates
    /// (d) Stage called once with: task.AssigneeId == null, CandidateRefsJson != null,
    ///     CandidateRefsJson matches CandidatePrincipalSerializer.Serialize(predicates)
    /// (e) result.IsSuccess
    /// </summary>
    [Fact]
    public async Task Handle_NonBranchingTransition_PoolResolver_CreatesPoolTaskWithCandidateRefsJson()
    {
        // Arrange
        var predicates = new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Permission, "GovOfficer", AssignmentCombineMode.Union),
            new(CandidatePrincipalType.Permission, "Reviewer", AssignmentCombineMode.Union)
        };
        var targetResolution = HandlerResolution.Pool(predicates);

        var (instance, _, _, inReviewState, transition) =
            BuildNonBranchingScenario(targetResolution);

        // Precondition checks
        inReviewState.IsFinal.Should().BeFalse("A0b requires non-final target state");
        targetResolution.CandidatePredicates.Should().HaveCount(2,
            "A0b requires exactly 2 pool predicates");
        targetResolution.Assignee.Should().BeNull("pool path has no single Assignee");

        WorkflowTask? capturedTask = null;
        _taskRepository.When(r => r.Stage(Arg.Any<WorkflowTask>()))
            .Do(c => capturedTask = c.Arg<WorkflowTask>());

        var cmd = new ExecuteTransitionCommand(
            InstanceId: instance.PublicId,
            TransitionId: transition.Id,
            ActorId: Guid.NewGuid(),
            Comment: null);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert
        _taskRepository.Received(1).Stage(Arg.Any<WorkflowTask>());

        capturedTask.Should().NotBeNull();
        capturedTask!.AssigneeId.Should().BeNull("pool task has no direct assignee");
        capturedTask.CandidateRefsJson.Should().NotBeNull(
            "pool task must have CandidateRefsJson set");
        capturedTask.CandidateRefsJson.Should().Be(
            CandidatePrincipalSerializer.Serialize(predicates),
            "CandidateRefsJson must be the canonical serialization of the resolved predicates");
        capturedTask.TenantId.Should().Be(instance.TenantId);
        capturedTask.Status.Should().Be(WorkflowTaskStatus.Open);

        result.IsSuccess.Should().BeTrue();
    }

    // ── A0c — branching path does NOT create extra task via HandleCore seam ───────

    /// <summary>
    /// A0c REGRESSION BAR — branching transitions must NOT create an extra task via the
    /// HandleCore seam. Branch-child tasks are created by ParallelBranchService only.
    ///
    /// Precondition asserts:
    /// (a) targetState.IsFinal == false
    /// (b) branchRepository returns non-null (branching path executes)
    /// (c) branchService.InitiateBranchingAsync Received(1)
    /// (d) taskRepository.Stage DidNotReceive (no extra task from HandleCore)
    /// (e) result.IsSuccess
    /// </summary>
    [Fact]
    public async Task Handle_BranchingTransition_DoesNotCreateExtraTaskViaHandleCoreSeam()
    {
        // Arrange
        var gen = TestGuidGenerator.Create();
        var actor = Guid.NewGuid();

        var definition = WorkflowDefinition.Create(gen, "BranchFlow", "Desc", DefaultTenantId, actor);

        var draft = WorkflowState.Create(definition.Id, "Draft", "Nháp", "Draft",
            isInitial: true, isFinal: false, "#808080", 1);
        AssignId(draft, NextId());

        var inReview = WorkflowState.Create(definition.Id, "InReview", "Đang duyệt", "InReview",
            isInitial: false, isFinal: false, "#1890ff", 2);
        AssignId(inReview, NextId());

        definition.AddState(draft);
        definition.AddState(inReview);

        var transition = WorkflowTransition.Create(
            definition.Id, draft.Id, inReview.Id,
            "Submit", "Nộp", "Submit", conditionsJson: null, sortOrder: 1);
        AssignId(transition, NextId());
        definition.AddTransition(transition);

        var instance = WorkflowInstance.Create(gen, definition.Id, draft.Id,
            "Case", Guid.NewGuid(), DefaultTenantId, actor);

        _instanceRepo.GetWithHistoryAsync(instance.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(instance));
        _definitionRepo.GetWithStatesAndTransitionsByInternalIdAsync(definition.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        var draftOA = new OutgoingAssignment(transition, null);
        _definitionRepo.GetOutgoingWithAssignmentsAsync(draft.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new[] { draftOA }));

        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == draft.Id),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Pool(new List<CandidatePredicate>
            {
                new(CandidatePrincipalType.Permission, "ANY_PERMISSION", AssignmentCombineMode.Union)
            }));

        // WF_NO_HANDLER pre-check: non-empty pool for target
        var fakeNext = WorkflowTransition.Create(definition.Id, inReview.Id, inReview.Id,
            "FakeNext", "Fake", "FakeNext", conditionsJson: null, sortOrder: 99);
        AssignId(fakeNext, NextId());
        _definitionRepo.GetOutgoingWithAssignmentsAsync(inReview.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(
                new[] { new OutgoingAssignment(fakeNext, null) }));
        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == inReview.Id),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Pool(new List<CandidatePredicate>
            {
                new(CandidatePrincipalType.Permission, "ANY_PERMISSION", AssignmentCombineMode.Union)
            }));

        // Precondition (b): wire NON-NULL branch so branching path executes
        var branch = WorkflowParallelBranch.Create(
            definitionId: definition.Id,
            branchingStateId: inReview.Id,
            continuationStateId: inReview.Id, // self-ref: irrelevant for this test
            branchType: ParallelBranchType.And,
            requiredCount: null,
            allowCancelOnAnyReject: false,
            timeoutMinutes: 0);
        AssignId(branch, NextId());

        var branchResult = branch; // avoid NSubstitute "problematic use" warning
        _branchRepo.GetBranchByStateIdAsync(
                Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(branchResult);

        // InitiateBranchingAsync succeeds (no exception = staging success).
        // Returns a dummy WorkflowInstanceBranchStatus — ETR discards the return value.
        // Capture return value in local variable BEFORE passing to .Returns() to avoid
        // NSubstitute "problematic use" ordering issue (pending-specification leak).
        var branchStatus = Task.FromResult<WorkflowInstanceBranchStatus>(null!);
        _branchService.InitiateBranchingAsync(
                Arg.Any<Guid>(), Arg.Any<WorkflowParallelBranch>(),
                Arg.Any<IReadOnlyDictionary<long, HandlerResolution>>(),
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(branchStatus);

        var cmd = new ExecuteTransitionCommand(
            InstanceId: instance.PublicId,
            TransitionId: transition.Id,
            ActorId: Guid.NewGuid(),
            Comment: null);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert (b+c): branching path ran
        inReview.IsFinal.Should().BeFalse("A0c precondition: non-final target");
        await _branchService.Received(1).InitiateBranchingAsync(
            Arg.Any<Guid>(), branch,
            Arg.Any<IReadOnlyDictionary<long, HandlerResolution>>(),
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        // Assert (d): HandleCore seam did NOT stage extra task
        _taskRepository.DidNotReceive().Stage(Arg.Any<WorkflowTask>());

        // Assert (e): success
        result.IsSuccess.Should().BeTrue("branching transition with valid branch must succeed");
    }

    // ── Ownership gate + auto-claim on the source work item ──────────────────────
    // Advancing a state completes its open work item. Three ownership outcomes:
    //   unclaimed  → auto-claimed to the acting user, then completed.
    //   self-claim → completed (no re-claim).
    //   foreign    → rejected (AlreadyClaimed); the task is left untouched.

    private WorkflowTask WireSourceTask(WorkflowInstance instance, Guid? assigneeId)
    {
        var task = WorkflowTask.Create(
            instance.Id, "Work item", null, assigneeId,
            WorkflowTaskPriority.Normal, null, instance.TenantId, Guid.NewGuid());
        _taskRepository.GetOpenNonBranchByInstanceAsync(instance.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowTask>>(new[] { task }));
        return task;
    }

    [Fact]
    public async Task Handle_UnclaimedSourceTask_AutoClaimsToActorThenCompletes()
    {
        var actor = Guid.NewGuid();
        var (instance, _, _, _, transition) =
            BuildNonBranchingScenario(HandlerResolution.ToAssignee(Guid.NewGuid()));
        var sourceTask = WireSourceTask(instance, assigneeId: null);

        var result = await _handler.Handle(
            new ExecuteTransitionCommand(instance.PublicId, transition.Id, actor, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue("an eligible actor advancing an unclaimed task must succeed");
        sourceTask.AssigneeId.Should().Be(actor, "an unclaimed work item is auto-claimed to the acting user");
        sourceTask.Status.Should().Be(WorkflowTaskStatus.Done, "the source work item is completed by the transition");
    }

    [Fact]
    public async Task Handle_SelfClaimedSourceTask_CompletesWithoutReclaim()
    {
        var actor = Guid.NewGuid();
        var (instance, _, _, _, transition) =
            BuildNonBranchingScenario(HandlerResolution.ToAssignee(Guid.NewGuid()));
        var sourceTask = WireSourceTask(instance, assigneeId: actor);

        var result = await _handler.Handle(
            new ExecuteTransitionCommand(instance.PublicId, transition.Id, actor, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue("the assignee may advance their own claimed task");
        sourceTask.AssigneeId.Should().Be(actor, "ownership is unchanged");
        sourceTask.Status.Should().Be(WorkflowTaskStatus.Done);
    }

    [Fact]
    public async Task Handle_ForeignClaimedSourceTask_RejectsAndLeavesTaskUntouched()
    {
        var actor = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var (instance, _, _, _, transition) =
            BuildNonBranchingScenario(HandlerResolution.ToAssignee(Guid.NewGuid()));
        var sourceTask = WireSourceTask(instance, assigneeId: otherOwner);

        var result = await _handler.Handle(
            new ExecuteTransitionCommand(instance.PublicId, transition.Id, actor, null),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue("a work item claimed by another user may not be advanced by an eligible non-assignee");
        result.Errors.Should().Contain(
            e => e.Metadata.ContainsKey("WorkflowErrorCode")
                 && (string)e.Metadata["WorkflowErrorCode"] == WorkflowErrorCodes.AlreadyClaimed,
            "the ownership-gate rejection must surface as AlreadyClaimed");
        sourceTask.AssigneeId.Should().Be(otherOwner, "the foreign claim is left intact");
        sourceTask.Status.Should().Be(WorkflowTaskStatus.Open, "a blocked transition must not complete the task");
    }
}
