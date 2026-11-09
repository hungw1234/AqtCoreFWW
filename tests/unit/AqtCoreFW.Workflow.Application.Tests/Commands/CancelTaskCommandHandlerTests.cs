using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CancelTask;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for CancelTaskCommandHandler.
/// Validates: success cancellation, task not found, DTO mapping.
/// Phase-4b B4c: WorkflowTask is T1 — Id is long (0 in unit tests without DB).
/// IWorkflowTaskRepository.GetByIdAsync takes long.
/// CancelTaskCommandHandler ctor: (repo, branchRepo, branchService, instanceRepo, gen, logger).
/// </summary>
public sealed class CancelTaskCommandHandlerTests
{
    private readonly IWorkflowTaskRepository _repository;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly CancelTaskCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CancelledBy = Guid.NewGuid();

    // WorkflowTask.Id = 0 (long, DB-assigned). Command.TaskId must match.
    private const long TaskId = 0L;
    // Phase-07 PR-D2 M2: known PublicId for DTO assertion in happy-path tests.
    private static readonly Guid ExpectedInstancePublicId = Guid.NewGuid();

    public CancelTaskCommandHandlerTests()
    {
        _repository = Substitute.For<IWorkflowTaskRepository>();
        _instanceRepo = Substitute.For<IWorkflowInstanceRepository>();

        // Phase-07 PR-D2 M2: lightweight projection stub — handler no longer calls GetWithHistoryByInternalIdAsync for DTO.
        _instanceRepo.GetPublicIdByInternalIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(ExpectedInstancePublicId);

        _handler = new CancelTaskCommandHandler(
            _repository,
            Substitute.For<IParallelBranchRepository>(),
            Substitute.For<IParallelBranchService>(),
            _instanceRepo,
            Substitute.For<IBranchJoinCompletionService>(),
            Substitute.For<ILogger<CancelTaskCommandHandler>>());
    }

    // WorkflowTask.Create(long workflowInstanceId, ...)
    private static WorkflowTask MakeTask(
        string title = "Task",
        string? description = null,
        WorkflowTaskPriority priority = WorkflowTaskPriority.Normal) =>
        WorkflowTask.Create(1L, title, description, Guid.NewGuid(), priority, null, TenantId, Guid.NewGuid());

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccess()
    {
        var task = MakeTask();
        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new CancelTaskCommand(
            TaskId: TaskId,
            Reason: "No longer needed",
            CancelledBy: CancelledBy,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Phase-07 PR-D2 M2: DTO must carry the resolved WorkflowInstance.PublicId
        result.Value.WorkflowInstanceId.Should().Be(ExpectedInstancePublicId);
    }

    [Fact]
    public async Task Handle_ValidCommand_UpdatesRepository()
    {
        var task = MakeTask();
        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new CancelTaskCommand(
            TaskId: TaskId,
            Reason: "Cancelled",
            CancelledBy: CancelledBy,
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).UpdateAsync(
            Arg.Is<WorkflowTask>(t => t.Status == WorkflowTaskStatus.Cancelled),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithReason_StatusIsCancelled()
    {
        var task = MakeTask("Review", "Document review", WorkflowTaskPriority.High);
        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var reason = "Duplicate task already assigned";
        var command = new CancelTaskCommand(
            TaskId: TaskId,
            Reason: reason,
            CancelledBy: CancelledBy,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Status.Should().Be(WorkflowTaskStatus.Cancelled);
    }

    [Fact]
    public async Task Handle_WithoutReason_CancelSuccessfully()
    {
        var task = MakeTask();
        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new CancelTaskCommand(
            TaskId: TaskId,
            Reason: null,
            CancelledBy: CancelledBy,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_PreservesTaskMetadata()
    {
        var task = MakeTask("Approve Document", "Important approval", WorkflowTaskPriority.Urgent);
        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new CancelTaskCommand(
            TaskId: TaskId,
            Reason: "No longer applicable",
            CancelledBy: CancelledBy,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Title.Should().Be("Approve Document");
        // Cancel() appends " | Cancelled: {reason}" to the existing description
        result.Value.Description.Should().StartWith("Important approval");
        result.Value.Description.Should().Contain("No longer applicable");
        result.Value.Priority.Should().Be(WorkflowTaskPriority.Urgent);
    }

    // --- Failure scenarios ---

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsFailed()
    {
        _repository.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("Task not found")));

        var command = new CancelTaskCommand(
            TaskId: TaskId,
            Reason: null,
            CancelledBy: CancelledBy,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Handle_TaskNotFound_DoesNotCallUpdate()
    {
        _repository.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("Task not found")));

        var command = new CancelTaskCommand(
            TaskId: TaskId,
            Reason: null,
            CancelledBy: CancelledBy,
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.DidNotReceive().UpdateAsync(
            Arg.Any<WorkflowTask>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RepositoryThrows_ExceptionPropagates()
    {
        _repository.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns<Result<WorkflowTask>>(_ => throw new InvalidOperationException("DB error"));

        var command = new CancelTaskCommand(
            TaskId: TaskId,
            Reason: null,
            CancelledBy: CancelledBy,
            TenantId: TenantId);

        var act = async () => await _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB error*");
    }
}
