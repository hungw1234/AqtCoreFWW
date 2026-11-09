using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Contracts.Events;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.ResolveBranchChild;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Reflection;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// TDD tests for ResolveBranchChildCommandHandler security fixes:
/// F1 — cross-tenant resolution blocked
/// F2 — non-assignee resolution blocked
/// F4 — concurrent resolution returns conflict
/// + happy path.
/// Phase-4b B4c:
/// - InstanceId = WorkflowInstance.PublicId (T2 Guid AK) — used in command and GetWithHistoryAsync.
/// - ChildStatusId = WorkflowBranchChildStatus.Id (T1 long PK).
/// - BranchChildId = WorkflowBranchChild.Id (T1 long PK).
/// - ContinuationStateId = WorkflowState.Id (T1 long PK).
/// - WorkflowInstanceBranchStatus.InstanceId = WorkflowInstance.Id (internal long PK, != PublicId).
/// </summary>
public sealed class ResolveBranchChildCommandHandlerTests
{
    private readonly IParallelBranchRepository _branchRepo;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IParallelBranchService _branchService;
    private readonly IWorkflowTaskRepository _taskRepo;
    private readonly IWorkflowDefinitionRepository _defRepo;
    private readonly IWorkflowHandlerResolver _resolver;
    private readonly ResolveBranchChildCommandHandler _handler;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    // WorkflowInstance.PublicId (T2 Guid AK) — used in command and GetWithHistoryAsync
    private static readonly Guid InstancePublicId = Guid.NewGuid();

    // T1 long internal IDs
    private const long InstanceInternalId = 10L;   // WorkflowInstance.Id (internal)
    private const long ParallelBranchId = 20L;      // WorkflowParallelBranch.Id
    private const long ChildStatusId = 30L;          // WorkflowBranchChildStatus.Id
    private const long BranchChildId = 40L;          // WorkflowBranchChild.Id
    private const long ContinuationStateId = 50L;    // WorkflowState.Id

    private static readonly Guid AssigneeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    public ResolveBranchChildCommandHandlerTests()
    {
        _branchRepo = Substitute.For<IParallelBranchRepository>();
        _instanceRepo = Substitute.For<IWorkflowInstanceRepository>();
        _branchService = Substitute.For<IParallelBranchService>();
        _taskRepo = Substitute.For<IWorkflowTaskRepository>();
        _defRepo = Substitute.For<IWorkflowDefinitionRepository>();
        _resolver = Substitute.For<IWorkflowHandlerResolver>();

        // Default: no outgoing assignments
        _defRepo.GetOutgoingWithAssignmentsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingAssignment>>([]));
        // Default: resolver returns a pool that is satisfied (so join-complete path doesn't block)
        _resolver.ResolveAsync(
                Arg.Any<WorkflowState>(),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Empty);

        // Default happy-path stubs for the atomic join-completion deferred-save path
        // (MarkResolved + ApplyJoin + continuation task flush in one SaveChanges).
        _instanceRepo.UpdateWithStateGuardNoSaveAsync(
                Arg.Any<WorkflowInstance>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        _instanceRepo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Ok(1));

        // Real join-completion service (continuation logic extracted out of the handler) so the existing
        // pre-check / ApplyJoin assertions exercise the real path.
        var joinCompletion = new BranchJoinCompletionService(
            _defRepo, _instanceRepo, _branchRepo, _resolver,
            Substitute.For<IWorkflowTaskFactory>(), TestGuidGenerator.Create(),
            NullLogger<BranchJoinCompletionService>.Instance);
        _handler = new ResolveBranchChildCommandHandler(
            _branchRepo, _instanceRepo, _branchService, _taskRepo, joinCompletion);
    }

    // ───── F1: Cross-tenant resolution ─────

    [Fact]
    public async Task Handle_TenantMismatch_ReturnsForbidden()
    {
        var instance = CreateInstance(TenantA);
        SetupInstanceFound(instance);
        SetupBranchStatusFound();

        var command = CreateCommand(tenantId: TenantB, resolvedBy: AssigneeId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
    }

    // ───── F2: Non-assignee resolution ─────

    [Fact]
    public async Task Handle_NoTaskForChild_ReturnsFail()
    {
        var instance = CreateInstance(TenantA);
        SetupInstanceFound(instance);
        SetupBranchStatusFound();
        _taskRepo.GetByBranchChildStatusIdAsync(ChildStatusId, Arg.Any<CancellationToken>())
            .Returns((WorkflowTask?)null);

        var command = CreateCommand(tenantId: TenantA, resolvedBy: AssigneeId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("task"));
    }

    [Fact]
    public async Task Handle_UserNotAssignee_ReturnsForbidden()
    {
        var instance = CreateInstance(TenantA);
        SetupInstanceFound(instance);
        SetupBranchStatusFound();
        SetupTaskWithAssignee(AssigneeId);

        var command = CreateCommand(tenantId: TenantA, resolvedBy: OtherUserId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
    }

    // ───── F4: Concurrency conflict ─────

    [Fact]
    public async Task Handle_ConcurrencyConflict_ReturnsConflictError()
    {
        var instance = CreateInstance(TenantA);
        SetupInstanceFound(instance);
        SetupBranchStatusFound();
        SetupTaskWithAssignee(AssigneeId);

        _branchService.ResolveBranchChildAsync(
                Arg.Any<WorkflowInstanceBranchStatus>(), BranchChildId,
                BranchResolutionType.Approved, AssigneeId, null, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<(bool, long?)>(new ConflictError("CONCURRENCY_CONFLICT")));

        var command = CreateCommand(tenantId: TenantA, resolvedBy: AssigneeId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ConflictError);
    }

    // ───── Happy path ─────

    [Fact]
    public async Task Handle_ValidRequest_ResolvesSuccessfully()
    {
        var instance = CreateInstance(TenantA);
        SetupInstanceFound(instance);
        SetupBranchStatusFound();
        SetupTaskWithAssignee(AssigneeId);
        SetupResolveSuccess(joinComplete: false);

        var command = CreateCommand(tenantId: TenantA, resolvedBy: AssigneeId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _branchService.Received(1).ResolveBranchChildAsync(
            Arg.Any<WorkflowInstanceBranchStatus>(), BranchChildId,
            BranchResolutionType.Approved, AssigneeId, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_JoinComplete_TransitionsInstance()
    {
        var instance = CreateInstance(TenantA);
        SetupInstanceFound(instance);
        SetupBranchStatusFound();
        SetupTaskWithAssignee(AssigneeId);
        SetupResolveSuccess(joinComplete: true, ContinuationStateId);
        _instanceRepo.UpdateWithStateGuardAsync(instance, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        // Slice-5: continuation pre-check requires a resolved pool for non-final continuation state
        SetupDefinitionWithContinuationState(isFinal: false);
        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == ContinuationStateId),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Pool([
                new CandidatePredicate(CandidatePrincipalType.Permission, "ANY_PERM", AssignmentCombineMode.Union)
            ]));

        var command = CreateCommand(tenantId: TenantA, resolvedBy: AssigneeId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        instance.CurrentStateId.Should().Be(ContinuationStateId);
    }

    [Fact]
    public async Task Handle_AlreadyResolved_ReturnsFail()
    {
        var instance = CreateInstance(TenantA);
        SetupInstanceFound(instance);

        var branchStatus = CreateBranchStatusWithResolvedChild();
        _branchRepo.GetInstanceBranchStatusAsync(InstancePublicId, Arg.Any<CancellationToken>())
            .Returns(branchStatus);

        var command = CreateCommand(tenantId: TenantA, resolvedBy: AssigneeId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("already resolved"));
    }

    // ───── Slice-5: Continuation pre-check (cases f, g) ─────

    /// <summary>
    /// Case (f): resolver returns WF_NO_HANDLER for continuation state → 422 ValidationError.
    /// Join is complete but continuation state has no eligible handler.
    /// </summary>
    [Fact]
    public async Task Handle_JoinComplete_ContinuationResolverEmpty_ReturnsValidationError()
    {
        var instance = CreateInstance(TenantA);
        SetupInstanceFound(instance);
        SetupBranchStatusFound();
        SetupTaskWithAssignee(AssigneeId);
        SetupResolveSuccess(joinComplete: true, ContinuationStateId);
        _instanceRepo.UpdateWithStateGuardAsync(instance, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        // Definition returns a def with continuation state (non-final)
        SetupDefinitionWithContinuationState(isFinal: false);
        // Resolver returns empty for continuation state
        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == ContinuationStateId),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Empty);

        var command = CreateCommand(tenantId: TenantA, resolvedBy: AssigneeId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        result.Errors.Should().ContainSingle(
            e => e.Metadata.ContainsKey("WorkflowErrorCode")
              && (string)e.Metadata["WorkflowErrorCode"] == WorkflowErrorCodes.NoHandlerResolved);
    }

    /// <summary>
    /// Case (g): resolver throws for continuation state → fail-CLOSED: WF_NO_HANDLER (422).
    /// Exception must not leak to caller.
    /// </summary>
    [Fact]
    public async Task Handle_JoinComplete_ContinuationResolverThrows_FailClosed_ValidationError()
    {
        var instance = CreateInstance(TenantA);
        SetupInstanceFound(instance);
        SetupBranchStatusFound();
        SetupTaskWithAssignee(AssigneeId);
        SetupResolveSuccess(joinComplete: true, ContinuationStateId);
        _instanceRepo.UpdateWithStateGuardAsync(instance, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        SetupDefinitionWithContinuationState(isFinal: false);
        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == ContinuationStateId),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Throws(new NotSupportedException("PreviousHandler not implemented — INTERNAL"));

        var command = CreateCommand(tenantId: TenantA, resolvedBy: AssigneeId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        result.Errors.Should().NotContain(e => e.Message.Contains("INTERNAL"));
    }

    /// <summary>
    /// Happy path: join complete, resolver satisfied → success.
    /// </summary>
    [Fact]
    public async Task Handle_JoinComplete_ContinuationResolverSatisfied_Succeeds()
    {
        var instance = CreateInstance(TenantA);
        SetupInstanceFound(instance);
        SetupBranchStatusFound();
        SetupTaskWithAssignee(AssigneeId);
        SetupResolveSuccess(joinComplete: true, ContinuationStateId);
        _instanceRepo.UpdateWithStateGuardAsync(instance, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        SetupDefinitionWithContinuationState(isFinal: false);
        _resolver.ResolveAsync(
                Arg.Is<WorkflowState>(s => s.Id == ContinuationStateId),
                Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.Pool([
                new CandidatePredicate(CandidatePrincipalType.Permission, "ANY_PERM", AssignmentCombineMode.Union)
            ]));

        var command = CreateCommand(tenantId: TenantA, resolvedBy: AssigneeId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        instance.CurrentStateId.Should().Be(ContinuationStateId);
    }

    /// <summary>
    /// M1 fix: definition load FAILURE on continuation pre-check → fail-CLOSED (WF_NO_HANDLER).
    /// Previously: defResult.IsFailed caused the entire pre-check block to be skipped,
    /// so ApplyParallelJoinTransition was called without any handler validation = fail-OPEN.
    /// After fix: IsFailed → NoHandlerResolved (422) immediately; ApplyParallelJoinTransition NOT called.
    /// </summary>
    [Fact]
    public async Task Handle_JoinComplete_DefinitionLoadFails_FailsClosed_NoHandlerResolved()
    {
        var instance = CreateInstance(TenantA);
        SetupInstanceFound(instance);
        SetupBranchStatusFound();
        SetupTaskWithAssignee(AssigneeId);
        SetupResolveSuccess(joinComplete: true, ContinuationStateId);

        // Definition load FAILS (transient DB error / definition missing)
        _defRepo.GetWithStatesAndTransitionsByInternalIdAsync(
                Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<WorkflowDefinition>("Definition not found"));

        var command = CreateCommand(tenantId: TenantA, resolvedBy: AssigneeId);
        var result = await _handler.Handle(command, CancellationToken.None);

        // Must fail-CLOSED with NoHandlerResolved (422 ValidationError)
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        result.Errors.Should().ContainSingle(
            e => e.Metadata.ContainsKey("WorkflowErrorCode")
              && (string)e.Metadata["WorkflowErrorCode"] == WorkflowErrorCodes.NoHandlerResolved);

        // ApplyParallelJoinTransition must NOT be invoked (instance state unchanged)
        await _instanceRepo.DidNotReceiveWithAnyArgs().UpdateWithStateGuardAsync(default!, default);
    }

    /// <summary>
    /// Final continuation state: resolver not invoked, no pre-check needed. The instance is
    /// stamped completed (CompletedAt). Entity here is "Case" (non-FormSubmission) so NO
    /// finalization integration event is raised — RaiseFinalizationEvent guards on FormSubmission.
    /// </summary>
    [Fact]
    public async Task Handle_JoinComplete_ContinuationIsFinal_SkipsResolverAndSucceeds()
    {
        var instance = CreateInstance(TenantA);
        SetupInstanceFound(instance);
        SetupBranchStatusFound();
        SetupTaskWithAssignee(AssigneeId);
        SetupResolveSuccess(joinComplete: true, ContinuationStateId);
        _instanceRepo.UpdateWithStateGuardAsync(instance, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        SetupDefinitionWithContinuationState(isFinal: true); // final → no resolver call

        var command = CreateCommand(tenantId: TenantA, resolvedBy: AssigneeId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default!, default!, default);

        // Join-to-final marks the run terminal even for non-form entities …
        instance.CompletedAt.Should().NotBeNull();
        // … but no FormSubmission finalization event is raised for a "Case" entity.
        instance.DomainEvents.OfType<WorkflowInstanceFinalizedIntegrationEvent>().Should().BeEmpty();
    }

    /// <summary>
    /// Final continuation on a FormSubmission-linked instance: the join path must raise
    /// WorkflowInstanceFinalizedIntegrationEvent (routed through the outbox by OutboxInterceptor)
    /// so the linked submission's status is updated — the gap WF-PARALLEL-JOIN-FINAL-NO-STATUS-WRITE
    /// closed. Mirrors the direct-transition path. Also stamps CompletedAt.
    /// </summary>
    [Fact]
    public async Task Handle_JoinComplete_ContinuationIsFinal_FormSubmission_RaisesFinalizationEvent()
    {
        var formSubmissionId = Guid.NewGuid();
        var instance = WorkflowInstance.Create(
            TestGuidGenerator.Create(), 1L, 2L, "FormSubmission", formSubmissionId, TenantA, Guid.NewGuid());
        SetupInstanceFound(instance);
        SetupBranchStatusFound();
        SetupTaskWithAssignee(AssigneeId);
        SetupResolveSuccess(joinComplete: true, ContinuationStateId);
        _instanceRepo.UpdateWithStateGuardAsync(instance, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        SetupDefinitionWithContinuationState(isFinal: true); // state name = "Continuation"

        var command = CreateCommand(tenantId: TenantA, resolvedBy: AssigneeId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        instance.CompletedAt.Should().NotBeNull();

        var evt = instance.DomainEvents.OfType<WorkflowInstanceFinalizedIntegrationEvent>().Single();
        evt.EntityType.Should().Be("FormSubmission");
        evt.EntityId.Should().Be(formSubmissionId);
        evt.FinalStateName.Should().Be("Continuation");
        evt.TenantId.Should().Be(TenantA);
        evt.TransitionedBy.Should().Be(AssigneeId);
    }

    /// <summary>
    /// Non-final continuation: the instance is NOT completed and no finalization event fires
    /// (regression guard for the common branch-join case). Resolver IS invoked to stage next work.
    /// </summary>
    [Fact]
    public async Task Handle_JoinComplete_ContinuationNonFinal_DoesNotCompleteOrFinalize()
    {
        var instance = WorkflowInstance.Create(
            TestGuidGenerator.Create(), 1L, 2L, "FormSubmission", Guid.NewGuid(), TenantA, Guid.NewGuid());
        SetupInstanceFound(instance);
        SetupBranchStatusFound();
        SetupTaskWithAssignee(AssigneeId);
        SetupResolveSuccess(joinComplete: true, ContinuationStateId);
        _instanceRepo.UpdateWithStateGuardAsync(instance, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        // Non-final continuation → resolver returns a satisfied resolution so the join is not blocked.
        _resolver.ResolveAsync(
                Arg.Any<WorkflowState>(), Arg.Any<IReadOnlyList<OutgoingAssignment>>(),
                Arg.Any<WorkflowResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(HandlerResolution.ToAssignee(AssigneeId));

        SetupDefinitionWithContinuationState(isFinal: false);

        var command = CreateCommand(tenantId: TenantA, resolvedBy: AssigneeId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        instance.CompletedAt.Should().BeNull();
        instance.DomainEvents.OfType<WorkflowInstanceFinalizedIntegrationEvent>().Should().BeEmpty();
    }

    // ───── Helpers ─────

    private static ResolveBranchChildCommand CreateCommand(Guid tenantId, Guid resolvedBy) =>
        // InstanceId = PublicId (Guid); ChildStatusId = long
        new(InstancePublicId, ChildStatusId, BranchResolutionType.Approved, resolvedBy, null, tenantId);

    private static WorkflowInstance CreateInstance(Guid tenantId)
    {
        var gen = TestGuidGenerator.Create();
        return WorkflowInstance.Create(gen, 1L, 2L, "Case", Guid.NewGuid(), tenantId, Guid.NewGuid());
    }

    private void SetupInstanceFound(WorkflowInstance instance) =>
        _instanceRepo.GetWithHistoryAsync(InstancePublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(instance));

    private void SetupBranchStatusFound()
    {
        // WorkflowInstanceBranchStatus.InstanceId = internal long Id (not PublicId)
        var status = WorkflowInstanceBranchStatus.Create(InstanceInternalId, ParallelBranchId);
        var child = WorkflowBranchChildStatus.Create(status.Id, BranchChildId);
        typeof(WorkflowBranchChildStatus).GetProperty("Id")!.SetValue(child, ChildStatusId);
        status.AddChildStatus(child);
        // GetInstanceBranchStatusAsync takes Guid instancePublicId. Match any id: the handler queries by
        // request.InstanceId while the shared join-completion re-reads by the loaded instance.PublicId for
        // its winner-election (these are the same value in production; the fixture's mocked instance carries
        // a gen-assigned PublicId distinct from the command constant, so accept either).
        _branchRepo.GetInstanceBranchStatusAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(status);
    }

    private static WorkflowInstanceBranchStatus CreateBranchStatusWithResolvedChild()
    {
        var status = WorkflowInstanceBranchStatus.Create(InstanceInternalId, ParallelBranchId);
        var child = WorkflowBranchChildStatus.Create(status.Id, BranchChildId);
        typeof(WorkflowBranchChildStatus).GetProperty("Id")!.SetValue(child, ChildStatusId);
        child.Resolve(BranchResolutionType.Approved, Guid.NewGuid(), "Already resolved");
        status.AddChildStatus(child);
        return status;
    }

    private void SetupTaskWithAssignee(Guid assigneeId)
    {
        // CreateForBranchChild(long workflowInstanceId, long branchChildStatusId, ...)
        var task = WorkflowTask.CreateForBranchChild(
            1L, ChildStatusId, "Approval", null,
            WorkflowTaskPriority.Normal, null, TenantA, Guid.NewGuid());
        task.Assign(assigneeId, Guid.NewGuid());
        _taskRepo.GetByBranchChildStatusIdAsync(ChildStatusId, Arg.Any<CancellationToken>())
            .Returns(task);
    }

    private void SetupResolveSuccess(bool joinComplete, long? continuationStateId = null) =>
        _branchService.ResolveBranchChildAsync(
                Arg.Any<WorkflowInstanceBranchStatus>(), BranchChildId,
                BranchResolutionType.Approved, Arg.Any<Guid>(), null, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<(bool, long?)>((joinComplete, continuationStateId)));

    /// <summary>
    /// Sets up the definition repository with a definition containing the ContinuationState (Id=50L).
    /// </summary>
    private void SetupDefinitionWithContinuationState(bool isFinal)
    {
        var gen = TestGuidGenerator.Create();
        var def = WorkflowDefinition.Create(gen, "Def", "Desc", Guid.NewGuid(), Guid.NewGuid());
        var state = WorkflowState.Create(def.Id, "Continuation", "Tiếp tục", "Continuation",
            isInitial: false, isFinal: isFinal, "#808080", 0);
        typeof(AqtCoreFW.SharedKernel.Domain.Entity)
            .GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!
            .SetValue(state, ContinuationStateId);
        def.AddState(state);
        _defRepo.GetWithStatesAndTransitionsByInternalIdAsync(
                Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(def));
    }
}
