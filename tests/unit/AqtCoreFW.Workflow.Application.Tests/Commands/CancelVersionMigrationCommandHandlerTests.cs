using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CancelVersionMigration;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for CancelVersionMigrationCommandHandler.
/// Validates: PendingApproval → Cancelled, Approved → Cancelled,
/// InProgress → CancelRequested=true, Completed → rejected.
/// </summary>
public sealed class CancelVersionMigrationCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();
    private static readonly Guid ApproverId = Guid.NewGuid();

    private readonly IWorkflowVersionMigrationRepository _repo;
    private readonly CancelVersionMigrationCommandHandler _handler;

    public CancelVersionMigrationCommandHandlerTests()
    {
        _repo = Substitute.For<IWorkflowVersionMigrationRepository>();
        _handler = new CancelVersionMigrationCommandHandler(_repo);
    }

    // WorkflowVersionMigration is T1: Create(long sourceDefinitionId, long targetDefinitionId, ...)
    private static WorkflowVersionMigration MakeMigration()
    {
        var mappings = "[{\"oldStateId\":1,\"newStateId\":2}]";
        return WorkflowVersionMigration.Create(
            1L, 2L, mappings, TenantId, CreatorId);
    }

    // --- PendingApproval → Cancelled ---

    [Fact]
    public async Task Handle_PendingApproval_ImmediatelyCancelled()
    {
        var migration = MakeMigration(); // Status = PendingApproval
        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));

        var cmd = new CancelVersionMigrationCommand(migration.Id, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(MigrationStatus.Cancelled);
    }

    // --- Approved → Cancelled ---

    [Fact]
    public async Task Handle_Approved_ImmediatelyCancelled()
    {
        var migration = MakeMigration();
        migration.MarkApproved(ApproverId); // Status = Approved
        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));

        var cmd = new CancelVersionMigrationCommand(migration.Id, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(MigrationStatus.Cancelled);
    }

    // --- InProgress → CancelRequested = true ---

    [Fact]
    public async Task Handle_InProgress_SetsCancelRequested()
    {
        var migration = MakeMigration();
        migration.MarkApproved(ApproverId);
        migration.MarkStarted("hangfire-job-1"); // Status = InProgress

        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));

        var cmd = new CancelVersionMigrationCommand(migration.Id, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Status stays InProgress — worker polls CancelRequested between batches
        migration.CancelRequested.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_InProgress_StatusRemainsInProgress()
    {
        var migration = MakeMigration();
        migration.MarkApproved(ApproverId);
        migration.MarkStarted("hangfire-job-2");

        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));

        var cmd = new CancelVersionMigrationCommand(migration.Id, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Not immediately cancelled — worker will mark Cancelled after current batch
        result.Value.Status.Should().Be(MigrationStatus.InProgress);
    }

    // --- Terminal state rejected ---

    [Fact]
    public async Task Handle_Completed_ReturnsValidationError()
    {
        var migration = MakeMigration();
        migration.MarkApproved(ApproverId);
        migration.MarkStarted("job-3");
        migration.MarkCompleted(); // Status = Completed

        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));

        var cmd = new CancelVersionMigrationCommand(migration.Id, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is ValidationError);
    }

    [Fact]
    public async Task Handle_AlreadyCancelled_ReturnsValidationError()
    {
        var migration = MakeMigration();
        migration.MarkCancelled(); // PendingApproval → Cancelled immediately

        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));

        var cmd = new CancelVersionMigrationCommand(migration.Id, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is ValidationError);
    }

    // --- Persistence ---

    [Fact]
    public async Task Handle_PendingApproval_PersistsUpdate()
    {
        var migration = MakeMigration();
        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));

        var cmd = new CancelVersionMigrationCommand(migration.Id, TenantId);
        await _handler.Handle(cmd, CancellationToken.None);

        await _repo.Received(1).UpdateAsync(
            Arg.Is<WorkflowVersionMigration>(m => m.Status == MigrationStatus.Cancelled),
            Arg.Any<CancellationToken>());
    }
}
