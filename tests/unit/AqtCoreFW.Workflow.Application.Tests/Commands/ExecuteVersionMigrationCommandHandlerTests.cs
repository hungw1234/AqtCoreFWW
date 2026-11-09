using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.ApproveVersionMigration;
using AqtCoreFW.Workflow.Application.Commands.ExecuteVersionMigration;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for ExecuteVersionMigrationCommandHandler.
/// Validates: Approved → InProgress + Hangfire enqueued,
/// wrong status rejected, hash tamper caught between Approve and Execute.
/// </summary>
public sealed class ExecuteVersionMigrationCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();
    private static readonly Guid ApproverId = Guid.NewGuid();
    private const string FakeJobId = "hangfire-job-123";

    private readonly IWorkflowVersionMigrationRepository _repo;
    private readonly IWorkflowJobEnqueuer _jobEnqueuer;
    private readonly ExecuteVersionMigrationCommandHandler _handler;

    public ExecuteVersionMigrationCommandHandlerTests()
    {
        _repo = Substitute.For<IWorkflowVersionMigrationRepository>();
        _jobEnqueuer = Substitute.For<IWorkflowJobEnqueuer>();
        _handler = new ExecuteVersionMigrationCommandHandler(_repo, _jobEnqueuer);
    }

    private static WorkflowVersionMigration MakeApprovedMigration()
    {
        // T1 entity: sourceDefinitionId/targetDefinitionId are long (FK to T2 internal PK)
        const string mappings = "[{\"oldStateId\":1,\"newStateId\":2}]";
        var migration = WorkflowVersionMigration.Create(
            1L, 2L, mappings, TenantId, CreatorId);
        migration.MarkApproved(ApproverId);
        return migration;
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_ApprovedMigration_TransitionsToInProgress()
    {
        var migration = MakeApprovedMigration();
        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));
        _jobEnqueuer.EnqueueMigrationExecution(migration.Id).Returns(FakeJobId);

        var cmd = new ExecuteVersionMigrationCommand(migration.Id, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(MigrationStatus.InProgress);
    }

    [Fact]
    public async Task Handle_ApprovedMigration_HangfireJobEnqueued()
    {
        var migration = MakeApprovedMigration();
        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));
        _jobEnqueuer.EnqueueMigrationExecution(migration.Id).Returns(FakeJobId);

        var cmd = new ExecuteVersionMigrationCommand(migration.Id, TenantId);
        await _handler.Handle(cmd, CancellationToken.None);

        _jobEnqueuer.Received(1).EnqueueMigrationExecution(migration.Id);
    }

    [Fact]
    public async Task Handle_ApprovedMigration_HangfireJobIdStoredOnDto()
    {
        var migration = MakeApprovedMigration();
        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));
        _jobEnqueuer.EnqueueMigrationExecution(migration.Id).Returns(FakeJobId);

        var cmd = new ExecuteVersionMigrationCommand(migration.Id, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.Value.HangfireJobId.Should().Be(FakeJobId);
    }

    [Fact]
    public async Task Handle_ApprovedMigration_PersistsUpdate()
    {
        var migration = MakeApprovedMigration();
        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));
        _jobEnqueuer.EnqueueMigrationExecution(migration.Id).Returns(FakeJobId);

        var cmd = new ExecuteVersionMigrationCommand(migration.Id, TenantId);
        await _handler.Handle(cmd, CancellationToken.None);

        await _repo.Received(1).UpdateAsync(
            Arg.Is<WorkflowVersionMigration>(m => m.Status == MigrationStatus.InProgress),
            Arg.Any<CancellationToken>());
    }

    // --- Wrong status ---

    [Fact]
    public async Task Handle_StatusPendingApproval_ReturnsValidationError()
    {
        const string mappings = "[{\"oldStateId\":1,\"newStateId\":2}]";
        var migration = WorkflowVersionMigration.Create(
            1L, 2L, mappings, TenantId, CreatorId);
        // Status = PendingApproval (not Approved)

        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));

        var cmd = new ExecuteVersionMigrationCommand(migration.Id, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is ValidationError);
        _jobEnqueuer.DidNotReceive().EnqueueMigrationExecution(Arg.Any<long>());
    }

    // --- Hash tamper between Approve and Execute ---

    [Fact]
    public async Task Handle_TamperedStateMappingsJsonAfterApproval_ReturnsValidationError()
    {
        // Simulates an attacker modifying StateMappingsJson in the DB between Approve and Execute.
        // After approval, tamper the JSON via reflection → VerifyMappingHash throws → handler wraps to Result.Fail.
        var migration = MakeApprovedMigration();

        // Tamper the StateMappingsJson after approval via reflection on private setter
        var prop = typeof(WorkflowVersionMigration)
            .GetProperty("StateMappingsJson",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)!;
        prop.SetValue(migration, "[{\"oldStateId\":\"tampered\",\"newStateId\":\"evil\"}]");

        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));

        var cmd = new ExecuteVersionMigrationCommand(migration.Id, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Handler must detect tampering and return failure before enqueueing
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is ValidationError);
        result.Errors.Should().Contain(e =>
            e.Message.Contains("tampered", StringComparison.OrdinalIgnoreCase) ||
            e.Message.Contains("hash", StringComparison.OrdinalIgnoreCase));
        _jobEnqueuer.DidNotReceive().EnqueueMigrationExecution(Arg.Any<long>());
    }

    [Fact]
    public async Task Handle_HashIntact_DoesNotReturnHashError()
    {
        // Un-tampered migration — VerifyMappingHash passes silently, no hash errors in result
        var migration = MakeApprovedMigration();
        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));
        _jobEnqueuer.EnqueueMigrationExecution(migration.Id).Returns(FakeJobId);

        var cmd = new ExecuteVersionMigrationCommand(migration.Id, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Errors.Should().NotContain(e =>
            e.Message.Contains("tampered", StringComparison.OrdinalIgnoreCase));
    }
}
