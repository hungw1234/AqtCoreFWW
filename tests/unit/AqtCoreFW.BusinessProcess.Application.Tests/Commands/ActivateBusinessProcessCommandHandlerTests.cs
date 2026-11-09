using AqtCoreFW.BusinessProcess.Application.Commands.ActivateBusinessProcess;
using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.BusinessProcess.Domain.Events;
using AqtCoreFW.BusinessProcess.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Application.Tests.Commands;

/// <summary>
/// Unit tests for ActivateBusinessProcessCommandHandler. The aggregate emits BusinessProcessActivatedEvent
/// on a real false→true transition — we assert the event is in DomainEvents BEFORE repo.UpdateAsync so the
/// OutboxInterceptor (in the integration layer) can collect it in the same transaction.
/// </summary>
public sealed class ActivateBusinessProcessCommandHandlerTests
{
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private readonly IBusinessProcessRepository _repo = Substitute.For<IBusinessProcessRepository>();
    private readonly IFormTemplateQueryClient _ftClient = Substitute.For<IFormTemplateQueryClient>();
    private readonly IGuidGenerator _gen = Substitute.For<IGuidGenerator>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly IWorkflowModuleClient _workflowClient = Substitute.For<IWorkflowModuleClient>();
    private readonly ILogger<ActivateBusinessProcessCommandHandler> _logger =
        Substitute.For<ILogger<ActivateBusinessProcessCommandHandler>>();
    private readonly ActivateBusinessProcessCommandHandler _handler;

    public ActivateBusinessProcessCommandHandlerTests()
    {
        _tenantContext.TenantId.Returns(CurrentTenantId);
        _gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        // Phase 5c: default graph validation passes so existing tests are unaffected.
        _workflowClient.ValidateDefinitionGraphAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(WorkflowGraphValidationOutcome.Ok());
        _handler = new ActivateBusinessProcessCommandHandler(
            _repo, _ftClient, _gen, _tenantContext, _cache, _workflowClient, _logger);
    }

    private BusinessProcessDefinition InactiveBp() =>
        BusinessProcessDefinition.Create(_gen, CurrentTenantId, "BP", "Test", 42L, ActorId);

    // Intake-pinned BP (template 99L, v1) for the snapshot fail-fast guard.
    private BusinessProcessDefinition InactiveBpWithIntake() =>
        BusinessProcessDefinition.Create(_gen, CurrentTenantId, "BP", "Test", 42L, ActorId,
            description: null, intakeFormTemplateId: 99L, intakeFormTemplateVersion: 1);

    [Fact]
    public async Task Handle_CrossTenant_Rejected_BeforeAnyDbCall()
    {
        var result = await _handler.Handle(
            new ActivateBusinessProcessCommand(OtherTenantId, Guid.NewGuid(), ActorId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await _repo.DidNotReceive().GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFoundError()
    {
        _repo.GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((BusinessProcessDefinition?)null);

        var result = await _handler.Handle(
            new ActivateBusinessProcessCommand(CurrentTenantId, Guid.NewGuid(), ActorId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InactiveToActive_EmitsActivatedEvent()
    {
        var bp = InactiveBp();
        _repo.GetByPublicIdAsync(bp.PublicId, Arg.Any<CancellationToken>()).Returns(bp);

        var result = await _handler.Handle(
            new ActivateBusinessProcessCommand(CurrentTenantId, bp.PublicId, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bp.IsActive.Should().BeTrue();
        bp.DomainEvents.Should().Contain(e => e is BusinessProcessActivatedEvent,
            "real false->true transition must produce the outbox event in the same SaveChanges");
        await _repo.Received(1).UpdateAsync(bp, Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveByPrefixAsync(
            $"bp:{CurrentTenantId}:", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyActive_NoOpStillInvalidatesCache()
    {
        var bp = InactiveBp();
        bp.Activate(_gen, ActorId); // pre-active
        bp.ClearDomainEvents();
        _repo.GetByPublicIdAsync(bp.PublicId, Arg.Any<CancellationToken>()).Returns(bp);

        var result = await _handler.Handle(
            new ActivateBusinessProcessCommand(CurrentTenantId, bp.PublicId, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bp.IsActive.Should().BeTrue();
        bp.DomainEvents.OfType<BusinessProcessActivatedEvent>().Should().BeEmpty(
            "idempotent Activate must not enqueue a duplicate outbox event");
        // UpdateAsync still called — cheap; cache invalidation safe even on no-op.
        await _repo.Received(1).UpdateAsync(bp, Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveByPrefixAsync(
            $"bp:{CurrentTenantId}:", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_IntakePinned_NoPublishedSnapshot_RejectsBeforeActivate()
    {
        var bp = InactiveBpWithIntake();
        _repo.GetByPublicIdAsync(bp.PublicId, Arg.Any<CancellationToken>()).Returns(bp);
        // Snapshot missing for the pinned (99L, v1) — fail-fast must block activation
        _ftClient.GetVersionSchemaByInternalIdAsync(99L, 1, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((FormTemplateVersionSchemaDto?)null);

        var result = await _handler.Handle(
            new ActivateBusinessProcessCommand(CurrentTenantId, bp.PublicId, ActorId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        result.Errors.First().Message.Should().Contain("chưa được publish");
        bp.IsActive.Should().BeFalse("activation must not proceed when the pinned intake has no snapshot");
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());
    }
}
