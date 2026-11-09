using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.ReassignTask;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for ReassignTaskCommandHandler.
/// Validates: success reassignment, task not found, DTO mapping.
/// F09 regression: TaskAssignment must be persisted via ITaskAssignmentRepository.AddAsync.
/// </summary>
public sealed class ReassignTaskCommandHandlerTests
{
    private readonly IWorkflowTaskRepository _repository;
    private readonly ITaskAssignmentRepository _assignmentRepository;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly ReassignTaskCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private const long TaskId = 1L;
    private static readonly Guid NewAssigneeId = Guid.NewGuid();
    private static readonly Guid ReassignedById = Guid.NewGuid();
    // Phase-07 PR-D2 M2: known PublicId for DTO assertion in happy-path tests.
    private static readonly Guid ExpectedInstancePublicId = Guid.NewGuid();

    public ReassignTaskCommandHandlerTests()
    {
        _repository = Substitute.For<IWorkflowTaskRepository>();
        _assignmentRepository = Substitute.For<ITaskAssignmentRepository>();
        _instanceRepo = Substitute.For<IWorkflowInstanceRepository>();

        // Phase-07 PR-D2 M2: lightweight projection stub — handler no longer calls GetWithHistoryByInternalIdAsync for DTO.
        _instanceRepo.GetPublicIdByInternalIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(ExpectedInstancePublicId);

        _handler = new ReassignTaskCommandHandler(
            _repository, _assignmentRepository, _instanceRepo);
    }

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccess()
    {
        var task = WorkflowTask.Create(
            1L, "Task", null, Guid.NewGuid(), WorkflowTaskPriority.Normal, null, TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new ReassignTaskCommand(
            TaskId: TaskId,
            NewAssigneeId: NewAssigneeId,
            ReassignedById: ReassignedById,
            Reason: "Reassigned due to workload",
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AssigneeId.Should().Be(NewAssigneeId);
        // Phase-07 PR-D2 M2: DTO must carry the resolved WorkflowInstance.PublicId
        result.Value.WorkflowInstanceId.Should().Be(ExpectedInstancePublicId);
    }

    [Fact]
    public async Task Handle_ValidCommand_UpdatesRepository()
    {
        var oldAssigneeId = Guid.NewGuid();
        var task = WorkflowTask.Create(
            1L, "Task", null, oldAssigneeId, WorkflowTaskPriority.Normal, null, TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new ReassignTaskCommand(
            TaskId: TaskId,
            NewAssigneeId: NewAssigneeId,
            ReassignedById: ReassignedById,
            Reason: "Load balancing",
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).UpdateAsync(
            Arg.Is<WorkflowTask>(t => t.AssigneeId == NewAssigneeId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ChangesAssigneeFromOldToNew_WithReason_Succeeds()
    {
        // Phase-4-A: admin stomp (task already claimed) requires a non-empty Reason.
        // Providing a reason → succeeds and changes assignee.
        var oldAssigneeId = Guid.NewGuid();
        var task = WorkflowTask.Create(
            1L, "Approve", null, oldAssigneeId, WorkflowTaskPriority.High,
            DateTime.UtcNow.AddDays(1), TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new ReassignTaskCommand(
            TaskId: TaskId,
            NewAssigneeId: NewAssigneeId,
            ReassignedById: ReassignedById,
            Reason: "PTO escalation",
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue("stomp with reason must succeed");
        result.Value.AssigneeId.Should().Be(NewAssigneeId);
        result.Value.AssigneeId.Should().NotBe(oldAssigneeId);
    }

    [Fact]
    public async Task Handle_ChangesAssigneeFromOldToNew_NullReason_ReturnsValidationError()
    {
        // Phase-4-A: admin stomp (task already claimed) with null Reason → ValidationError.
        // Guards against unauthorized mass-reassignment without audit trail.
        var oldAssigneeId = Guid.NewGuid();
        var task = WorkflowTask.Create(
            1L, "Approve", null, oldAssigneeId, WorkflowTaskPriority.High,
            DateTime.UtcNow.AddDays(1), TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new ReassignTaskCommand(
            TaskId: TaskId,
            NewAssigneeId: NewAssigneeId,
            ReassignedById: ReassignedById,
            Reason: null,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue("stomp without reason must fail");
        result.Errors.Should().ContainSingle(e =>
            e is AqtCoreFW.SharedKernel.Errors.ValidationError &&
            e.Message.Contains("Reason"),
            "error must identify Reason as the missing field");
    }

    [Fact]
    public async Task Handle_PreservesTaskMetadata()
    {
        var task = WorkflowTask.Create(
            1L, "Review Document", "Critical review required", Guid.NewGuid(),
            WorkflowTaskPriority.Urgent, DateTime.UtcNow.AddHours(2), TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new ReassignTaskCommand(
            TaskId: TaskId,
            NewAssigneeId: NewAssigneeId,
            ReassignedById: ReassignedById,
            Reason: "Reassignment",
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Title.Should().Be("Review Document");
        result.Value.Description.Should().Be("Critical review required");
        result.Value.Priority.Should().Be(WorkflowTaskPriority.Urgent);
    }

    // --- Failure scenarios ---

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsFailed()
    {
        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("Task not found")));

        var command = new ReassignTaskCommand(
            TaskId: TaskId,
            NewAssigneeId: NewAssigneeId,
            ReassignedById: ReassignedById,
            Reason: null,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Handle_TaskNotFound_DoesNotCallUpdate()
    {
        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("Task not found")));

        var command = new ReassignTaskCommand(
            TaskId: TaskId,
            NewAssigneeId: NewAssigneeId,
            ReassignedById: ReassignedById,
            Reason: null,
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.DidNotReceive().UpdateAsync(
            Arg.Any<WorkflowTask>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RepositoryThrows_ExceptionPropagates()
    {
        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns<Result<WorkflowTask>>(_ => throw new InvalidOperationException("DB connection failed"));

        var command = new ReassignTaskCommand(
            TaskId: TaskId,
            NewAssigneeId: NewAssigneeId,
            ReassignedById: ReassignedById,
            Reason: null,
            TenantId: TenantId);

        var act = async () => await _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB connection failed*");
    }

    // --- F09 Regression: TaskAssignment MUST be persisted ---

    [Fact]
    public async Task Handle_Reassign_PersistsTaskAssignmentRecord_F09Regression()
    {
        // Arrange: task exists — AssigneeId=null (unclaimed pool task → Type=Reassign, no reason required).
        // Phase-4-A: F09 regression still holds; test uses unclaimed task so Reason is optional.
        var task = WorkflowTask.Create(
            1L, "Task", null, assigneeId: null,
            WorkflowTaskPriority.Normal, null, TenantId, Guid.NewGuid());
        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new ReassignTaskCommand(
            TaskId: TaskId,
            NewAssigneeId: NewAssigneeId,
            ReassignedById: ReassignedById,
            Reason: "Workload rebalance",
            TenantId: TenantId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert: AddAsync MUST be called — pre-fix this was never called (F09 bug).
        // Unclaimed task → Type=Reassign (not ReassignAdmin).
        await _assignmentRepository.Received(1).AddAsync(
            Arg.Is<TaskAssignment>(a =>
                a.WorkflowTaskId == task.Id &&
                a.AssignedById == ReassignedById &&
                a.AssignedToId == NewAssigneeId &&
                a.Type == TaskAssignmentType.Reassign &&
                a.Reason == "Workload rebalance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AdminStomp_PersistsReassignAdminRecord()
    {
        // Phase-4-A: admin stomp (task already claimed) with reason → Type=ReassignAdmin.
        var existingAssigneeId = Guid.NewGuid();
        var task = WorkflowTask.Create(
            1L, "Task", null, assigneeId: existingAssigneeId,
            WorkflowTaskPriority.Normal, null, TenantId, Guid.NewGuid());
        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new ReassignTaskCommand(
            TaskId: TaskId,
            NewAssigneeId: NewAssigneeId,
            ReassignedById: ReassignedById,
            Reason: "Escalation override",
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        await _assignmentRepository.Received(1).AddAsync(
            Arg.Is<TaskAssignment>(a =>
                a.WorkflowTaskId == task.Id &&
                a.AssignedById == ReassignedById &&
                a.AssignedToId == NewAssigneeId &&
                a.Type == TaskAssignmentType.ReassignAdmin &&
                a.Reason == "Escalation override"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TaskNotFound_DoesNotPersistAssignment_F09Regression()
    {
        // Arrange: task NOT found
        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail("Task not found"));

        var command = new ReassignTaskCommand(
            TaskId: TaskId,
            NewAssigneeId: NewAssigneeId,
            ReassignedById: ReassignedById,
            Reason: null,
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        // Assert: no assignment persisted when task not found
        await _assignmentRepository.DidNotReceive().AddAsync(
            Arg.Any<TaskAssignment>(), Arg.Any<CancellationToken>());
    }

    // --- RT7: branch-child tasks are pool-claim only — manual reassign rejected ---

    [Fact]
    public async Task Handle_BranchChildTask_ReturnsValidationError()
    {
        var task = WorkflowTask.CreateForBranchChild(
            1L, 42L, "Branch child task", null, WorkflowTaskPriority.Normal, null, TenantId, Guid.NewGuid());
        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new ReassignTaskCommand(
            TaskId: TaskId,
            NewAssigneeId: NewAssigneeId,
            ReassignedById: ReassignedById,
            Reason: "manual reassign attempt",
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
    }

    [Fact]
    public async Task Handle_BranchChildTask_DoesNotMutateOrPersist()
    {
        var task = WorkflowTask.CreateForBranchChild(
            1L, 42L, "Branch child task", null, WorkflowTaskPriority.Normal, null, TenantId, Guid.NewGuid());
        _repository.GetByIdAsync(TaskId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var command = new ReassignTaskCommand(
            TaskId: TaskId,
            NewAssigneeId: NewAssigneeId,
            ReassignedById: ReassignedById,
            Reason: "manual reassign attempt",
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        // Guard fires before task.Assign() — assignee untouched, nothing persisted.
        task.AssigneeId.Should().BeNull();
        await _repository.DidNotReceive().UpdateAsync(
            Arg.Any<WorkflowTask>(), Arg.Any<CancellationToken>());
        await _assignmentRepository.DidNotReceive().AddAsync(
            Arg.Any<TaskAssignment>(), Arg.Any<CancellationToken>());
    }
}
