using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.AssignTask;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for AssignTaskCommandHandler.
/// Validates: success assignment, task not found, DTO mapping.
/// Phase-4b B4c: WorkflowTask is T1 — Id is long (0 in unit tests without DB).
/// IWorkflowTaskRepository.GetByIdAsync takes long.
/// </summary>
public sealed class AssignTaskCommandHandlerTests
{
    private readonly IWorkflowTaskRepository _repository;
    private readonly ITaskAssignmentRepository _assignmentRepository;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly AssignTaskCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AssigneeId = Guid.NewGuid();
    private static readonly Guid AssignedById = Guid.NewGuid();
    // Phase-07 PR-D2 M2: known PublicId for DTO assertion in happy-path tests.
    private static readonly Guid ExpectedInstancePublicId = Guid.NewGuid();

    public AssignTaskCommandHandlerTests()
    {
        _repository = Substitute.For<IWorkflowTaskRepository>();
        _assignmentRepository = Substitute.For<ITaskAssignmentRepository>();
        _instanceRepo = Substitute.For<IWorkflowInstanceRepository>();

        // Phase-07 PR-D2 M2: lightweight projection stub — handler no longer calls GetWithHistoryByInternalIdAsync for DTO.
        _instanceRepo.GetPublicIdByInternalIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(ExpectedInstancePublicId);

        _handler = new AssignTaskCommandHandler(
            _repository, _assignmentRepository, _instanceRepo);
    }

    // WorkflowTask.Create(long workflowInstanceId, ...) — instanceId is long.
    // Id is long (DB-assigned — 0 in unit tests).
    private static WorkflowTask MakeTask(string title = "Task", string? description = null) =>
        WorkflowTask.Create(1L, title, description, null, WorkflowTaskPriority.Normal, null, TenantId, Guid.NewGuid());

    // Branch-child task carries a non-null BranchChildStatusId — pool-claim only.
    private static WorkflowTask MakeBranchChildTask() =>
        WorkflowTask.CreateForBranchChild(1L, 42L, "Branch child task", null, WorkflowTaskPriority.Normal, null, TenantId, Guid.NewGuid());

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccess()
    {
        var task = MakeTask();
        _repository.GetByIdAsync(task.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new AssignTaskCommand(
            TaskId: task.Id,
            AssigneeId: AssigneeId,
            AssignedById: AssignedById,
            Reason: "Assigned by manager",
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AssigneeId.Should().Be(AssigneeId);
        // Phase-07 PR-D2 M2: DTO must carry the resolved WorkflowInstance.PublicId
        result.Value.WorkflowInstanceId.Should().Be(ExpectedInstancePublicId);
    }

    [Fact]
    public async Task Handle_ValidCommand_UpdatesRepository()
    {
        var task = MakeTask();
        _repository.GetByIdAsync(task.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new AssignTaskCommand(
            TaskId: task.Id,
            AssigneeId: AssigneeId,
            AssignedById: AssignedById,
            Reason: "Assignment",
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).UpdateAsync(
            Arg.Is<WorkflowTask>(t => t.AssigneeId == AssigneeId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_MapsTaskToDto()
    {
        var task = WorkflowTask.Create(
            1L, "Review", "Please review document", null,
            WorkflowTaskPriority.High, DateTime.UtcNow.AddDays(1), TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(task.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new AssignTaskCommand(
            TaskId: task.Id,
            AssigneeId: AssigneeId,
            AssignedById: AssignedById,
            Reason: null,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Title.Should().Be("Review");
        result.Value.Description.Should().Be("Please review document");
    }

    // --- Failure scenarios ---

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsFailed()
    {
        _repository.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("Task not found")));

        var command = new AssignTaskCommand(
            TaskId: 999L,
            AssigneeId: AssigneeId,
            AssignedById: AssignedById,
            Reason: null,
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

        var command = new AssignTaskCommand(
            TaskId: 999L,
            AssigneeId: AssigneeId,
            AssignedById: AssignedById,
            Reason: null,
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.DidNotReceive().UpdateAsync(
            Arg.Any<WorkflowTask>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RepositoryGetThrows_ExceptionPropagates()
    {
        _repository.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns<Result<WorkflowTask>>(_ => throw new InvalidOperationException("DB unavailable"));

        var command = new AssignTaskCommand(
            TaskId: 0L,
            AssigneeId: AssigneeId,
            AssignedById: AssignedById,
            Reason: null,
            TenantId: TenantId);

        var act = async () => await _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB unavailable*");
    }

    // --- F09 Regression: TaskAssignment MUST be persisted ---

    [Fact]
    public async Task Handle_Assign_PersistsTaskAssignmentRecord_F09Regression()
    {
        var task = MakeTask();
        _repository.GetByIdAsync(task.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new AssignTaskCommand(
            TaskId: task.Id,
            AssigneeId: AssigneeId,
            AssignedById: AssignedById,
            Reason: "Initial assignment",
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        await _assignmentRepository.Received(1).AddAsync(
            Arg.Is<TaskAssignment>(a =>
                a.WorkflowTaskId == task.Id &&
                a.AssignedById == AssignedById &&
                a.AssignedToId == AssigneeId &&
                a.Type == TaskAssignmentType.Initial &&
                a.Reason == "Initial assignment"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TaskNotFound_DoesNotPersistAssignment_F09Regression()
    {
        _repository.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail("Task not found"));

        var command = new AssignTaskCommand(
            TaskId: 999L,
            AssigneeId: AssigneeId,
            AssignedById: AssignedById,
            Reason: null,
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        await _assignmentRepository.DidNotReceive().AddAsync(
            Arg.Any<TaskAssignment>(), Arg.Any<CancellationToken>());
    }

    // --- RT7: branch-child tasks are pool-claim only — manual assign rejected ---

    [Fact]
    public async Task Handle_BranchChildTask_ReturnsValidationError()
    {
        var task = MakeBranchChildTask();
        _repository.GetByIdAsync(task.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new AssignTaskCommand(
            TaskId: task.Id,
            AssigneeId: AssigneeId,
            AssignedById: AssignedById,
            Reason: "manual assign attempt",
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
    }

    [Fact]
    public async Task Handle_BranchChildTask_DoesNotMutateOrPersist()
    {
        var task = MakeBranchChildTask();
        _repository.GetByIdAsync(task.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new AssignTaskCommand(
            TaskId: task.Id,
            AssigneeId: AssigneeId,
            AssignedById: AssignedById,
            Reason: null,
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        // Guard fires before task.Assign() — assignee stays null, nothing persisted.
        task.AssigneeId.Should().BeNull();
        await _repository.DidNotReceive().UpdateAsync(
            Arg.Any<WorkflowTask>(), Arg.Any<CancellationToken>());
        await _assignmentRepository.DidNotReceive().AddAsync(
            Arg.Any<TaskAssignment>(), Arg.Any<CancellationToken>());
    }
}
