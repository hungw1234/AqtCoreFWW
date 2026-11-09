using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.GetTasksByAssignee;
using AqtCoreFW.Workflow.Domain.Entities;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

using TaskRow = GetTasksByAssigneeQueryHandler.TaskRow;

/// <summary>
/// Unit tests for GetTasksByAssigneeQueryHandler.
/// Validates: returns tasks for assignee, filters by status, ordered by priority and due date.
/// Phase-07 PR-D2: TaskRow.WorkflowInstanceId changed long→Guid (T2 WorkflowInstance.PublicId AK).
/// </summary>
public sealed class GetTasksByAssigneeQueryHandlerTests
{
    private readonly IReadDbConnection _db;
    private readonly GetTasksByAssigneeQueryHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AssigneeId = Guid.NewGuid();
    private static readonly Guid FakeInstancePublicId = Guid.NewGuid();

    public GetTasksByAssigneeQueryHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _handler = new GetTasksByAssigneeQueryHandler(_db);
    }

    // --- Helpers ---

    private static long _taskIdCounter = 1L;

    private static TaskRow MakeRow(
        string title,
        Guid? assigneeId = null,
        WorkflowTaskStatus status = WorkflowTaskStatus.Open,
        WorkflowTaskPriority priority = WorkflowTaskPriority.Normal,
        DateTime? dueDate = null)
        => new()
        {
            Id = _taskIdCounter++,
            WorkflowInstanceId = FakeInstancePublicId,
            Title = title,
            Description = null,
            AssigneeId = assigneeId ?? AssigneeId,
            Status = (int)status,
            Priority = (int)priority,
            DueDate = dueDate,
            CompletedAt = null,
            CompletedBy = null,
            TenantId = TenantId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = Guid.NewGuid()
        };

    // --- Success path ---

    [Fact]
    public async Task Handle_WithAssignedTasks_ReturnsList()
    {
        var rows = new List<TaskRow>
        {
            MakeRow("Task 1", status: WorkflowTaskStatus.InProgress, priority: WorkflowTaskPriority.High),
            MakeRow("Task 2")
        };

        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>(rows));

        var result = await _handler.Handle(new GetTasksByAssigneeQuery(AssigneeId, TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_NoAssignedTasks_ReturnsEmpty()
    {
        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>([]));

        var result = await _handler.Handle(new GetTasksByAssigneeQuery(AssigneeId, TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PassesCorrectTenantAndAssignee()
    {
        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>([]));

        await _handler.Handle(new GetTasksByAssigneeQuery(AssigneeId, TenantId), CancellationToken.None);

        await _db.Received(1).QueryAsync<TaskRow>(
            Arg.Is<string>(sql => sql.Contains("AssigneeId") && sql.Contains("TenantId")),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FiltersActiveStatusOnly()
    {
        var rows = new List<TaskRow>
        {
            MakeRow("Pending Task",  priority: WorkflowTaskPriority.High),
            MakeRow("In Progress",   status:   WorkflowTaskStatus.InProgress)
        };

        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>(rows));

        var result = await _handler.Handle(new GetTasksByAssigneeQuery(AssigneeId, TenantId), CancellationToken.None);

        // Both Open (0) and InProgress (1) should be returned per SQL IN (0, 1)
        result.Value.Should().AllSatisfy(t =>
            t.Status.Should().BeOneOf(WorkflowTaskStatus.Open, WorkflowTaskStatus.InProgress));
    }

    [Fact]
    public async Task Handle_OrdersByPriorityDescDueDateAsc()
    {
        var rows = new List<TaskRow>
        {
            MakeRow("High Priority Soon",  priority: WorkflowTaskPriority.High, dueDate: DateTime.UtcNow.AddDays(1)),
            MakeRow("High Priority Later", priority: WorkflowTaskPriority.High, dueDate: DateTime.UtcNow.AddDays(3)),
            MakeRow("Low Priority",        priority: WorkflowTaskPriority.Low)
        };

        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>(rows));

        var result = await _handler.Handle(new GetTasksByAssigneeQuery(AssigneeId, TenantId), CancellationToken.None);

        result.Value.Should().HaveCount(3);
        result.Value.First().Priority.Should().Be(WorkflowTaskPriority.High);
    }

    [Fact]
    public async Task Handle_CancellationTokenPropagated()
    {
        var cts = new CancellationTokenSource();

        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>([]));

        await _handler.Handle(new GetTasksByAssigneeQuery(AssigneeId, TenantId), cts.Token);

        await _db.Received(1).QueryAsync<TaskRow>(
            Arg.Any<string>(), Arg.Any<object?>(), cts.Token);
    }

    [Fact]
    public async Task Handle_DatabaseException_Propagates()
    {
        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IEnumerable<TaskRow>>>(_ =>
                Task.FromException<IEnumerable<TaskRow>>(
                    new InvalidOperationException("Query failed")));

        var act = async () => await _handler.Handle(
            new GetTasksByAssigneeQuery(AssigneeId, TenantId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Query failed*");
    }
}
