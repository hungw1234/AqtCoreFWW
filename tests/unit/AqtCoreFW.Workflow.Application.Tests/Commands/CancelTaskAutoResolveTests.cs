using AqtCoreFW.Workflow.Application.Commands.CancelTask;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Tests for CancelTaskCommandHandler auto-resolve branch child paths.
/// Covers: branch link → Rejected resolution, reason passed as comment,
/// join complete → transition, already-resolved skip, exception handling.
/// Phase-4b B4c:
/// - WorkflowTask is T1: Id=long (0 in unit tests). TaskId in command is long.
/// - WorkflowInstanceBranchStatus.Create(long instanceId, long parallelBranchId).
/// - WorkflowBranchChildStatus.Create(long instanceBranchStatusId, long branchChildId).
/// - GetBranchStatusByChildStatusIdAsync(long childStatusInternalId).
/// - ResolveBranchChildAsync(branchStatus, long childId, ...) → Result&lt;(bool, long?)&gt;.
/// - continuationStateId is long?.
/// - GetWithHistoryByInternalIdAsync(long internalId) used by branch-join path only.
/// - GetPublicIdByInternalIdAsync(long internalId) used by DTO projection path (Phase-07 PR-D2 M1).
/// </summary>
public sealed class CancelTaskAutoResolveTests
{
    private readonly IWorkflowTaskRepository _taskRepo;
    private readonly IParallelBranchRepository _branchRepo;
    private readonly IParallelBranchService _branchService;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IBranchJoinCompletionService _joinCompletion;
    private readonly CancelTaskCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CancelledBy = Guid.NewGuid();

    // T1 long IDs for branch entities
    private const long TaskId = 0L;              // WorkflowTask.Id (DB-assigned = 0 in tests)
    private const long InstanceInternalId = 10L;  // WorkflowInstance.Id (internal)
    private const long ParallelBranchId = 20L;    // WorkflowParallelBranch.Id
    private const long ChildStatusId = 30L;        // WorkflowBranchChildStatus.Id
    private const long BranchChildId = 40L;        // WorkflowBranchChild.Id
    private const long ContinuationStateId = 50L;  // WorkflowState.Id

    public CancelTaskAutoResolveTests()
    {
        _taskRepo = Substitute.For<IWorkflowTaskRepository>();
        _branchRepo = Substitute.For<IParallelBranchRepository>();
        _branchService = Substitute.For<IParallelBranchService>();
        _instanceRepo = Substitute.For<IWorkflowInstanceRepository>();

        // Phase-07 PR-D2 M2: lightweight projection stub for DTO path.
        // GetWithHistoryByInternalIdAsync stubs are set per-test only for the branch-join path.
        _instanceRepo.GetPublicIdByInternalIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        // Join-completion is a collaborator now (continuation logic lives in BranchJoinCompletionService).
        // Assert the auto-resolve path DELEGATES to it. Default to success so the happy path doesn't loud-log.
        _joinCompletion = Substitute.For<IBranchJoinCompletionService>();
        _joinCompletion.CompleteJoinAsync(
                Arg.Any<WorkflowInstance>(), Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        _handler = new CancelTaskCommandHandler(
            _taskRepo, _branchRepo, _branchService, _instanceRepo,
            _joinCompletion,
            Substitute.For<ILogger<CancelTaskCommandHandler>>());
    }

    // --- Auto-resolve triggers as Rejected ---

    [Fact]
    public async Task Handle_WithBranchChild_ResolvesAsRejected()
    {
        var task = CreateBranchLinkedTask();
        SetupTaskFound(task);
        var branchStatus = CreateBranchStatus();
        SetupBranchFound(branchStatus);
        SetupResolveResult(branchStatus, joinComplete: false);

        await _handler.Handle(CreateCommand("Budget rejected"), CancellationToken.None);

        await _branchService.Received(1).ResolveBranchChildAsync(
            branchStatus, BranchChildId, BranchResolutionType.Rejected,
            CancelledBy, "Budget rejected", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNullReason_PassesNullComment()
    {
        var task = CreateBranchLinkedTask();
        SetupTaskFound(task);
        var branchStatus = CreateBranchStatus();
        SetupBranchFound(branchStatus);
        SetupResolveResult(branchStatus, joinComplete: false, reason: null);

        await _handler.Handle(CreateCommand(null), CancellationToken.None);

        await _branchService.Received(1).ResolveBranchChildAsync(
            branchStatus, BranchChildId, BranchResolutionType.Rejected,
            CancelledBy, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_JoinComplete_AppliesTransition()
    {
        var task = CreateBranchLinkedTask();
        SetupTaskFound(task);
        var branchStatus = CreateBranchStatus();
        SetupBranchFound(branchStatus);
        SetupResolveResult(branchStatus, joinComplete: true, ContinuationStateId, reason: "Done");

        var gen = TestGuidGenerator.Create();
        var instance = WorkflowInstance.Create(
            gen, 1L, 2L, "Case", Guid.NewGuid(), TenantId, CancelledBy);
        // Handler uses GetWithHistoryByInternalIdAsync(branchStatus.InstanceId)
        _instanceRepo.GetWithHistoryByInternalIdAsync(InstanceInternalId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(instance));

        var result = await _handler.Handle(CreateCommand("Done"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Join satisfied → handler delegates the continuation to the shared join-completion service.
        await _joinCompletion.Received(1).CompleteJoinAsync(
            instance, ContinuationStateId, CancelledBy, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_JoinIncomplete_DoesNotTransition()
    {
        var task = CreateBranchLinkedTask();
        SetupTaskFound(task);
        var branchStatus = CreateBranchStatus();
        SetupBranchFound(branchStatus);
        SetupResolveResult(branchStatus, joinComplete: false);

        await _handler.Handle(CreateCommand("Not needed"), CancellationToken.None);

        // No join → handler must not delegate a continuation.
        await _joinCompletion.DidNotReceive().CompleteJoinAsync(
            Arg.Any<WorkflowInstance>(), Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // --- Null/missing branch status → skip ---

    [Fact]
    public async Task Handle_BranchStatusNull_SkipsResolve()
    {
        var task = CreateBranchLinkedTask();
        SetupTaskFound(task);
        _branchRepo.GetBranchStatusByChildStatusIdAsync(ChildStatusId, Arg.Any<CancellationToken>())
            .Returns((WorkflowInstanceBranchStatus?)null);

        var result = await _handler.Handle(CreateCommand("Skip"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _branchService.DidNotReceive().ResolveBranchChildAsync(
            Arg.Any<WorkflowInstanceBranchStatus>(), Arg.Any<long>(),
            Arg.Any<BranchResolutionType>(), Arg.Any<Guid>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // --- Already-resolved child → skip ---

    [Fact]
    public async Task Handle_ChildAlreadyResolved_SkipsResolve()
    {
        var task = CreateBranchLinkedTask();
        SetupTaskFound(task);
        var branchStatus = CreateBranchStatusWithResolvedChild();
        SetupBranchFound(branchStatus);

        var result = await _handler.Handle(CreateCommand("Late cancel"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _branchService.DidNotReceive().ResolveBranchChildAsync(
            Arg.Any<WorkflowInstanceBranchStatus>(), Arg.Any<long>(),
            Arg.Any<BranchResolutionType>(), Arg.Any<Guid>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // --- Exception is non-fatal ---

    [Fact]
    public async Task Handle_AutoResolveThrows_TaskStillCancelled()
    {
        var task = CreateBranchLinkedTask();
        SetupTaskFound(task);
        _branchRepo.GetBranchStatusByChildStatusIdAsync(ChildStatusId, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("DB error"));

        var result = await _handler.Handle(CreateCommand("Cancel"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(WorkflowTaskStatus.Cancelled);
    }

    // --- No branch link → no auto-resolve ---

    [Fact]
    public async Task Handle_NoBranchChild_SkipsAutoResolve()
    {
        // WorkflowTask.Create(long workflowInstanceId, ...) — no BranchChildStatusId
        var task = WorkflowTask.Create(
            1L, "Regular Task", null, Guid.NewGuid(),
            WorkflowTaskPriority.Normal, null, TenantId, Guid.NewGuid());
        SetupTaskFound(task);

        await _handler.Handle(CreateCommand("Not needed"), CancellationToken.None);

        await _branchRepo.DidNotReceive().GetBranchStatusByChildStatusIdAsync(
            Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    // --- Helpers ---

    private static CancelTaskCommand CreateCommand(string? reason) =>
        new(TaskId: TaskId, Reason: reason, CancelledBy: CancelledBy, TenantId: TenantId);

    /// <summary>Creates a task linked to a branch child. BranchChildStatusId = ChildStatusId (long).</summary>
    private static WorkflowTask CreateBranchLinkedTask() =>
        WorkflowTask.CreateForBranchChild(
            1L, ChildStatusId, "Branch Approval", "Approve branch",
            WorkflowTaskPriority.Normal, null, TenantId, Guid.NewGuid());

    private void SetupTaskFound(WorkflowTask task) =>
        _taskRepo.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

    /// <summary>
    /// Creates a branch status with one child whose Id = ChildStatusId and BranchChildId = BranchChildId.
    /// The handler looks up: branchStatus.ChildStatuses.FirstOrDefault(cs => cs.Id == task.BranchChildStatusId)
    /// then calls ResolveBranchChildAsync(..., childStatus.BranchChildId, ...).
    /// </summary>
    private static WorkflowInstanceBranchStatus CreateBranchStatus()
    {
        var status = WorkflowInstanceBranchStatus.Create(InstanceInternalId, ParallelBranchId);
        var child = WorkflowBranchChildStatus.Create(status.Id, BranchChildId);
        // Set child.Id to ChildStatusId so handler finds it via cs.Id == task.BranchChildStatusId
        typeof(WorkflowBranchChildStatus).GetProperty("Id")!.SetValue(child, ChildStatusId);
        status.AddChildStatus(child);
        return status;
    }

    private static WorkflowInstanceBranchStatus CreateBranchStatusWithResolvedChild()
    {
        var status = WorkflowInstanceBranchStatus.Create(InstanceInternalId, ParallelBranchId);
        var child = WorkflowBranchChildStatus.Create(status.Id, BranchChildId);
        typeof(WorkflowBranchChildStatus).GetProperty("Id")!.SetValue(child, ChildStatusId);
        child.Resolve(BranchResolutionType.Rejected, Guid.NewGuid(), "Already resolved");
        status.AddChildStatus(child);
        return status;
    }

    private void SetupBranchFound(WorkflowInstanceBranchStatus branchStatus) =>
        _branchRepo.GetBranchStatusByChildStatusIdAsync(ChildStatusId, Arg.Any<CancellationToken>())
            .Returns(branchStatus);

    private void SetupResolveResult(
        WorkflowInstanceBranchStatus branchStatus,
        bool joinComplete,
        long? continuationStateId = null,
        string? reason = "Budget rejected") =>
        _branchService.ResolveBranchChildAsync(
                branchStatus, BranchChildId, BranchResolutionType.Rejected,
                CancelledBy, reason, Arg.Any<CancellationToken>())
            .Returns(FluentResults.Result.Ok<(bool, long?)>((joinComplete, continuationStateId)));
}
