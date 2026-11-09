using System.Reflection;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.Workflow.Application.Commands.CreateEscalationRule;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateEscalationRuleCommandHandler.
/// Phase-07 PR-D2: WorkflowDefinitionId changed long→Guid (T2 PublicId AK);
/// handler now resolves PublicId → internal long FK via IWorkflowDefinitionRepository.GetByIdAsync.
/// Validates: success creation, NotFound on unknown definition, persistence, DTO mapping, optional state scope.
/// </summary>
public sealed class CreateEscalationRuleCommandHandlerTests
{
    // EscalationRule is T1: IRepository<T, long>
    private readonly IRepository<EscalationRule, long> _repository;
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly CreateEscalationRuleCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();

    // Phase-07 PR-D2: WorkflowDefinitionId is now Guid (T2 PublicId AK)
    private static readonly Guid WorkflowDefinitionPublicId = Guid.NewGuid();
    private const long WorkflowDefinitionInternalId = 1L;
    private const long StateId = 2L;
    private static readonly Guid CreatedBy = Guid.NewGuid();

    public CreateEscalationRuleCommandHandlerTests()
    {
        _repository = Substitute.For<IRepository<EscalationRule, long>>();
        _definitionRepository = Substitute.For<IWorkflowDefinitionRepository>();
        _handler = new CreateEscalationRuleCommandHandler(_repository, _definitionRepository);

        // Default: definition lookup succeeds, returns a stub with InternalId = WorkflowDefinitionInternalId
        _definitionRepository
            .GetByIdAsync(WorkflowDefinitionPublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(CreateStubDefinition(WorkflowDefinitionInternalId, WorkflowDefinitionPublicId)));
    }

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithDto()
    {
        var command = new CreateEscalationRuleCommand(
            WorkflowDefinitionId: WorkflowDefinitionPublicId,
            StateId: StateId,
            EscalationAfterMinutes: 60,
            EscalateTo: EscalationTarget.Manager,
            EscalateToValue: "manager@example.com",
            NotifyVia: "Email",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.EscalationAfterMinutes.Should().Be(60);
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsViaRepository()
    {
        var command = new CreateEscalationRuleCommand(
            WorkflowDefinitionId: WorkflowDefinitionPublicId,
            StateId: null,
            EscalationAfterMinutes: 120,
            EscalateTo: EscalationTarget.Role,
            EscalateToValue: "escalation-team",
            NotifyVia: "Teams",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<EscalationRule>(r => r.EscalationAfterMinutes == 120),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MapsAllProperties()
    {
        var command = new CreateEscalationRuleCommand(
            WorkflowDefinitionId: WorkflowDefinitionPublicId,
            StateId: StateId,
            EscalationAfterMinutes: 30,
            EscalateTo: EscalationTarget.Manager,
            EscalateToValue: "mgr-email",
            NotifyVia: "Slack",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        // Phase-07 PR-D2: WorkflowDefinitionId in DTO is now Guid (PublicId AK)
        result.Value.WorkflowDefinitionId.Should().Be(WorkflowDefinitionPublicId);
        result.Value.StateId.Should().Be(StateId);
        result.Value.EscalateTo.Should().Be(EscalationTarget.Manager);
        result.Value.EscalateToValue.Should().Be("mgr-email");
        result.Value.NotifyVia.Should().Be("Slack");
    }

    [Fact]
    public async Task Handle_RuleCreatedAsActive()
    {
        var command = new CreateEscalationRuleCommand(
            WorkflowDefinitionId: WorkflowDefinitionPublicId,
            StateId: StateId,
            EscalationAfterMinutes: 60,
            EscalateTo: EscalationTarget.Manager,
            EscalateToValue: "value",
            NotifyVia: "Email",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithoutStateScope_AppliesWorkflowWide()
    {
        var command = new CreateEscalationRuleCommand(
            WorkflowDefinitionId: WorkflowDefinitionPublicId,
            StateId: null,
            EscalationAfterMinutes: 90,
            EscalateTo: EscalationTarget.Role,
            EscalateToValue: "escalation-group",
            NotifyVia: "Email",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StateId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MultipleTargetTypes()
    {
        var targets = new[]
        {
            EscalationTarget.Manager,
            EscalationTarget.Role,
            EscalationTarget.SpecificUser
        };

        foreach (var target in targets)
        {
            var command = new CreateEscalationRuleCommand(
                WorkflowDefinitionId: WorkflowDefinitionPublicId,
                StateId: StateId,
                EscalationAfterMinutes: 60,
                EscalateTo: target,
                EscalateToValue: $"value-{target}",
                NotifyVia: "Email",
                TenantId: TenantId,
                CreatedBy: CreatedBy);

            var result = await _handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.EscalateTo.Should().Be(target);
        }
    }

    // --- Edge cases ---

    [Fact]
    public async Task Handle_MinimumEscalationTime()
    {
        var command = new CreateEscalationRuleCommand(
            WorkflowDefinitionId: WorkflowDefinitionPublicId,
            StateId: StateId,
            EscalationAfterMinutes: 1,
            EscalateTo: EscalationTarget.Manager,
            EscalateToValue: "value",
            NotifyVia: "Email",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.EscalationAfterMinutes.Should().Be(1);
    }

    [Fact]
    public async Task Handle_LargeEscalationTime()
    {
        var command = new CreateEscalationRuleCommand(
            WorkflowDefinitionId: WorkflowDefinitionPublicId,
            StateId: StateId,
            EscalationAfterMinutes: 10080, // 7 days
            EscalateTo: EscalationTarget.Manager,
            EscalateToValue: "value",
            NotifyVia: "Email",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.EscalationAfterMinutes.Should().Be(10080);
    }

    [Fact]
    public async Task Handle_RepositoryException_Propagates()
    {
        _repository
            .AddAsync(Arg.Any<EscalationRule>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB unavailable")));

        var command = new CreateEscalationRuleCommand(
            WorkflowDefinitionId: WorkflowDefinitionPublicId,
            StateId: StateId,
            EscalationAfterMinutes: 60,
            EscalateTo: EscalationTarget.Manager,
            EscalateToValue: "value",
            NotifyVia: "Email",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var act = async () => await _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB unavailable*");
    }

    // --- NotFound path (Phase-07 PR-D2 new test) ---

    [Fact]
    public async Task Handle_DefinitionNotFound_ReturnsFailure()
    {
        var unknownId = Guid.NewGuid();
        _definitionRepository
            .GetByIdAsync(unknownId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<WorkflowDefinition>("not found"));

        var command = new CreateEscalationRuleCommand(
            WorkflowDefinitionId: unknownId,
            StateId: null,
            EscalationAfterMinutes: 60,
            EscalateTo: EscalationTarget.Manager,
            EscalateToValue: "value",
            NotifyVia: "Email",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _repository.DidNotReceive().AddAsync(
            Arg.Any<EscalationRule>(), Arg.Any<CancellationToken>());
    }

    // --- Helper: create a WorkflowDefinition stub with specific Id and PublicId ---

    private static WorkflowDefinition CreateStubDefinition(long internalId, Guid publicId)
    {
        // WorkflowDefinition has a private EF-Core constructor; use reflection to instantiate
        var def = (WorkflowDefinition)Activator.CreateInstance(
            typeof(WorkflowDefinition), nonPublic: true)!;

        // Set Id (on AuditableEntity base → Entity base) via reflection
        typeof(Entity)
            .GetProperty(nameof(Entity.Id), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(def, internalId);

        // Set PublicId via reflection
        typeof(WorkflowDefinition)
            .GetProperty(nameof(WorkflowDefinition.PublicId), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(def, publicId);

        return def;
    }
}
