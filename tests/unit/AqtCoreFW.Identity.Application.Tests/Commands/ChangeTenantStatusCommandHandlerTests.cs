using AqtCoreFW.Identity.Application.Commands.ChangeTenantStatus;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Events;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Unit tests for ChangeTenantStatusCommandHandler.
/// Verifies: Active→Archived (soft-delete), status transitions, event publishing.
/// </summary>
public sealed class ChangeTenantStatusCommandHandlerTests
{
    private readonly ITenantRepository _tenantRepository = Substitute.For<ITenantRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly ChangeTenantStatusCommandHandler _sut;

    private static readonly Guid ActorId = Guid.NewGuid();

    public ChangeTenantStatusCommandHandlerTests()
    {
        _guidGen.NewIdMonotonic().Returns(Guid.NewGuid());
        _guidGen.NewIdRandom().Returns(Guid.NewGuid());
        _sut = new ChangeTenantStatusCommandHandler(_tenantRepository, _publisher, _guidGen);
    }

    [Fact]
    public async Task Handle_ActiveToArchived_SoftDeleteSucceeds()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var existingTenant = Tenant.Create(_guidGen, "Test Tenant", "TEST", null, ActorId);
        typeof(Tenant).GetProperty("PublicId")!.SetValue(existingTenant, tenantId);
        existingTenant.Status.Should().Be(TenantStatus.Active);

        _tenantRepository.GetByPublicIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(existingTenant);

        var cmd = new ChangeTenantStatusCommand(
            tenantId,
            TenantStatus.Archived,
            ActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        existingTenant.Status.Should().Be(TenantStatus.Archived);

        await _tenantRepository.Received(1).UpdateAsync(existingTenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ActiveToSuspended_TransitionSucceeds()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var existingTenant = Tenant.Create(_guidGen, "Test Tenant", "TEST", null, ActorId);
        typeof(Tenant).GetProperty("PublicId")!.SetValue(existingTenant, tenantId);

        _tenantRepository.GetByPublicIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(existingTenant);

        var cmd = new ChangeTenantStatusCommand(
            tenantId,
            TenantStatus.Suspended,
            ActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        existingTenant.Status.Should().Be(TenantStatus.Suspended);
    }

    [Fact]
    public async Task Handle_SuspendedToActive_ReactivationSucceeds()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var existingTenant = Tenant.Create(_guidGen, "Test Tenant", "TEST", null, ActorId);
        typeof(Tenant).GetProperty("PublicId")!.SetValue(existingTenant, tenantId);
        existingTenant.ChangeStatus(TenantStatus.Suspended, ActorId, _guidGen);
        existingTenant.ClearDomainEvents();
        existingTenant.Status.Should().Be(TenantStatus.Suspended);

        _tenantRepository.GetByPublicIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(existingTenant);

        var cmd = new ChangeTenantStatusCommand(
            tenantId,
            TenantStatus.Active,
            ActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        existingTenant.Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public async Task Handle_SameStatus_NoOpSucceeds()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var existingTenant = Tenant.Create(_guidGen, "Test Tenant", "TEST", null, ActorId);
        typeof(Tenant).GetProperty("PublicId")!.SetValue(existingTenant, tenantId);
        existingTenant.ClearDomainEvents();

        _tenantRepository.GetByPublicIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(existingTenant);

        var cmd = new ChangeTenantStatusCommand(
            tenantId,
            TenantStatus.Active, // Same as current
            ActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        existingTenant.Status.Should().Be(TenantStatus.Active);
        // No event should be raised for same status
        existingTenant.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TenantNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantRepository.GetByPublicIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns((Tenant?)null);

        var cmd = new ChangeTenantStatusCommand(
            tenantId,
            TenantStatus.Archived,
            ActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("không tồn tại"));

        await _tenantRepository.DidNotReceive().UpdateAsync(
            Arg.Any<Tenant>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StatusChange_PublishesTenantStatusChangedEvent()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var existingTenant = Tenant.Create(_guidGen, "Test Tenant", "TEST", null, ActorId);
        typeof(Tenant).GetProperty("PublicId")!.SetValue(existingTenant, tenantId);
        existingTenant.ClearDomainEvents();

        _tenantRepository.GetByPublicIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(existingTenant);

        var cmd = new ChangeTenantStatusCommand(
            tenantId,
            TenantStatus.Suspended,
            ActorId);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert — handler publishes via non-generic IPublisher.Publish(object, ct).
        // Use `as` cast (Expression-tree compatible — `is` pattern is not).
        await _publisher.Received(1).Publish(
            Arg.Is<object>(e => (e as TenantStatusChangedEvent) != null
                && ((TenantStatusChangedEvent)e).TenantId == tenantId
                && ((TenantStatusChangedEvent)e).OldStatus == TenantStatus.Active
                && ((TenantStatusChangedEvent)e).NewStatus == TenantStatus.Suspended),
            Arg.Any<CancellationToken>());
    }
}
