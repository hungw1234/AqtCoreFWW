using System.Reflection;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.Workflow.Application.Commands.CreateAssignmentRule;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateAssignmentRuleCommandHandler.
/// Validates: success creation, persistence, cache invalidation, specificity calculation.
/// Phase-4b B6a: WorkflowDefinitionId changed long→Guid; handler now resolves T2 PublicId→long FK.
/// </summary>
public sealed class CreateAssignmentRuleCommandHandlerTests
{
    // WorkflowAssignmentRule is T1: IRepository<T, long>
    private readonly IRepository<WorkflowAssignmentRule, long> _repository;
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly ICacheService _cache;
    private readonly CreateAssignmentRuleCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();

    // Phase-4b B6a: WorkflowDefinitionId is now Guid (T2 PublicId); DefinitionInternalId = long FK resolved by handler
    private static readonly Guid DefinitionPublicId = Guid.NewGuid();
    private const long DefinitionInternalId = 42L;

    private static readonly Guid CreatedBy = Guid.NewGuid();

    public CreateAssignmentRuleCommandHandlerTests()
    {
        _repository = Substitute.For<IRepository<WorkflowAssignmentRule, long>>();
        _definitionRepository = Substitute.For<IWorkflowDefinitionRepository>();
        _cache = Substitute.For<ICacheService>();
        _handler = new CreateAssignmentRuleCommandHandler(_repository, _definitionRepository, _cache);

        // Default: definition lookup succeeds, returns a stub with InternalId = DefinitionInternalId + PublicId = DefinitionPublicId
        _definitionRepository
            .GetByIdAsync(DefinitionPublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(CreateStubDefinition(DefinitionInternalId, DefinitionPublicId)));
    }

    // ── Success path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidCommand_CreatesRule_InvalidatesCache()
    {
        var command = new CreateAssignmentRuleCommand(
            TenantId: TenantId,
            WorkflowDefinitionId: DefinitionPublicId,
            EntityType: "Case",
            EntitySubType: null,
            Priority: 10,
            Description: "Main case flow",
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Phase-07 PR-D2: DTO now exposes PublicId (Guid AK), not internal long FK
        result.Value.WorkflowDefinitionId.Should().Be(DefinitionPublicId);
        result.Value.TenantId.Should().Be(TenantId);
        result.Value.EntityType.Should().Be("Case");
        result.Value.IsActive.Should().BeTrue();

        await _repository.Received(1).AddAsync(
            Arg.Is<WorkflowAssignmentRule>(r =>
                r.TenantId == TenantId &&
                r.WorkflowDefinitionId == DefinitionInternalId),
            Arg.Any<CancellationToken>());

        await _cache.Received(1).RemoveByPrefixAsync(
            $"wf:assignment:{TenantId}:",
            Arg.Any<CancellationToken>());
    }

    // ── Definition not found returns failure ──────────────────────────────────

    [Fact]
    public async Task Handle_DefinitionNotFound_ReturnsFailure()
    {
        var unknownId = Guid.NewGuid();
        _definitionRepository
            .GetByIdAsync(unknownId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<WorkflowDefinition>("not found"));

        var command = new CreateAssignmentRuleCommand(
            TenantId: TenantId,
            WorkflowDefinitionId: unknownId,
            EntityType: "Case",
            EntitySubType: null,
            Priority: 0,
            Description: null,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _repository.DidNotReceive().AddAsync(
            Arg.Any<WorkflowAssignmentRule>(), Arg.Any<CancellationToken>());
    }

    // ── Specificity level 3: TenantEntitySubType ──────────────────────────────

    [Fact]
    public async Task Handle_SetsCorrectSpecificity_Level3()
    {
        var command = new CreateAssignmentRuleCommand(
            TenantId: TenantId,
            WorkflowDefinitionId: DefinitionPublicId,
            EntityType: "Case",
            EntitySubType: "ConstructionPermit",
            Priority: 5,
            Description: null,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Specificity.Should().Be((int)AssignmentRuleSpecificity.TenantEntitySubType);
        result.Value.EntitySubType.Should().Be("ConstructionPermit");
    }

    // ── Specificity level 1: Tenant-only (no EntityType) ─────────────────────

    [Fact]
    public async Task Handle_SetsCorrectSpecificity_Level1()
    {
        var command = new CreateAssignmentRuleCommand(
            TenantId: TenantId,
            WorkflowDefinitionId: DefinitionPublicId,
            EntityType: null,
            EntitySubType: null,
            Priority: 0,
            Description: "Catch-all for tenant",
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Specificity.Should().Be((int)AssignmentRuleSpecificity.Tenant);
        result.Value.EntityType.Should().BeNull();
        result.Value.EntitySubType.Should().BeNull();
    }

    // ── Specificity level 2: TenantAndEntity ─────────────────────────────────

    [Fact]
    public async Task Handle_SetsCorrectSpecificity_Level2()
    {
        var command = new CreateAssignmentRuleCommand(
            TenantId: TenantId,
            WorkflowDefinitionId: DefinitionPublicId,
            EntityType: "Application",
            EntitySubType: null,
            Priority: 3,
            Description: null,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Specificity.Should().Be((int)AssignmentRuleSpecificity.TenantAndEntity);
    }

    // ── Cache invalidation uses correct prefix ────────────────────────────────

    [Fact]
    public async Task Handle_CacheInvalidation_UsesTenantScopedPrefix()
    {
        var command = new CreateAssignmentRuleCommand(
            TenantId: TenantId,
            WorkflowDefinitionId: DefinitionPublicId,
            EntityType: "Case",
            EntitySubType: null,
            Priority: 1,
            Description: null,
            CreatedBy: CreatedBy);

        await _handler.Handle(command, CancellationToken.None);

        await _cache.Received(1).RemoveByPrefixAsync(
            Arg.Is<string>(k => k == $"wf:assignment:{TenantId}:"),
            Arg.Any<CancellationToken>());
    }

    // ── EffectiveFrom/Until forwarded correctly ───────────────────────────────

    [Fact]
    public async Task Handle_WithEffectiveDates_MappedToDto()
    {
        var from = DateTimeOffset.UtcNow.AddDays(1);
        var until = DateTimeOffset.UtcNow.AddDays(30);

        var command = new CreateAssignmentRuleCommand(
            TenantId: TenantId,
            WorkflowDefinitionId: DefinitionPublicId,
            EntityType: "Case",
            EntitySubType: null,
            Priority: 0,
            Description: null,
            CreatedBy: CreatedBy,
            EffectiveFrom: from,
            EffectiveUntil: until);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.EffectiveFrom.Should().Be(from);
        result.Value.EffectiveUntil.Should().Be(until);
    }

    // ── Repository exception propagates ──────────────────────────────────────

    [Fact]
    public async Task Handle_RepositoryException_Propagates()
    {
        _repository
            .AddAsync(Arg.Any<WorkflowAssignmentRule>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB unavailable")));

        var command = new CreateAssignmentRuleCommand(
            TenantId: TenantId,
            WorkflowDefinitionId: DefinitionPublicId,
            EntityType: "Case",
            EntitySubType: null,
            Priority: 0,
            Description: null,
            CreatedBy: CreatedBy);

        var act = async () => await _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB unavailable*");
    }

    // ── Helper: create a WorkflowDefinition stub with specific internal Id and PublicId ──

    private static WorkflowDefinition CreateStubDefinition(long internalId, Guid publicId)
    {
        // WorkflowDefinition has a private EF-Core constructor; use reflection to instantiate
        var def = (WorkflowDefinition)Activator.CreateInstance(
            typeof(WorkflowDefinition), nonPublic: true)!;

        // Set Id (on Entity base) via reflection
        typeof(Entity)
            .GetProperty(nameof(Entity.Id), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(def, internalId);

        // Phase-07 PR-D2: set PublicId so MapToDto can project Guid AK into DTO
        typeof(WorkflowDefinition)
            .GetProperty(nameof(WorkflowDefinition.PublicId), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(def, publicId);

        return def;
    }
}
