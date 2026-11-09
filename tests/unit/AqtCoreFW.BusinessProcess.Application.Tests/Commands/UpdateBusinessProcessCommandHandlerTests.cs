using AqtCoreFW.BusinessProcess.Application.Commands.UpdateBusinessProcess;
using AqtCoreFW.BusinessProcess.Domain.Entities;
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
/// Unit tests for UpdateBusinessProcessCommandHandler — Pattern A guard + NotFound + happy path.
/// Update is metadata-only this slice; state/transition-form map edits live in a later slice.
/// </summary>
public sealed class UpdateBusinessProcessCommandHandlerTests
{
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid BpPublicId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private readonly IBusinessProcessRepository _repo = Substitute.For<IBusinessProcessRepository>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly ILogger<UpdateBusinessProcessCommandHandler> _logger =
        Substitute.For<ILogger<UpdateBusinessProcessCommandHandler>>();
    private readonly UpdateBusinessProcessCommandHandler _handler;

    public UpdateBusinessProcessCommandHandlerTests()
    {
        _tenantContext.TenantId.Returns(CurrentTenantId);
        _handler = new UpdateBusinessProcessCommandHandler(_repo, _tenantContext, _cache, _logger);
    }

    private static BusinessProcessDefinition Existing()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return BusinessProcessDefinition.Create(gen, CurrentTenantId, "BP", "Original", 42L, ActorId);
    }

    [Fact]
    public async Task Handle_CrossTenant_Rejected_BeforeAnyDbCall()
    {
        var result = await _handler.Handle(
            new UpdateBusinessProcessCommand(OtherTenantId, BpPublicId, "X", null, ActorId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await _repo.DidNotReceive().GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFoundError()
    {
        _repo.GetByPublicIdAsync(BpPublicId, Arg.Any<CancellationToken>())
            .Returns((BusinessProcessDefinition?)null);

        var result = await _handler.Handle(
            new UpdateBusinessProcessCommand(CurrentTenantId, BpPublicId, "New", null, ActorId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<BusinessProcessDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HappyPath_UpdatesAndInvalidatesCache()
    {
        var bp = Existing();
        _repo.GetByPublicIdAsync(bp.PublicId, Arg.Any<CancellationToken>()).Returns(bp);

        var result = await _handler.Handle(
            new UpdateBusinessProcessCommand(CurrentTenantId, bp.PublicId, "New Name", "New Desc", ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bp.Name.Should().Be("New Name");
        bp.Description.Should().Be("New Desc");
        await _repo.Received(1).UpdateAsync(bp, Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveByPrefixAsync(
            $"bp:{CurrentTenantId}:", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NameOnly_PreservesDescription()
    {
        // Partial-update permutation: caller sends Name only; aggregate's UpdateMetadata leaves
        // Description alone when null is passed (per aggregate's null=don't-touch semantics).
        var bp = Existing();
        _repo.GetByPublicIdAsync(bp.PublicId, Arg.Any<CancellationToken>()).Returns(bp);

        var result = await _handler.Handle(
            new UpdateBusinessProcessCommand(CurrentTenantId, bp.PublicId, "Only Name", Description: null, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bp.Name.Should().Be("Only Name");
        bp.Description.Should().BeNull("null Description must NOT clear the existing value");
    }

    [Fact]
    public async Task Handle_DescriptionOnly_PreservesName()
    {
        var bp = Existing();
        var originalName = bp.Name;
        _repo.GetByPublicIdAsync(bp.PublicId, Arg.Any<CancellationToken>()).Returns(bp);

        var result = await _handler.Handle(
            new UpdateBusinessProcessCommand(CurrentTenantId, bp.PublicId, Name: null, "Just description", ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bp.Name.Should().Be(originalName, "null Name must preserve the existing aggregate name");
        bp.Description.Should().Be("Just description");
    }
}
