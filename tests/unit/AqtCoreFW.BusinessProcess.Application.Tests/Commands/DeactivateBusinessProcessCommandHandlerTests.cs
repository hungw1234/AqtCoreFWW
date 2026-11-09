using AqtCoreFW.BusinessProcess.Application.Commands.DeactivateBusinessProcess;
using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.BusinessProcess.Domain.Events;
using AqtCoreFW.BusinessProcess.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Application.Tests.Commands;

/// <summary>
/// Unit tests for DeactivateBusinessProcessCommandHandler. Mirrors Activate tests symmetrically — same
/// Pattern A guard + NotFound + state-transition event-emission expectations.
/// </summary>
public sealed class DeactivateBusinessProcessCommandHandlerTests
{
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private readonly IBusinessProcessRepository _repo = Substitute.For<IBusinessProcessRepository>();
    private readonly IGuidGenerator _gen = Substitute.For<IGuidGenerator>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly ILogger<DeactivateBusinessProcessCommandHandler> _logger =
        Substitute.For<ILogger<DeactivateBusinessProcessCommandHandler>>();
    private readonly DeactivateBusinessProcessCommandHandler _handler;

    public DeactivateBusinessProcessCommandHandlerTests()
    {
        _tenantContext.TenantId.Returns(CurrentTenantId);
        _gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _handler = new DeactivateBusinessProcessCommandHandler(_repo, _gen, _tenantContext, _cache, _logger);
    }

    private BusinessProcessDefinition ActiveBp()
    {
        var bp = BusinessProcessDefinition.Create(_gen, CurrentTenantId, "BP", "Test", 42L, ActorId);
        bp.Activate(_gen, ActorId);
        bp.ClearDomainEvents();
        return bp;
    }

    [Fact]
    public async Task Handle_CrossTenant_Rejected_BeforeAnyDbCall()
    {
        var result = await _handler.Handle(
            new DeactivateBusinessProcessCommand(OtherTenantId, Guid.NewGuid(), ActorId),
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
            new DeactivateBusinessProcessCommand(CurrentTenantId, Guid.NewGuid(), ActorId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ActiveToInactive_EmitsDeactivatedEvent()
    {
        var bp = ActiveBp();
        _repo.GetByPublicIdAsync(bp.PublicId, Arg.Any<CancellationToken>()).Returns(bp);

        var result = await _handler.Handle(
            new DeactivateBusinessProcessCommand(CurrentTenantId, bp.PublicId, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bp.IsActive.Should().BeFalse();
        bp.DomainEvents.Should().Contain(e => e is BusinessProcessDeactivatedEvent,
            "real true->false transition must produce the outbox event in the same SaveChanges");
        await _repo.Received(1).UpdateAsync(bp, Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveByPrefixAsync(
            $"bp:{CurrentTenantId}:", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyInactive_NoOpStillInvalidatesCache()
    {
        var bp = BusinessProcessDefinition.Create(_gen, CurrentTenantId, "BP", "Test", 42L, ActorId);
        bp.ClearDomainEvents();
        _repo.GetByPublicIdAsync(bp.PublicId, Arg.Any<CancellationToken>()).Returns(bp);

        var result = await _handler.Handle(
            new DeactivateBusinessProcessCommand(CurrentTenantId, bp.PublicId, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bp.IsActive.Should().BeFalse();
        bp.DomainEvents.OfType<BusinessProcessDeactivatedEvent>().Should().BeEmpty(
            "idempotent Deactivate must not enqueue a duplicate outbox event");
        await _repo.Received(1).UpdateAsync(bp, Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveByPrefixAsync(
            $"bp:{CurrentTenantId}:", Arg.Any<CancellationToken>());
    }
}
