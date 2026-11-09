using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.Queries.GetTasksByInstance;
using AqtCoreFW.Workflow.Domain.Entities;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

using TaskRow = GetTasksByInstanceQueryHandler.TaskRow;

/// <summary>
/// Unit tests for GetTasksByInstanceQueryHandler.
/// Validates: returns tasks for instance, empty result, SQL query execution.
/// Phase-07 PR-D2: TaskRow.WorkflowInstanceId changed long→Guid (T2 WorkflowInstance.PublicId AK).
/// </summary>
public sealed class GetTasksByInstanceQueryHandlerTests
{
    private readonly IReadDbConnection _db;
    private readonly IIdentityModuleClient _identity;
    private readonly IOrgUnitModuleClient _orgUnit;
    private readonly GetTasksByInstanceQueryHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid FakeInstancePublicId = Guid.NewGuid();

    public GetTasksByInstanceQueryHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _identity = Substitute.For<IIdentityModuleClient>();
        _orgUnit = Substitute.For<IOrgUnitModuleClient>();
        _handler = new GetTasksByInstanceQueryHandler(_db, _identity, _orgUnit);
    }

    // --- Helpers ---

    private static long _taskIdCounter = 1L;

    private static TaskRow MakeRow(
        string title,
        WorkflowTaskStatus status = WorkflowTaskStatus.Open,
        WorkflowTaskPriority priority = WorkflowTaskPriority.Normal,
        DateTimeOffset? createdAt = null)
        => new()
        {
            Id = _taskIdCounter++,
            WorkflowInstanceId = FakeInstancePublicId,
            Title = title,
            AssigneeId = Guid.NewGuid(),
            Status = (int)status,
            Priority = (int)priority,
            DueDate = null,
            CandidateRefsJson = null,
        };

    // --- Success path ---

    [Fact]
    public async Task Handle_WithTasks_ReturnsTaskList()
    {
        var rows = new List<TaskRow>
        {
            MakeRow("Review", status: WorkflowTaskStatus.InProgress, priority: WorkflowTaskPriority.High),
            MakeRow("Approve")
        };

        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>(rows));

        var result = await _handler.Handle(
            new GetTasksByInstanceQuery(InstanceId, TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_NoTasks_ReturnsEmptyList()
    {
        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>([]));

        var result = await _handler.Handle(
            new GetTasksByInstanceQuery(InstanceId, TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PassesCorrectParameters()
    {
        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>([]));

        await _handler.Handle(
            new GetTasksByInstanceQuery(InstanceId, TenantId), CancellationToken.None);

        await _db.Received(1).QueryAsync<TaskRow>(
            Arg.Is<string>(sql => sql.Contains("WorkflowTasks") && sql.Contains("WorkflowInstanceId")),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsMultipleTasksInOrder()
    {
        var rows = new List<TaskRow>
        {
            MakeRow("Task 1", status: WorkflowTaskStatus.Open,       priority: WorkflowTaskPriority.High,   createdAt: DateTimeOffset.UtcNow.AddDays(-2)),
            MakeRow("Task 2", status: WorkflowTaskStatus.InProgress, priority: WorkflowTaskPriority.Normal, createdAt: DateTimeOffset.UtcNow.AddDays(-1)),
            MakeRow("Task 3", status: WorkflowTaskStatus.Done,       priority: WorkflowTaskPriority.Low,    createdAt: DateTimeOffset.UtcNow)
        };

        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>(rows));

        var result = await _handler.Handle(
            new GetTasksByInstanceQuery(InstanceId, TenantId), CancellationToken.None);

        result.Value.Should().HaveCount(3);
        result.Value.Select(t => t.Title)
            .Should().ContainInOrder("Task 1", "Task 2", "Task 3");
    }

    [Fact]
    public async Task Handle_CancellationTokenPropagated()
    {
        var cts = new CancellationTokenSource();

        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>([]));

        await _handler.Handle(
            new GetTasksByInstanceQuery(InstanceId, TenantId), cts.Token);

        await _db.Received(1).QueryAsync<TaskRow>(
            Arg.Any<string>(), Arg.Any<object?>(), cts.Token);
    }

    [Fact]
    public async Task Handle_DatabaseException_Propagates()
    {
        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IEnumerable<TaskRow>>>(_ =>
                Task.FromException<IEnumerable<TaskRow>>(
                    new InvalidOperationException("Connection timeout")));

        var act = async () => await _handler.Handle(
            new GetTasksByInstanceQuery(InstanceId, TenantId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Connection timeout*");
    }

    // --- Enrichment: assignee name + candidate pool decode ---

    [Fact]
    public async Task Handle_AssignedTask_ResolvesAssigneeName()
    {
        var assignee = Guid.NewGuid();
        var row = new TaskRow
        {
            Id = 1,
            WorkflowInstanceId = FakeInstancePublicId,
            Title = "Review",
            AssigneeId = assignee,
            Status = (int)WorkflowTaskStatus.Open,
            Priority = (int)WorkflowTaskPriority.Normal,
            CandidateRefsJson = null,
        };
        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>([row]));
        _identity.GetUserInfoByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, UserInfoDto>>(
                new Dictionary<Guid, UserInfoDto> { [assignee] = new UserInfoDto { UserId = assignee, FullName = "Nguyễn Văn A" } }));

        var result = await _handler.Handle(
            new GetTasksByInstanceQuery(InstanceId, TenantId), CancellationToken.None);

        result.Value.Should().ContainSingle();
        result.Value[0].AssigneeName.Should().Be("Nguyễn Văn A");
        result.Value[0].Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PoolTask_DecodesCandidatesFromJson()
    {
        var row = new TaskRow
        {
            Id = 2,
            WorkflowInstanceId = FakeInstancePublicId,
            Title = "Submit",
            AssigneeId = null,
            Status = (int)WorkflowTaskStatus.Open,
            Priority = (int)WorkflowTaskPriority.Normal,
            CandidateRefsJson = "[{\"t\":1,\"r\":\"GOVOFFICER\",\"c\":0}]",
        };
        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>([row]));

        var result = await _handler.Handle(
            new GetTasksByInstanceQuery(InstanceId, TenantId), CancellationToken.None);

        result.Value.Should().ContainSingle();
        var dto = result.Value[0];
        dto.AssigneeName.Should().BeNull();          // no assignee → no identity lookup
        dto.Candidates.Should().ContainSingle();
        dto.Candidates[0].PrincipalType.Should().Be(1); // 1 = Role
        dto.Candidates[0].PrincipalRefId.Should().Be("GOVOFFICER");
    }

    [Fact]
    public async Task Handle_ActivePoolTask_ResolvesEligibleUsersFromRole()
    {
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var row = new TaskRow
        {
            Id = 3,
            WorkflowInstanceId = FakeInstancePublicId,
            Title = "Submit",
            AssigneeId = null,
            Status = (int)WorkflowTaskStatus.Open,
            Priority = (int)WorkflowTaskPriority.Normal,
            CandidateRefsJson = "[{\"t\":1,\"r\":\"GOVOFFICER\",\"c\":0}]",
        };
        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>([row]));
        _identity.ResolveActiveUsersInRolesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([u1, u2]));
        _identity.GetUserInfoByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, UserInfoDto>>(
                new Dictionary<Guid, UserInfoDto>
                {
                    [u1] = new UserInfoDto { UserId = u1, FullName = "Nguyễn Văn A" },
                    [u2] = new UserInfoDto { UserId = u2, FullName = "Trần Thị B" },
                }));

        var result = await _handler.Handle(
            new GetTasksByInstanceQuery(InstanceId, TenantId), CancellationToken.None);

        var dto = result.Value[0];
        dto.EligibleUserCount.Should().Be(2);
        dto.EligibleUsers.Should().HaveCount(2);
        dto.EligibleUsers.Select(u => u.FullName).Should().Contain(["Nguyễn Văn A", "Trần Thị B"]);
        dto.HasUnresolvedPermissionCandidate.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_PoolTaskWithPermissionCandidate_FlagsUnresolvedAndCountsOnlyResolved()
    {
        var u1 = Guid.NewGuid();
        var row = new TaskRow
        {
            Id = 4,
            WorkflowInstanceId = FakeInstancePublicId,
            Title = "Submit",
            AssigneeId = null,
            Status = (int)WorkflowTaskStatus.Open,
            Priority = (int)WorkflowTaskPriority.Normal,
            // Permission (t=0) + Role (t=1) candidates — permission users are NOT resolved.
            CandidateRefsJson = "[{\"t\":0,\"r\":\"WORKFLOW.TASK.PROCESS\",\"c\":0},{\"t\":1,\"r\":\"GOVOFFICER\",\"c\":0}]",
        };
        _db.QueryAsync<TaskRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TaskRow>>([row]));
        _identity.ResolveActiveUsersInRolesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([u1]));
        _identity.GetUserInfoByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, UserInfoDto>>(
                new Dictionary<Guid, UserInfoDto> { [u1] = new UserInfoDto { UserId = u1, FullName = "A" } }));

        var result = await _handler.Handle(
            new GetTasksByInstanceQuery(InstanceId, TenantId), CancellationToken.None);

        var dto = result.Value[0];
        dto.HasUnresolvedPermissionCandidate.Should().BeTrue(); // permission users not expanded → count is incomplete
        dto.EligibleUserCount.Should().Be(1);                   // only the role users were resolved
    }
}
