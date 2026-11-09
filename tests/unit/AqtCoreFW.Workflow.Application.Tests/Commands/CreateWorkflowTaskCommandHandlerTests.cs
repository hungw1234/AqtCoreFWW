using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowTask;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateWorkflowTaskCommandHandler.
/// Phase-07 PR-2: command now takes WorkflowInstancePublicId (Guid T2 AK);
/// handler resolves via IWorkflowInstanceRepository.GetByIdAsync → internal long Id.
/// </summary>
public sealed class CreateWorkflowTaskCommandHandlerTests
{
    private readonly IWorkflowTaskRepository _repository;
    private readonly IWorkflowInstanceRepository _instanceRepository;
    private readonly CreateWorkflowTaskCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstancePublicId = Guid.NewGuid();
    private static readonly Guid AssigneeId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();

    /// <summary>
    /// WorkflowInstance created via factory — Id == 0L pre-SaveChanges (DB-assigned IDENTITY).
    /// Used for mock returns and assertion comparisons.
    /// </summary>
    private static readonly WorkflowInstance FakeInstance = WorkflowInstance.Create(
        TestGuidGenerator.Create(),
        definitionId: 1L,
        initialStateId: 1L,
        entityType: "Case",
        entityId: Guid.NewGuid(),
        tenantId: TenantId,
        createdBy: Guid.NewGuid());

    public CreateWorkflowTaskCommandHandlerTests()
    {
        _repository = Substitute.For<IWorkflowTaskRepository>();
        _instanceRepository = Substitute.For<IWorkflowInstanceRepository>();

        // Default: resolve InstancePublicId → FakeInstance
        _instanceRepository
            .GetByIdAsync(InstancePublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(FakeInstance));

        _handler = new CreateWorkflowTaskCommandHandler(_repository, _instanceRepository);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <param name="omitAssignee">When true, AssigneeId is set to null (no assignee scenario).</param>
    private static CreateWorkflowTaskCommand BuildCommand(
        Guid? instancePublicId = null,
        string title = "Review Document",
        string? description = "Mandatory review",
        bool omitAssignee = false,
        WorkflowTaskPriority priority = WorkflowTaskPriority.High,
        DateTime? dueDate = null) =>
        new(
            WorkflowInstancePublicId: instancePublicId ?? InstancePublicId,
            Title: title,
            Description: description,
            AssigneeId: omitAssignee ? null : AssigneeId,
            Priority: priority,
            DueDate: dueDate ?? DateTime.UtcNow.AddDays(7),
            TenantId: TenantId,
            CreatedBy: CreatedBy);

    // -------------------------------------------------------------------------
    // Success path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithDto()
    {
        var result = await _handler.Handle(BuildCommand(title: "Review Document"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Title.Should().Be("Review Document");
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsViaRepository()
    {
        var command = BuildCommand(title: "Approve Request", description: null,
            priority: WorkflowTaskPriority.Normal, dueDate: null);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<WorkflowTask>(t => t.Title == "Approve Request"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_MapsAllProperties()
    {
        var dueDate = DateTime.UtcNow.AddDays(3);
        var command = BuildCommand(title: "Test Task", description: "Test Description",
            priority: WorkflowTaskPriority.Urgent, dueDate: dueDate);
        // AssigneeId defaults to static AssigneeId (omitAssignee = false)

        var result = await _handler.Handle(command, CancellationToken.None);

        // Phase-07 PR-D2: WorkflowInstanceId in DTO = FakeInstance.PublicId (Guid T2 AK)
        result.Value.WorkflowInstanceId.Should().Be(FakeInstance.PublicId);
        result.Value.AssigneeId.Should().Be(AssigneeId);
        result.Value.Priority.Should().Be(WorkflowTaskPriority.Urgent);
        result.Value.Description.Should().Be("Test Description");
    }

    [Fact]
    public async Task Handle_TaskCreatedWithOpenStatus()
    {
        var command = BuildCommand(title: "New Task", description: null,
            omitAssignee: true, priority: WorkflowTaskPriority.Low, dueDate: null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Status.Should().Be(WorkflowTaskStatus.Open);
    }

    [Fact]
    public async Task Handle_NoAssignee_CreatesPendingTask()
    {
        var command = BuildCommand(title: "Unassigned Task", description: "For later assignment",
            omitAssignee: true, priority: WorkflowTaskPriority.Normal, dueDate: null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AssigneeId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RepositoryException_Propagates()
    {
        _repository
            .AddAsync(Arg.Any<WorkflowTask>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB error")));

        var act = async () => await _handler.Handle(BuildCommand(title: "Task"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB error*");
    }

    // -------------------------------------------------------------------------
    // Failure path — instance not found (new test per plan Phase-01)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_InstanceNotFound_ReturnsFailResult()
    {
        var missingPublicId = Guid.NewGuid();
        _instanceRepository
            .GetByIdAsync(missingPublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<WorkflowInstance>($"WorkflowInstance '{missingPublicId}' not found."));

        var command = BuildCommand(instancePublicId: missingPublicId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("not found"));

        // AddAsync must not be called when lookup fails
        await _repository.DidNotReceive().AddAsync(Arg.Any<WorkflowTask>(), Arg.Any<CancellationToken>());
    }
}
