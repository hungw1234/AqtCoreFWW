using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.ApproveVersionMigration;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for ApproveVersionMigrationCommandHandler.
/// Validates: 4-eyes, status guards, tamper detection, successful approval.
/// Phase-4b B4c: WorkflowVersionMigration is T1 — Create(long, long, ...), Id is long (0 in unit tests).
/// IWorkflowVersionMigrationRepository.GetByIdAsync takes long.
/// </summary>
public sealed class ApproveVersionMigrationCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();
    private static readonly Guid ApproverId = Guid.NewGuid(); // different from CreatorId

    private readonly IWorkflowVersionMigrationRepository _repo;
    private readonly ApproveVersionMigrationCommandHandler _handler;

    public ApproveVersionMigrationCommandHandlerTests()
    {
        _repo = Substitute.For<IWorkflowVersionMigrationRepository>();
        _handler = new ApproveVersionMigrationCommandHandler(_repo);
    }

    // WorkflowVersionMigration.Create(long sourceDefId, long targetDefId, string json, Guid tenantId, Guid createdBy)
    // Id is long (DB-assigned — 0 in unit tests without DB).
    private static WorkflowVersionMigration MakePendingMigration(Guid? createdBy = null)
    {
        var mappings = "[{\"oldStateId\":1,\"newStateId\":2}]";
        return WorkflowVersionMigration.Create(
            1L, 2L, mappings, TenantId, createdBy ?? CreatorId);
    }

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidApproval_ReturnsApprovedStatus()
    {
        var migration = MakePendingMigration(CreatorId);
        // migration.Id = 0 (long, DB-assigned). GetByIdAsync takes long.
        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));

        var cmd = new ApproveVersionMigrationCommand(migration.Id, ApproverId, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(MigrationStatus.Approved);
        result.Value.ApprovedBy.Should().Be(ApproverId);
    }

    [Fact]
    public async Task Handle_ValidApproval_PersistsUpdate()
    {
        var migration = MakePendingMigration(CreatorId);
        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));

        var cmd = new ApproveVersionMigrationCommand(migration.Id, ApproverId, TenantId);
        await _handler.Handle(cmd, CancellationToken.None);

        await _repo.Received(1).UpdateAsync(
            Arg.Is<WorkflowVersionMigration>(m => m.Status == MigrationStatus.Approved),
            Arg.Any<CancellationToken>());
    }

    // --- 4-eyes violation ---

    [Fact]
    public async Task Handle_SameUserAsCreator_ReturnsForbiddenError()
    {
        var migration = MakePendingMigration(CreatorId);
        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));

        // ApproverId == CreatorId → self-approval violation
        var cmd = new ApproveVersionMigrationCommand(migration.Id, CreatorId, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is ForbiddenError);
        result.Errors.Should().Contain(e =>
            e.Message.Contains("self_approval", StringComparison.OrdinalIgnoreCase));
    }

    // --- Status guards ---

    [Fact]
    public async Task Handle_StatusNotPendingApproval_ReturnsValidationError()
    {
        var migration = MakePendingMigration(CreatorId);
        migration.MarkApproved(ApproverId); // advance to Approved
        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));

        var differentApprover = Guid.NewGuid();
        var cmd = new ApproveVersionMigrationCommand(migration.Id, differentApprover, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is ValidationError);
    }

    [Fact]
    public async Task Handle_MigrationNotFound_ReturnsFailed()
    {
        _repo.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail("Not found"));

        var cmd = new ApproveVersionMigrationCommand(0L, ApproverId, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Tamper detection ---

    [Fact]
    public async Task Handle_TamperedStateMappingsJson_ReturnsValidationError()
    {
        // Create migration with one JSON; then tamper the stored JSON via reflection
        // so the computed hash differs from MappingHash → VerifyMappingHash throws.
        var migration = MakePendingMigration(CreatorId);

        // Use reflection to overwrite the private StateMappingsJson backing field
        // (the property has a private setter — we target the backing field directly).
        var prop = typeof(WorkflowVersionMigration)
            .GetProperty("StateMappingsJson",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)!;
        // Use SetValue via the property's setter (private) through reflection
        prop.SetValue(migration, "[{\"oldStateId\":\"tampered\",\"newStateId\":\"evil\"}]");

        _repo.GetByIdAsync(migration.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(migration));

        var cmd = new ApproveVersionMigrationCommand(migration.Id, ApproverId, TenantId);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        // Handler must catch InvalidOperationException from VerifyMappingHash and return failure
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is ValidationError);
        result.Errors.Should().Contain(e =>
            e.Message.Contains("tampered", StringComparison.OrdinalIgnoreCase) ||
            e.Message.Contains("hash", StringComparison.OrdinalIgnoreCase));
    }
}
