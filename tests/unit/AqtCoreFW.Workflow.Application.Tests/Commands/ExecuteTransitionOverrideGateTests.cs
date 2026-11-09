using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.Commands.ExecuteTransition;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Reflection;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// A1–A4, A6 unit tests — Slice 10a override gate (DLIVE-b ordering).
///
/// Gate order (DLIVE-b):
///   (1) request.NextHandlerUserId == null → skip override entirely
///   (2) tOA.Assignment.AllowAssignorOverride == false → silent fallthrough (DGATE-b log)
///   (3) ∈Pool check via ResolverAuthz.IsActorAuthorized(nohResolution, codes, override) → 403 if false
///   (4) live GetUserActiveStatusAsync(overrideTarget) → 403 if !IsActive || IsLockedOut
///   (5) override accepted → chosenHandler = NextHandlerUserId
///
/// Precondition-assert discipline: every test asserts WHICH code path ran via
/// NSubstitute receive-counts and captured domain-object state (S5/S6 META-L (c) preventive).
/// </summary>
public sealed class ExecuteTransitionOverrideGateTests
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
    private const string AnyPermission = "ANY_PERMISSION";

    public ExecuteTransitionOverrideGateTests()
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
            .Returns((IReadOnlySet<string>)new HashSet<string>([AnyPermission]));
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

    // ── scenario builder ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a two-state non-final scenario wired for override-gate tests.
    /// <paramref name="allowAssignorOverride"/> controls the assignment config.
    /// <paramref name="nohResolution"/> is the target-state resolver result (used for ∈Pool check).
    /// </summary>
    private (
        WorkflowInstance instance,
        WorkflowDefinition definition,
        WorkflowState draftState,
        WorkflowState inReviewState,
        WorkflowTransition transition,
        WorkflowTransitionAssignment assignment)
        BuildOverrideScenario(
            bool allowAssignorOverride,
            HandlerResolution nohResolution)
    {
        var gen = TestGuidGenerator.Create();
        var actor = Guid.NewGuid();

        var definition = WorkflowDefinition.Create(gen, "OverrideFlow", "Desc", DefaultTenantId, actor);

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

        // Create assignment with explicit AllowAssignorOverride value (DTEST requirement:
        // NEVER rely on default=false coincidence; always set explicitly).
        var assignment = WorkflowTransitionAssignment.Create(
            transition.Id,
            combineMode: AssignmentCombineMode.Union,
            allowAssignorOverride: allowAssignorOverride);
        AssignId(assignment, NextId());

        var instance = WorkflowInstance.Create(gen, definition.Id, draft.Id,
            "Case", Guid.NewGuid(), DefaultTenantId, actor);

        // Wire loads
        _instanceRepo.GetWithHistoryAsync(instance.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(instance));
        _definitionRepo.GetWithStatesAndTransitionsByInternalIdAsync(definition.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        // Authz-of-T gate: OA with assignment for current state (draft)
        var draftOA = new OutgoingAssignment(transition, assignment);
        _definitionRepo.GetOutgoingWithAssignmentsAsync(draft.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new[] { draftOA }));

        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == draft.Id),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Pool(new List<CandidatePredicate>
            {
                new(CandidatePrincipalType.Permission, AnyPermission, AssignmentCombineMode.Union)
            }));

        // WF_NO_HANDLER pre-check: wire non-empty outgoing for target state
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
            .Returns(nohResolution);

        return (instance, definition, draft, inReview, transition, assignment);
    }

    // ── A1 — AllowAssignorOverride=false → silent fallthrough (DGATE-b) ──────────

    /// <summary>
    /// A1 — When <c>AllowAssignorOverride == false</c>, override is silently denied:
    /// the handler proceeds with normal resolution (NOT 403), logs at Information level,
    /// and creates the task for the NORMAL resolver's handler (not NextHandlerUserId).
    ///
    /// Precondition asserts:
    /// (a) tOA.Assignment.AllowAssignorOverride == false  (verified via assignment.AllowAssignorOverride)
    /// (b) identityClient.GetUserActiveStatusAsync DidNotReceive (DLIVE-b gate 4 skipped)
    /// (c) nohResolution was computed (WF_NO_HANDLER ran: GetOutgoingWithAssignmentsAsync for target called)
    /// (d) result.IsSuccess
    /// (e) created WorkflowTask has assignee from normal resolution (NOT NextHandlerUserId)
    /// </summary>
    [Fact]
    public async Task Handle_OverrideRequested_AllowAssignorOverrideFalse_FallsThroughToNormalResolution()
    {
        // Arrange
        var normalAssignee = Guid.NewGuid();
        var overrideTarget = Guid.NewGuid(); // different user — must NOT be used

        var normalResolution = HandlerResolution.ToAssignee(normalAssignee);

        var (instance, _, _, _, transition, assignment) =
            BuildOverrideScenario(allowAssignorOverride: false, nohResolution: normalResolution);

        // Precondition (a)
        assignment.AllowAssignorOverride.Should().BeFalse(
            "A1 requires AllowAssignorOverride=false to test the fallthrough path");

        WorkflowTask? capturedTask = null;
        _taskRepository.When(r => r.Stage(Arg.Any<WorkflowTask>()))
            .Do(c => capturedTask = c.Arg<WorkflowTask>());

        var cmd = new ExecuteTransitionCommand(
            InstanceId: instance.PublicId,
            TransitionId: transition.Id,
            ActorId: Guid.NewGuid(),
            Comment: null,
            NextHandlerUserId: overrideTarget); // override requested but denied

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert (b): live check skipped
        await _identityClient.DidNotReceive().GetUserActiveStatusAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        // Assert (c): WF_NO_HANDLER check ran (target outgoing was queried)
        await _definitionRepo.Received().GetOutgoingWithAssignmentsAsync(
            Arg.Any<long>(), Arg.Any<CancellationToken>());

        // Assert (d): success (not 403)
        result.IsSuccess.Should().BeTrue("AllowAssignorOverride=false → silent fallthrough, NOT 403");

        // Assert (e): task assigned to NORMAL resolver result, NOT overrideTarget
        capturedTask.Should().NotBeNull("task must still be created via normal resolution");
        capturedTask!.AssigneeId.Should().Be(normalAssignee,
            "task must be assigned to normal-resolution Assignee when override is denied");
        capturedTask.AssigneeId.Should().NotBe(overrideTarget,
            "override target must NOT be used when AllowAssignorOverride=false");
    }

    // ── A2 — AllowAssignorOverride=true, target not in pool → 403 ────────────────

    /// <summary>
    /// A2 — When <c>AllowAssignorOverride == true</c> but <c>NextHandlerUserId</c> is not
    /// ∈ Pool(targetState), override is denied with 403 TransitionForbidden.
    ///
    /// Precondition asserts:
    /// (a) assignment.AllowAssignorOverride == true
    /// (b) nohResolution pool does NOT permit NextHandlerUserId (different permission required)
    /// (c) identityClient.GetUserActiveStatusAsync DidNotReceive (gate 4 skipped when ∈Pool fails)
    /// (d) result.IsFailed + first-error WorkflowErrorCode == TransitionForbidden
    /// (e) instanceRepo.UpdateWithStateGuardNoSaveAsync DidNotReceive (no persist — state unchanged)
    /// </summary>
    [Fact]
    public async Task Handle_OverrideRequested_TargetNotInPool_Returns403()
    {
        // Arrange
        var overrideTarget = Guid.NewGuid();

        // Pool that requires a specific permission the override target does NOT hold
        const string PoolPermission = "SPECIFIC_RESTRICTED_PERMISSION";
        var poolResolution = HandlerResolution.Pool(new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Permission, PoolPermission, AssignmentCombineMode.Union)
        });

        var (instance, _, _, _, transition, assignment) =
            BuildOverrideScenario(allowAssignorOverride: true, nohResolution: poolResolution);

        // Precondition (a)
        assignment.AllowAssignorOverride.Should().BeTrue(
            "A2 requires AllowAssignorOverride=true to reach the pool-membership check");

        // Override target does NOT have PoolPermission (only AnyPermission default)
        _identityClient.GetEffectivePermissionCodesAsync(overrideTarget, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<string>)new HashSet<string>(["SOME_OTHER_PERMISSION"]));

        var cmd = new ExecuteTransitionCommand(
            InstanceId: instance.PublicId,
            TransitionId: transition.Id,
            ActorId: Guid.NewGuid(),
            Comment: null,
            NextHandlerUserId: overrideTarget);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert (c): live-check skipped when ∈Pool fails
        await _identityClient.DidNotReceive().GetUserActiveStatusAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        // Assert (d): 403
        result.IsFailed.Should().BeTrue("override target not in pool → 403");
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("WorkflowErrorCode") &&
            e.Metadata["WorkflowErrorCode"]!.ToString() == WorkflowErrorCodes.TransitionForbidden,
            "error code must be TransitionForbidden");

        // Assert (e): no state change
        await _instanceRepo.DidNotReceive().UpdateWithStateGuardNoSaveAsync(
            Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>());
    }

    // ── A3 — AllowAssignorOverride=true, target in pool but inactive → 403 ───────

    /// <summary>
    /// A3 — When override target ∈ Pool but user is inactive OR locked out → 403.
    /// Two [InlineData] cases: !IsActive and IsLockedOut.
    ///
    /// Precondition asserts:
    /// (a) assignment.AllowAssignorOverride == true
    /// (b) override target IS ∈ Pool
    /// (c) GetUserActiveStatusAsync(overrideTarget) Received(1) EXACTLY (gate 4 ran)
    /// (d) result.IsFailed + WorkflowErrorCode == TransitionForbidden
    /// (e) instanceRepo.UpdateWithStateGuardNoSaveAsync DidNotReceive
    /// </summary>
    [Theory]
    [InlineData(false, false)] // inactive, not locked
    [InlineData(true, true)]   // active but locked out
    public async Task Handle_OverrideRequested_TargetInactiveOrLockedOut_Returns403(
        bool isActive, bool isLockedOut)
    {
        // Arrange
        var overrideTarget = Guid.NewGuid();
        const string PoolPermission = "OVERRIDE_POOL_PERM";

        // Pool permits overrideTarget via the PoolPermission
        var poolResolution = HandlerResolution.Pool(new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Permission, PoolPermission, AssignmentCombineMode.Union)
        });

        var (instance, _, _, _, transition, assignment) =
            BuildOverrideScenario(allowAssignorOverride: true, nohResolution: poolResolution);

        // Precondition (a)
        assignment.AllowAssignorOverride.Should().BeTrue(
            "A3 requires AllowAssignorOverride=true to reach gate 4");

        // Precondition (b): override target HAS the pool permission
        _identityClient.GetEffectivePermissionCodesAsync(overrideTarget, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<string>)new HashSet<string>([PoolPermission]));

        // Precondition (c): live check will find user inactive or locked
        _identityClient.GetUserActiveStatusAsync(overrideTarget, Arg.Any<CancellationToken>())
            .Returns(new UserActiveStatusDto { IsActive = isActive, IsLockedOut = isLockedOut });

        var cmd = new ExecuteTransitionCommand(
            InstanceId: instance.PublicId,
            TransitionId: transition.Id,
            ActorId: Guid.NewGuid(),
            Comment: null,
            NextHandlerUserId: overrideTarget);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert (c): live check ran EXACTLY once for overrideTarget
        await _identityClient.Received(1).GetUserActiveStatusAsync(
            overrideTarget, Arg.Any<CancellationToken>());

        // Assert (d): 403
        result.IsFailed.Should().BeTrue(
            $"inactive ({isActive}) or locked ({isLockedOut}) override target → 403");
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("WorkflowErrorCode") &&
            e.Metadata["WorkflowErrorCode"]!.ToString() == WorkflowErrorCodes.TransitionForbidden,
            "error code must be TransitionForbidden");

        // Assert (e): no state change
        await _instanceRepo.DidNotReceive().UpdateWithStateGuardNoSaveAsync(
            Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>());
    }

    // ── A4 — all gates pass → override accepted, task assigned to NextHandlerUserId ──

    /// <summary>
    /// A4 — When all 3 override gates pass, the transition succeeds and the new
    /// WorkflowTask is assigned to <c>NextHandlerUserId</c> (NOT the normal resolver).
    ///
    /// Precondition asserts:
    /// (a) assignment.AllowAssignorOverride == true
    /// (b) override target ∈ Pool
    /// (c) GetUserActiveStatusAsync returns {IsActive=true, IsLockedOut=false}
    /// (d) GetUserActiveStatusAsync Received(1)
    /// (e) UpdateWithStateGuardNoSaveAsync Received(1)
    /// (f) SaveChangesAsync Received(1)
    /// (g) result.IsSuccess
    /// (h) LOAD-BEARING: task.AssigneeId == NextHandlerUserId (NOT normalAssignee)
    ///     (locks DASSIGN-a: override replaces pool)
    /// </summary>
    [Fact]
    public async Task Handle_OverrideRequested_AllGatesPass_AssignsToOverrideTarget()
    {
        // Arrange
        var overrideTarget = Guid.NewGuid();
        var normalAssignee = Guid.NewGuid(); // should NOT be used
        const string PoolPermission = "OVERRIDE_POOL_PERM";

        // Normal resolution = different user; override should win
        var poolResolution = HandlerResolution.Pool(new List<CandidatePredicate>
        {
            new(CandidatePrincipalType.Permission, PoolPermission, AssignmentCombineMode.Union)
        });

        var (instance, _, _, _, transition, assignment) =
            BuildOverrideScenario(allowAssignorOverride: true, nohResolution: poolResolution);

        // Precondition (a)
        assignment.AllowAssignorOverride.Should().BeTrue(
            "A4 requires AllowAssignorOverride=true");

        // Precondition (b): override target has PoolPermission
        _identityClient.GetEffectivePermissionCodesAsync(overrideTarget, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<string>)new HashSet<string>([PoolPermission]));

        // Precondition (c): target is active
        _identityClient.GetUserActiveStatusAsync(overrideTarget, Arg.Any<CancellationToken>())
            .Returns(new UserActiveStatusDto { IsActive = true, IsLockedOut = false });

        WorkflowTask? capturedTask = null;
        _taskRepository.When(r => r.Stage(Arg.Any<WorkflowTask>()))
            .Do(c => capturedTask = c.Arg<WorkflowTask>());

        var cmd = new ExecuteTransitionCommand(
            InstanceId: instance.PublicId,
            TransitionId: transition.Id,
            ActorId: Guid.NewGuid(),
            Comment: null,
            NextHandlerUserId: overrideTarget);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert (d): live check ran once
        await _identityClient.Received(1).GetUserActiveStatusAsync(
            overrideTarget, Arg.Any<CancellationToken>());

        // Assert (e+f): persist pipeline ran
        await _instanceRepo.Received(1).UpdateWithStateGuardNoSaveAsync(
            Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>());
        await _instanceRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Assert (g): success
        result.IsSuccess.Should().BeTrue("all gates pass → override succeeds");

        // Assert (h): LOAD-BEARING — override target is the task assignee
        capturedTask.Should().NotBeNull("task must be created when override passes");
        capturedTask!.AssigneeId.Should().Be(overrideTarget,
            "task must be assigned to override target (DASSIGN-a: override replaces pool)");
        capturedTask.AssigneeId.Should().NotBe(normalAssignee,
            "normal resolver result must NOT be used when override is accepted");
        capturedTask.CandidateRefsJson.Should().BeNull(
            "override path is single-handler: no pool CandidateRefsJson (DASSIGN-a)");
    }

    // ── A6 — final state target → override path skipped entirely ─────────────────

    /// <summary>
    /// A6 (belt-and-braces) — When <c>targetState.IsFinal == true</c>, the WF_NO_HANDLER
    /// pre-check AND the override gate are both skipped (no pool, no task).
    ///
    /// Precondition asserts:
    /// (a) targetState.IsFinal == true
    /// (b) override path skipped (identityClient.GetUserActiveStatusAsync DidNotReceive)
    /// (c) no task created (taskRepository.Stage DidNotReceive)
    /// (d) result.IsSuccess
    /// </summary>
    [Fact]
    public async Task Handle_OverrideRequested_TargetIsFinalState_OverrideAndTaskCreationSkipped()
    {
        // Arrange
        var gen = TestGuidGenerator.Create();
        var actor = Guid.NewGuid();
        var overrideTarget = Guid.NewGuid();

        var definition = WorkflowDefinition.Create(gen, "FinalFlow", "Desc", DefaultTenantId, actor);

        var draft = WorkflowState.Create(definition.Id, "Draft", "Nháp", "Draft",
            isInitial: true, isFinal: false, "#808080", 1);
        AssignId(draft, NextId());

        var approved = WorkflowState.Create(definition.Id, "Approved", "Đã duyệt", "Approved",
            isInitial: false, isFinal: true, "#00FF00", 2); // FINAL
        AssignId(approved, NextId());

        definition.AddState(draft);
        definition.AddState(approved);

        var transition = WorkflowTransition.Create(
            definition.Id, draft.Id, approved.Id,
            "Approve", "Phê duyệt", "Approve", conditionsJson: null, sortOrder: 1);
        AssignId(transition, NextId());
        definition.AddTransition(transition);

        var assignment = WorkflowTransitionAssignment.Create(
            transition.Id,
            combineMode: AssignmentCombineMode.Union,
            allowAssignorOverride: true); // even if true, final skips entirely
        AssignId(assignment, NextId());

        var instance = WorkflowInstance.Create(gen, definition.Id, draft.Id,
            "Case", Guid.NewGuid(), DefaultTenantId, actor);

        _instanceRepo.GetWithHistoryAsync(instance.PublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(instance));
        _definitionRepo.GetWithStatesAndTransitionsByInternalIdAsync(definition.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(definition));

        var draftOA = new OutgoingAssignment(transition, assignment);
        _definitionRepo.GetOutgoingWithAssignmentsAsync(draft.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>(new[] { draftOA }));

        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == draft.Id),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Pool(new List<CandidatePredicate>
            {
                new(CandidatePrincipalType.Permission, AnyPermission, AssignmentCombineMode.Union)
            }));

        // Precondition (a)
        approved.IsFinal.Should().BeTrue("A6 requires final target state");

        var cmd = new ExecuteTransitionCommand(
            InstanceId: instance.PublicId,
            TransitionId: transition.Id,
            ActorId: Guid.NewGuid(),
            Comment: null,
            NextHandlerUserId: overrideTarget);

        // Act
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Assert (b): live check skipped (final state — no pool, no override)
        await _identityClient.DidNotReceive().GetUserActiveStatusAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        // Assert (c): no task staged
        _taskRepository.DidNotReceive().Stage(Arg.Any<WorkflowTask>());

        // Assert (d): success
        result.IsSuccess.Should().BeTrue("final-state transition with override must succeed (override silently ignored)");
    }
}
