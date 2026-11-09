using AqtCoreFW.Workflow.Application.Commands.CompleteTask;
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
/// Tests for CompleteTaskCommandHandler auto-resolve branch child paths.
/// Phase-4b B4c:
/// - WorkflowTask is T1: Id=long (0 in unit tests). TaskId in command is long.
/// - BranchChildStatusId/ChildStatusId/BranchChildId: long (T1 FKs).
/// - ResolveBranchChildAsync → Result&lt;(bool, long?)&gt; (continuationStateId is long?).
/// - Handler uses GetWithHistoryByInternalIdAsync(long) for instance lookup.
/// </summary>
public sealed class CompleteTaskAutoResolveTests
{
    private readonly IWorkflowTaskRepository _taskRepo;
    private readonly IParallelBranchRepository _branchRepo;
    private readonly IParallelBranchService _branchService;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IBranchJoinCompletionService _joinCompletion;
    private readonly CompleteTaskCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CompletedBy = Guid.NewGuid();

    // T1 long IDs
    private const long TaskId = 0L;
    private const long InstanceInternalId = 10L;
    private const long ParallelBranchId = 20L;
    private const long ChildStatusId = 30L;
    private const long BranchChildId = 40L;
    private const long ContinuationStateId = 50L;

    public CompleteTaskAutoResolveTests()
    {
        _taskRepo = Substitute.For<IWorkflowTaskRepository>();
        _branchRepo = Substitute.For<IParallelBranchRepository>();
        _branchService = Substitute.For<IParallelBranchService>();
        _instanceRepo = Substitute.For<IWorkflowInstanceRepository>();

        // Phase-07 PR-D2 M2: lightweight projection stub for DTO path.
        // GetWithHistoryByInternalIdAsync stubs are set per-test only for the branch-join path.
        _instanceRepo.GetPublicIdByInternalIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        // Join-completion is a collaborator now (the continuation logic lives in BranchJoinCompletionService,
        // covered by its own + ResolveBranchChild tests). Here we assert the auto-resolve path DELEGATES to
        // it. Default to success so the happy path doesn't loud-log.
        _joinCompletion = Substitute.For<IBranchJoinCompletionService>();
        _joinCompletion.CompleteJoinAsync(
                Arg.Any<WorkflowInstance>(), Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        _handler = new CompleteTaskCommandHandler(
            _taskRepo, _branchRepo, _branchService, _instanceRepo,
            _joinCompletion,
            Substitute.For<ILogger<CompleteTaskCommandHandler>>());
    }

    // --- Auto-resolve triggers on linked branch child ---

    [Fact]
    public async Task Handle_WithBranchChild_ResolvesAsApproved()
    {
        var task = CreateBranchLinkedTask();
        SetupTaskFound(task);
        var branchStatus = CreateBranchStatus();
        SetupBranchFound(branchStatus);
        SetupResolveResult(branchStatus, joinComplete: false);

        await _handler.Handle(CreateCommand(), CancellationToken.None);

        await _branchService.Received(1).ResolveBranchChildAsync(
            branchStatus, BranchChildId, BranchResolutionType.Approved,
            CompletedBy, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_JoinComplete_AppliesTransition()
    {
        var task = CreateBranchLinkedTask();
        SetupTaskFound(task);
        var branchStatus = CreateBranchStatus();
        SetupBranchFound(branchStatus);
        SetupResolveResult(branchStatus, joinComplete: true, ContinuationStateId);

        var gen = TestGuidGenerator.Create();
        var instance = WorkflowInstance.Create(
            gen, 1L, 2L, "Case", Guid.NewGuid(), TenantId, CompletedBy);
        _instanceRepo.GetWithHistoryByInternalIdAsync(InstanceInternalId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(instance));

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Join satisfied → handler delegates the continuation to the shared join-completion service.
        await _joinCompletion.Received(1).CompleteJoinAsync(
            instance, ContinuationStateId, CompletedBy, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_JoinIncomplete_DoesNotTransition()
    {
        var task = CreateBranchLinkedTask();
        SetupTaskFound(task);
        var branchStatus = CreateBranchStatus();
        SetupBranchFound(branchStatus);
        SetupResolveResult(branchStatus, joinComplete: false);

        await _handler.Handle(CreateCommand(), CancellationToken.None);

        // No join → handler must not delegate a continuation.
        await _joinCompletion.DidNotReceive().CompleteJoinAsync(
            Arg.Any<WorkflowInstance>(), Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // --- Null/missing branch status → skip silently ---

    [Fact]
    public async Task Handle_BranchStatusNull_SkipsResolve()
    {
        var task = CreateBranchLinkedTask();
        SetupTaskFound(task);
        _branchRepo.GetBranchStatusByChildStatusIdAsync(ChildStatusId, Arg.Any<CancellationToken>())
            .Returns((WorkflowInstanceBranchStatus?)null);

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

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

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _branchService.DidNotReceive().ResolveBranchChildAsync(
            Arg.Any<WorkflowInstanceBranchStatus>(), Arg.Any<long>(),
            Arg.Any<BranchResolutionType>(), Arg.Any<Guid>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // --- Exception in auto-resolve is non-fatal ---

    [Fact]
    public async Task Handle_AutoResolveThrows_TaskStillCompletes()
    {
        var task = CreateBranchLinkedTask();
        SetupTaskFound(task);
        _branchRepo.GetBranchStatusByChildStatusIdAsync(ChildStatusId, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("DB error during branch lookup"));

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(WorkflowTaskStatus.Done);
    }

    // --- No branch link → no auto-resolve ---

    [Fact]
    public async Task Handle_NoBranchChild_SkipsAutoResolve()
    {
        var task = WorkflowTask.Create(
            1L, "Regular Task", null, Guid.NewGuid(),
            WorkflowTaskPriority.Normal, null, TenantId, Guid.NewGuid());
        SetupTaskFound(task);

        await _handler.Handle(CreateCommand(), CancellationToken.None);

        await _branchRepo.DidNotReceive().GetBranchStatusByChildStatusIdAsync(
            Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    // --- Helpers ---

    private static CompleteTaskCommand CreateCommand() =>
        new(TaskId: TaskId, CompletedBy: CompletedBy, TenantId: TenantId);

    private static WorkflowTask CreateBranchLinkedTask() =>
        WorkflowTask.CreateForBranchChild(
            1L, ChildStatusId, "Branch Approval", "Approve branch",
            WorkflowTaskPriority.Normal, null, TenantId, Guid.NewGuid());

    private void SetupTaskFound(WorkflowTask task) =>
        _taskRepo.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

    private static WorkflowInstanceBranchStatus CreateBranchStatus()
    {
        var status = WorkflowInstanceBranchStatus.Create(InstanceInternalId, ParallelBranchId);
        var child = WorkflowBranchChildStatus.Create(status.Id, BranchChildId);
        typeof(WorkflowBranchChildStatus).GetProperty("Id")!.SetValue(child, ChildStatusId);
        status.AddChildStatus(child);
        return status;
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

    private void SetupBranchFound(WorkflowInstanceBranchStatus branchStatus) =>
        _branchRepo.GetBranchStatusByChildStatusIdAsync(ChildStatusId, Arg.Any<CancellationToken>())
            .Returns(branchStatus);

    private void SetupResolveResult(
        WorkflowInstanceBranchStatus branchStatus,
        bool joinComplete,
        long? continuationStateId = null) =>
        _branchService.ResolveBranchChildAsync(
                branchStatus, BranchChildId, BranchResolutionType.Approved,
                CompletedBy, null, Arg.Any<CancellationToken>())
            .Returns(FluentResults.Result.Ok<(bool, long?)>((joinComplete, continuationStateId)));
}
