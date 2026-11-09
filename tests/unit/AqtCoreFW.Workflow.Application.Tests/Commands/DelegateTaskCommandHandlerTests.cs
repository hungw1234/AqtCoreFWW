using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.DelegateTask;
using AqtCoreFW.Workflow.Application.Commands.ReturnDelegatedTask;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// TDD tests for DelegateTaskCommandHandler and ReturnDelegatedTaskCommandHandler.
/// Covers: happy path, C3 cross-tenant isolation, C4 actor authz, H5 unassigned guard.
/// </summary>
public sealed class DelegateTaskCommandHandlerTests
{
    private readonly IWorkflowTaskRepository _repository;
    private readonly ITaskAssignmentRepository _assignmentRepository;
    private readonly IWorkflowInstanceRepository _instanceRepo;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private const long TaskId = 1L;
    private static readonly Guid OriginalAssigneeId = Guid.NewGuid();
    private static readonly Guid DelegateToId = Guid.NewGuid();
    private static readonly Guid ActorId = OriginalAssigneeId; // actor = assignee by default
    private static readonly Guid RandomUserId = Guid.NewGuid();
    // Phase-07 PR-D2 M2: known PublicId for DTO assertion in happy-path tests.
    private static readonly Guid ExpectedInstancePublicId = Guid.NewGuid();

    public DelegateTaskCommandHandlerTests()
    {
        _repository = Substitute.For<IWorkflowTaskRepository>();
        _assignmentRepository = Substitute.For<ITaskAssignmentRepository>();
        _instanceRepo = Substitute.For<IWorkflowInstanceRepository>();

        // Phase-07 PR-D2 M2: lightweight projection stub — handler no longer calls GetWithHistoryByInternalIdAsync for DTO.
        _instanceRepo.GetPublicIdByInternalIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(ExpectedInstancePublicId);
    }

    // Phase-07 PR-D2 M2: lightweight projection; DTO WorkflowInstanceId = ExpectedInstancePublicId.
    private DelegateTaskCommandHandler MakeDelegate() =>
        new(_repository, _assignmentRepository, _instanceRepo);

    private ReturnDelegatedTaskCommandHandler MakeReturn() =>
        new(_repository, _assignmentRepository, _instanceRepo);

    private WorkflowTask MakeTask(Guid? assigneeId = null, Guid? tenantId = null)
    {
        return WorkflowTask.Create(
            1L, "Task", null, assigneeId ?? OriginalAssigneeId,
            WorkflowTaskPriority.Normal, null, tenantId ?? TenantId, Guid.NewGuid());
    }

    private void SetupTaskFound(WorkflowTask task, Guid? tenantId = null)
    {
        _repository.GetByIdAndTenantAsync(TaskId, tenantId ?? TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));
    }

    private void SetupTaskNotFound(Guid? tenantId = null)
    {
        _repository.GetByIdAndTenantAsync(TaskId, tenantId ?? TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("not found")));
    }

    // ── DELEGATE — happy path ─────────────────────────────────────────────────

    [Fact]
    public async Task Delegate_ValidTask_AssigneeActor_ReturnsDtoWithDelegatedFromUserId()
    {
        var task = MakeTask();
        SetupTaskFound(task);

        var handler = MakeDelegate();
        var command = new DelegateTaskCommand(
            TaskId, DelegateToId, DateTimeOffset.UtcNow.AddDays(3),
            "going on leave", ActorId, TenantId, IsAdminOverride: false);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DelegatedFromUserId.Should().Be(OriginalAssigneeId);
        result.Value.AssigneeId.Should().Be(DelegateToId);
        // Phase-07 PR-D2 M2: DTO must carry the resolved WorkflowInstance.PublicId
        result.Value.WorkflowInstanceId.Should().Be(ExpectedInstancePublicId);
    }

    [Fact]
    public async Task Delegate_ValidTask_PersistsTaskAssignment()
    {
        var task = MakeTask();
        SetupTaskFound(task);

        var handler = MakeDelegate();
        var command = new DelegateTaskCommand(
            TaskId, DelegateToId, DateTimeOffset.UtcNow.AddDays(3),
            "leave", ActorId, TenantId, IsAdminOverride: false);

        await handler.Handle(command, CancellationToken.None);

        await _assignmentRepository.Received(1).AddAsync(
            Arg.Is<TaskAssignment>(a =>
                a.WorkflowTaskId == task.Id &&
                a.AssignedToId == DelegateToId &&
                a.Type == TaskAssignmentType.Delegate),
            Arg.Any<CancellationToken>());
    }

    // ── DELEGATE — C3: tenant isolation ──────────────────────────────────────

    [Fact]
    public async Task Delegate_CrossTenantTask_ReturnsNotFoundError()
    {
        // GetByIdAndTenantAsync with OtherTenantId returns not found
        _repository.GetByIdAndTenantAsync(TaskId, OtherTenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("not found")));

        var handler = MakeDelegate();
        var command = new DelegateTaskCommand(
            TaskId, DelegateToId, DateTimeOffset.UtcNow.AddDays(3),
            null, ActorId, OtherTenantId, IsAdminOverride: false);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _assignmentRepository.DidNotReceive().AddAsync(Arg.Any<TaskAssignment>(), Arg.Any<CancellationToken>());
    }

    // ── DELEGATE — C4: actor authorization ───────────────────────────────────

    [Fact]
    public async Task Delegate_AdminOverride_NotAssignee_Succeeds()
    {
        var task = MakeTask(assigneeId: OriginalAssigneeId);
        SetupTaskFound(task);

        var handler = MakeDelegate();
        // RandomUserId is NOT the assignee, but IsAdminOverride = true
        var command = new DelegateTaskCommand(
            TaskId, DelegateToId, DateTimeOffset.UtcNow.AddDays(3),
            null, RandomUserId, TenantId, IsAdminOverride: true);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Delegate_RandomUser_NotAssignee_NoAdminOverride_ReturnsForbiddenError()
    {
        var task = MakeTask(assigneeId: OriginalAssigneeId);
        SetupTaskFound(task);

        var handler = MakeDelegate();
        var command = new DelegateTaskCommand(
            TaskId, DelegateToId, DateTimeOffset.UtcNow.AddDays(3),
            null, RandomUserId, TenantId, IsAdminOverride: false);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
    }

    // ── DELEGATE — H5: unassigned task guard ─────────────────────────────────

    [Fact]
    public async Task Delegate_UnassignedTask_ReturnsValidationError()
    {
        // Task created with no assignee — simulate null AssigneeId
        var task = WorkflowTask.Create(
            1L, "Unassigned Task", null, null,
            WorkflowTaskPriority.Normal, null, TenantId, Guid.NewGuid());
        SetupTaskFound(task);

        var handler = MakeDelegate();
        var command = new DelegateTaskCommand(
            TaskId, DelegateToId, DateTimeOffset.UtcNow.AddDays(3),
            null, ActorId, TenantId, IsAdminOverride: true);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        result.Errors[0].Message.Should().Contain("unassigned");
    }

    // ── DELEGATE — other failure cases ───────────────────────────────────────

    [Fact]
    public async Task Delegate_TaskNotFound_ReturnsNotFoundError()
    {
        SetupTaskNotFound();

        var handler = MakeDelegate();
        var command = new DelegateTaskCommand(
            TaskId, DelegateToId, DateTimeOffset.UtcNow.AddDays(3),
            null, ActorId, TenantId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }

    [Fact]
    public async Task Delegate_AlreadyDelegated_ReturnsValidationError()
    {
        var task = MakeTask();
        task.Delegate(OriginalAssigneeId, DelegateToId, ActorId);
        SetupTaskFound(task);

        var handler = MakeDelegate();
        // Actor is now DelegateToId (current assignee after delegation)
        var command = new DelegateTaskCommand(
            TaskId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(3),
            null, DelegateToId, TenantId, IsAdminOverride: true);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // ── RETURN DELEGATION — happy path ────────────────────────────────────────

    [Fact]
    public async Task Return_DelegatedTask_CurrentAssignee_RestoresOriginalAssignee()
    {
        var task = MakeTask();
        task.Delegate(OriginalAssigneeId, DelegateToId, ActorId);

        // DelegateToId is now the current assignee — actor = DelegateToId
        _repository.GetByIdAndTenantAsync(TaskId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var handler = MakeReturn();
        var command = new ReturnDelegatedTaskCommand(TaskId, DelegateToId, TenantId, IsAdminOverride: false);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AssigneeId.Should().Be(OriginalAssigneeId);
        result.Value.DelegatedFromUserId.Should().BeNull();
        // Phase-07 PR-D2 M2: DTO must carry the resolved WorkflowInstance.PublicId
        result.Value.WorkflowInstanceId.Should().Be(ExpectedInstancePublicId);
    }

    [Fact]
    public async Task Return_DelegatedTask_AdminOverride_NotCurrentAssignee_Succeeds()
    {
        var task = MakeTask();
        task.Delegate(OriginalAssigneeId, DelegateToId, ActorId);

        _repository.GetByIdAndTenantAsync(TaskId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var handler = MakeReturn();
        // RandomUserId is not the assignee, but admin override
        var command = new ReturnDelegatedTaskCommand(TaskId, RandomUserId, TenantId, IsAdminOverride: true);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Return_DelegatedTask_RandomUser_NoAdminOverride_ReturnsForbiddenError()
    {
        var task = MakeTask();
        task.Delegate(OriginalAssigneeId, DelegateToId, ActorId);

        _repository.GetByIdAndTenantAsync(TaskId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var handler = MakeReturn();
        var command = new ReturnDelegatedTaskCommand(TaskId, RandomUserId, TenantId, IsAdminOverride: false);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ForbiddenError);
    }

    [Fact]
    public async Task Return_CrossTenantTask_ReturnsNotFoundError()
    {
        _repository.GetByIdAndTenantAsync(TaskId, OtherTenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("not found")));

        var handler = MakeReturn();
        var command = new ReturnDelegatedTaskCommand(TaskId, ActorId, OtherTenantId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }

    [Fact]
    public async Task Return_NotDelegatedTask_ReturnsNoOpSuccess()
    {
        var task = MakeTask();
        _repository.GetByIdAndTenantAsync(TaskId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var handler = MakeReturn();
        var command = new ReturnDelegatedTaskCommand(TaskId, ActorId, TenantId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _assignmentRepository.DidNotReceive().AddAsync(
            Arg.Any<TaskAssignment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Return_DelegatedTask_PersistsReturnAssignment()
    {
        var task = MakeTask();
        task.Delegate(OriginalAssigneeId, DelegateToId, ActorId);

        _repository.GetByIdAndTenantAsync(TaskId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(task));

        var handler = MakeReturn();
        var command = new ReturnDelegatedTaskCommand(TaskId, DelegateToId, TenantId, IsAdminOverride: false);

        await handler.Handle(command, CancellationToken.None);

        await _assignmentRepository.Received(1).AddAsync(
            Arg.Is<TaskAssignment>(a =>
                a.WorkflowTaskId == task.Id &&
                a.Type == TaskAssignmentType.Return),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Return_TaskNotFound_ReturnsNotFoundError()
    {
        SetupTaskNotFound();

        var handler = MakeReturn();
        var command = new ReturnDelegatedTaskCommand(TaskId, ActorId, TenantId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }
}
