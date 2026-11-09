using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateVersionMigration;
using AqtCoreFW.Workflow.Application.Commands.ValidateMigrationMapping;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Tests.Helpers;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateVersionMigrationCommandHandler.
/// Validates: success creates entity at PendingApproval with hash,
/// lock acquire fail → ConflictError, duplicate pending → ConflictError,
/// rate limit ≥3 → ValidationError, creator captured from command.
/// Phase-4b B4c: SourceDefinitionId/TargetDefinitionId in command are Guid (T2 PublicId AK).
/// Handler internally resolves them to long internal Ids via IWorkflowDefinitionRepository.
/// GetActiveOnSourceAsync takes long (internal Id).
/// WorkflowVersionMigration.Create(long, long, ...).
/// </summary>
public sealed class CreateVersionMigrationCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();
    // Command uses Guid (T2 PublicId AK) for source/target definition Ids
    private static readonly Guid SourceDefPublicId = Guid.NewGuid();
    private static readonly Guid TargetDefPublicId = Guid.NewGuid();
    // Handler resolves these to internal long Ids (simulated as 10L, 20L)
    private const long SourceDefInternalId = 10L;
    private const long TargetDefInternalId = 20L;

    private readonly IWorkflowVersionMigrationRepository _migrationRepo;
    private readonly IWorkflowDefinitionRepository _definitionRepo;
    private readonly ISourceDefinitionLockService _lockService;
    private readonly IMediator _mediator;
    private readonly CreateVersionMigrationCommandHandler _handler;

    public CreateVersionMigrationCommandHandlerTests()
    {
        _migrationRepo = Substitute.For<IWorkflowVersionMigrationRepository>();
        _definitionRepo = Substitute.For<IWorkflowDefinitionRepository>();
        _lockService = Substitute.For<ISourceDefinitionLockService>();
        _mediator = Substitute.For<IMediator>();

        _handler = new CreateVersionMigrationCommandHandler(
            _migrationRepo, _definitionRepo, _lockService, _mediator);
    }

    private static CreateVersionMigrationCommand BuildCommand(
        Guid? sourceId = null, Guid? targetId = null, Guid? createdBy = null) =>
        new(
            sourceId ?? SourceDefPublicId,
            targetId ?? TargetDefPublicId,
            [new StateMappingEntry(1L, 2L)],
            TenantId,
            createdBy ?? CreatorId);

    /// <summary>
    /// Setup definitions so handler can resolve PublicId → internal long Id.
    /// Returns source/target definitions with stub internal Ids set via reflection.
    /// </summary>
    private void SetupDefinitionResolution()
    {
        var gen = TestGuidGenerator.Create();
        var sourceDef = WorkflowDefinition.Create(gen, "Source WF", "desc", TenantId, CreatorId, "wf-key");
        var targetDef = WorkflowDefinition.Create(gen, "Target WF", "desc", TenantId, CreatorId, "wf-key");

        // Set internal Id via reflection (DB-assigned = 0, override for test)
        typeof(WorkflowDefinition).BaseType!.GetProperty("Id")!
            .SetValue(sourceDef, SourceDefInternalId);
        typeof(WorkflowDefinition).BaseType!.GetProperty("Id")!
            .SetValue(targetDef, TargetDefInternalId);

        _definitionRepo.GetWithStatesAndTransitionsAsync(SourceDefPublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(sourceDef));
        _definitionRepo.GetWithStatesAndTransitionsAsync(TargetDefPublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(targetDef));
    }

    private void SetupValidFlow()
    {
        SetupDefinitionResolution();
        _lockService.AcquireAsync(Arg.Any<Guid>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _migrationRepo.GetActiveOnSourceAsync(
                Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowVersionMigration?)null);
        _migrationRepo.CountActiveByTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(0);
        _mediator.Send(Arg.Any<ValidateMigrationMappingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new MigrationValidationResult { IsValid = true }));
    }

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithPendingApprovalStatus()
    {
        SetupValidFlow();
        var cmd = BuildCommand();

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(MigrationStatus.PendingApproval);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatedByMatchesCommand()
    {
        SetupValidFlow();
        var cmd = BuildCommand(createdBy: CreatorId);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CreatedBy.Should().Be(CreatorId);
    }

    [Fact]
    public async Task Handle_ValidCommand_MappingHashIsSet()
    {
        SetupValidFlow();
        var cmd = BuildCommand();

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.MappingHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsMigrationViaAddAsync()
    {
        SetupValidFlow();
        var cmd = BuildCommand();

        await _handler.Handle(cmd, CancellationToken.None);

        await _migrationRepo.Received(1).AddAsync(
            Arg.Is<WorkflowVersionMigration>(m =>
                m.Status == MigrationStatus.PendingApproval &&
                m.TenantId == TenantId),
            Arg.Any<CancellationToken>());
    }

    // --- Lock failure ---

    [Fact]
    public async Task Handle_LockAcquireFail_ReturnsConflictError()
    {
        _lockService.AcquireAsync(Arg.Any<Guid>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(false); // lock not acquired

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is ConflictError);
    }

    [Fact]
    public async Task Handle_LockAcquireFail_DoesNotPersist()
    {
        _lockService.AcquireAsync(Arg.Any<Guid>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await _handler.Handle(BuildCommand(), CancellationToken.None);

        await _migrationRepo.DidNotReceive().AddAsync(
            Arg.Any<WorkflowVersionMigration>(), Arg.Any<CancellationToken>());
    }

    // --- Duplicate pending ---

    [Fact]
    public async Task Handle_DuplicatePendingOnSource_ReturnsConflictError()
    {
        SetupDefinitionResolution();
        _lockService.AcquireAsync(Arg.Any<Guid>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // WorkflowVersionMigration.Create(long, long, ...) — sourceId/targetId are long internal Ids
        var existing = WorkflowVersionMigration.Create(
            SourceDefInternalId, TargetDefInternalId, "[{\"oldStateId\":1,\"newStateId\":2}]",
            TenantId, Guid.NewGuid());
        _migrationRepo.GetActiveOnSourceAsync(
                Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is ConflictError);
    }

    // --- Rate limit ---

    [Fact]
    public async Task Handle_TenantAtRateLimit_ReturnsValidationError()
    {
        SetupDefinitionResolution();
        _lockService.AcquireAsync(Arg.Any<Guid>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _migrationRepo.GetActiveOnSourceAsync(
                Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowVersionMigration?)null);
        _migrationRepo.CountActiveByTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(3); // at limit

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is ValidationError);
    }

    // --- Validation failure propagated ---

    [Fact]
    public async Task Handle_ValidationFails_ReturnsValidationError()
    {
        SetupDefinitionResolution();
        _lockService.AcquireAsync(Arg.Any<Guid>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _migrationRepo.GetActiveOnSourceAsync(
                Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowVersionMigration?)null);
        _migrationRepo.CountActiveByTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(0);
        _mediator.Send(Arg.Any<ValidateMigrationMappingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new MigrationValidationResult
            {
                IsValid = false,
                Errors = ["Target definition is not active."]
            }));

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e is ValidationError);
    }
}
