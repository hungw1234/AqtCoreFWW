using AqtCoreFW.Identity.Application.Commands.ActivateRole;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Events;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for ActivateRoleCommandHandler.
/// Verifies: success, not found, already active (idempotent), tenant isolation, cache invalidation.
/// </summary>
public sealed class ActivateRoleCommandHandlerTests
{
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly IBulkRoleChangeService _bulkRoleChangeService = Substitute.For<IBulkRoleChangeService>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly ActivateRoleCommandHandler _sut;

    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public ActivateRoleCommandHandlerTests()
    {
        _guidGen.NewIdRandom().Returns(Guid.NewGuid());
        _sut = new ActivateRoleCommandHandler(_roleRepository, _bulkRoleChangeService, _publisher, _guidGen);
    }

    [Fact]
    public async Task Handle_InactiveRole_ActivatesAndInvalidatesCache()
    {
        // Arrange
        var cmd = new ActivateRoleCommand(RoleId, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Manager",
            Code = "MANAGER",
            RoleType = RoleType.Business,
            TenantId = TenantId,
            IsActive = false  // Currently inactive
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _roleRepository.Received(1).UpdateAsync(
            Arg.Is<ApplicationRole>(r =>
                r.Id == RoleId &&
                r.IsActive),
            Arg.Any<CancellationToken>());

        await _bulkRoleChangeService.Received(1).InvalidateRoleCacheForAllUsersAsync();
    }

    [Fact]
    public async Task Handle_RoleNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var cmd = new ActivateRoleCommand(RoleId, ActorId, TenantId);

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns((ApplicationRole?)null);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("không tồn tại"));

        await _roleRepository.DidNotReceive().UpdateAsync(
            Arg.Any<ApplicationRole>(),
            Arg.Any<CancellationToken>());

        await _bulkRoleChangeService.DidNotReceive().InvalidateRoleCacheForAllUsersAsync();
    }

    [Fact]
    public async Task Handle_AlreadyActive_ReturnsSuccessIdempotent()
    {
        // Arrange
        var cmd = new ActivateRoleCommand(RoleId, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Manager",
            Code = "MANAGER",
            RoleType = RoleType.Business,
            TenantId = TenantId,
            IsActive = true  // Already active
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Idempotent: should not call UpdateAsync when already active
        await _roleRepository.DidNotReceive().UpdateAsync(
            Arg.Any<ApplicationRole>(),
            Arg.Any<CancellationToken>());

        // And should not invalidate cache if no change made
        await _bulkRoleChangeService.DidNotReceive().InvalidateRoleCacheForAllUsersAsync();
    }

    [Fact]
    public async Task Handle_TenantMismatch_ReturnsForbiddenError()
    {
        // Arrange
        var otherTenantId = Guid.NewGuid();
        var cmd = new ActivateRoleCommand(RoleId, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Manager",
            Code = "MANAGER",
            RoleType = RoleType.Business,
            TenantId = otherTenantId,  // Different tenant
            IsActive = false
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("Không có quyền"));

        await _roleRepository.DidNotReceive().UpdateAsync(
            Arg.Any<ApplicationRole>(),
            Arg.Any<CancellationToken>());

        await _bulkRoleChangeService.DidNotReceive().InvalidateRoleCacheForAllUsersAsync();
    }

    [Fact]
    public async Task Handle_PublishesRoleActivatedEvent()
    {
        // Arrange
        var cmd = new ActivateRoleCommand(RoleId, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Viewer",
            Code = "VIEWER",
            RoleType = RoleType.Business,
            TenantId = TenantId,
            IsActive = false
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        await _publisher.Received(1).Publish(
            Arg.Is<RoleActivatedEvent>(e =>
                e.RoleId == RoleId &&
                e.ActorId == ActorId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullTenantRole_AllowsActivation()
    {
        // Arrange - Role with null TenantId (global system role)
        var cmd = new ActivateRoleCommand(RoleId, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Global Viewer",
            Code = "GLOBAL_VIEWER",
            RoleType = RoleType.System,
            TenantId = null,  // Global role
            IsActive = false
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _roleRepository.Received(1).UpdateAsync(
            Arg.Any<ApplicationRole>(),
            Arg.Any<CancellationToken>());

        await _bulkRoleChangeService.Received(1).InvalidateRoleCacheForAllUsersAsync();
    }

    [Fact]
    public async Task Handle_CacheInvalidationCalledAfterStatusChange()
    {
        // Arrange
        var cmd = new ActivateRoleCommand(RoleId, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Editor",
            Code = "EDITOR",
            RoleType = RoleType.Business,
            TenantId = TenantId,
            IsActive = false
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // RT-05: Cache invalidation for permission re-activation
        await _bulkRoleChangeService.Received(1).InvalidateRoleCacheForAllUsersAsync();
    }

    [Fact]
    public async Task Handle_SystemRole_ActivatesSuccessfully()
    {
        // Arrange
        var cmd = new ActivateRoleCommand(RoleId, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Administrator",
            Code = "ADMINISTRATOR",
            RoleType = RoleType.System,
            TenantId = TenantId,
            IsActive = false
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _roleRepository.Received(1).UpdateAsync(
            Arg.Is<ApplicationRole>(r => r.IsActive),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EventPublishingAlwaysCalledBeforeCacheInvalidation()
    {
        // Arrange
        var cmd = new ActivateRoleCommand(RoleId, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Moderator",
            Code = "MODERATOR",
            RoleType = RoleType.Business,
            TenantId = TenantId,
            IsActive = false
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        Received.InOrder(async () =>
        {
            await _roleRepository.UpdateAsync(Arg.Any<ApplicationRole>(), Arg.Any<CancellationToken>());
            await _bulkRoleChangeService.InvalidateRoleCacheForAllUsersAsync();
            await _publisher.Publish(Arg.Any<RoleActivatedEvent>(), Arg.Any<CancellationToken>());
        });
    }
}
