using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowTask;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// TDD tests for Step 12: auto-delegate on task creation when assignee has active rule.
/// Phase-07 PR-2: command now takes WorkflowInstancePublicId (Guid T2 AK);
/// handler resolves via IWorkflowInstanceRepository.GetByIdAsync once then uses task.Id (long T1 PK).
/// </summary>
public sealed class CreateWorkflowTaskAutoDelegateTests
{
    private readonly IWorkflowTaskRepository _repository;
    private readonly IWorkflowInstanceRepository _instanceRepository;
    private readonly ITaskAssignmentRepository _assignmentRepository;
    private readonly IDelegationRuleRepository _delegationRuleRepository;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstancePublicId = Guid.NewGuid();
    private static readonly Guid AssigneeId = Guid.NewGuid();
    private static readonly Guid DelegateUserId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();

    /// <summary>
    /// WorkflowInstance created via factory — Id == 0L pre-SaveChanges (DB-assigned IDENTITY).
    /// </summary>
    private static readonly WorkflowInstance FakeInstance = WorkflowInstance.Create(
        TestGuidGenerator.Create(),
        definitionId: 1L,
        initialStateId: 1L,
        entityType: "Case",
        entityId: Guid.NewGuid(),
        tenantId: TenantId,
        createdBy: Guid.NewGuid());

    public CreateWorkflowTaskAutoDelegateTests()
    {
        _repository = Substitute.For<IWorkflowTaskRepository>();
        _instanceRepository = Substitute.For<IWorkflowInstanceRepository>();
        _assignmentRepository = Substitute.For<ITaskAssignmentRepository>();
        _delegationRuleRepository = Substitute.For<IDelegationRuleRepository>();

        // Default: resolve InstancePublicId → FakeInstance
        _instanceRepository
            .GetByIdAsync(InstancePublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(FakeInstance));
    }

    private CreateWorkflowTaskCommandHandler BuildHandler() =>
        new(_repository, _instanceRepository, _assignmentRepository, _delegationRuleRepository);

    private static CreateWorkflowTaskCommand BuildCommand(Guid? assigneeId = null) =>
        new(
            WorkflowInstancePublicId: InstancePublicId,
            Title: "Task",
            Description: null,
            AssigneeId: assigneeId ?? AssigneeId,
            Priority: WorkflowTaskPriority.Normal,
            DueDate: null,
            TenantId: TenantId,
            CreatedBy: CreatedBy);

    [Fact]
    public async Task Create_AssigneeHasActiveRule_AutoDelegatesTask()
    {
        // Arrange: active rule delegates AssigneeId → DelegateUserId
        var rule = DelegationRule.Create(
            AssigneeId, DelegateUserId,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7),
            TenantId, CreatedBy);

        _delegationRuleRepository.GetActiveRuleAsync(AssigneeId, TenantId, Arg.Any<CancellationToken>())
            .Returns(rule);

        var handler = BuildHandler();
        var result = await handler.Handle(BuildCommand(), CancellationToken.None);

        // Assert: task is auto-delegated
        result.IsSuccess.Should().BeTrue();
        result.Value.DelegatedFromUserId.Should().Be(AssigneeId);
        result.Value.AssigneeId.Should().Be(DelegateUserId);
    }

    [Fact]
    public async Task Create_AssigneeHasActiveRule_PersistsAssignment()
    {
        var rule = DelegationRule.Create(
            AssigneeId, DelegateUserId,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7),
            TenantId, CreatedBy);

        _delegationRuleRepository.GetActiveRuleAsync(AssigneeId, TenantId, Arg.Any<CancellationToken>())
            .Returns(rule);

        var handler = BuildHandler();
        await handler.Handle(BuildCommand(), CancellationToken.None);

        await _assignmentRepository.Received(1).AddAsync(
            Arg.Is<TaskAssignment>(a =>
                a.AssignedToId == DelegateUserId &&
                a.Type == TaskAssignmentType.Delegate),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_NoActiveRule_DoesNotDelegate()
    {
        _delegationRuleRepository.GetActiveRuleAsync(AssigneeId, TenantId, Arg.Any<CancellationToken>())
            .Returns((DelegationRule?)null);

        var handler = BuildHandler();
        var result = await handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DelegatedFromUserId.Should().BeNull();
        result.Value.AssigneeId.Should().Be(AssigneeId);
        await _assignmentRepository.DidNotReceive().AddAsync(Arg.Any<TaskAssignment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_NoAssigneeId_SkipsAutoDelegateCheck()
    {
        var handler = BuildHandler();
        var command = BuildCommand(assigneeId: null) with { AssigneeId = null };
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // GetActiveRuleAsync should NOT be called when no assignee
        await _delegationRuleRepository.DidNotReceive().GetActiveRuleAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
