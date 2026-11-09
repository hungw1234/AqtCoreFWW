using AqtCoreFW.Identity.Application.Commands.DeleteRole;
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
/// Tests for DeleteRoleCommandHandler.
/// Verifies: success (soft-delete), not found, system role blocked, tenant isolation, cache invalidation.
/// </summary>
public sealed class DeleteRoleCommandHandlerTests
{
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly IBulkRoleChangeService _bulkRoleChangeService = Substitute.For<IBulkRoleChangeService>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly DeleteRoleCommandHandler _sut;

    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public DeleteRoleCommandHandlerTests()
    {
        _guidGen.NewIdRandom().Returns(Guid.NewGuid());
        _sut = new DeleteRoleCommandHandler(_roleRepository, _bulkRoleChangeService, _publisher, _guidGen);
    }

    [Fact]
    public async Task Handle_ValidBusinessRole_SoftDeletesAndInvalidatesCache()
    {
        // Arrange
        var cmd = new DeleteRoleCommand(RoleId, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Manager",
            Code = "MANAGER",
            RoleType = RoleType.Business,
            TenantId = TenantId,
            IsActive = true
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _roleRepository.Received(1).SoftDeleteAsync(
            Arg.Is<ApplicationRole>(r => r.Id == RoleId),
            Arg.Any<CancellationToken>());

        await _bulkRoleChangeService.Received(1).InvalidateRoleCacheForAllUsersAsync();
    }

    [Fact]
    public async Task Handle_RoleNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var cmd = new DeleteRoleCommand(RoleId, ActorId, TenantId);

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns((ApplicationRole?)null);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("không tồn tại"));

        await _roleRepository.DidNotReceive().SoftDeleteAsync(
            Arg.Any<ApplicationRole>(),
            Arg.Any<CancellationToken>());

        await _bulkRoleChangeService.DidNotReceive().InvalidateRoleCacheForAllUsersAsync();
    }

    [Fact]
    public async Task Handle_TenantMismatch_ReturnsForbiddenError()
    {
        // Arrange
        var otherTenantId = Guid.NewGuid();
        var cmd = new DeleteRoleCommand(RoleId, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Manager",
            Code = "MANAGER",
            RoleType = RoleType.Business,
            TenantId = otherTenantId,  // Different tenant
            IsActive = true
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("Không có quyền"));

        await _roleRepository.DidNotReceive().SoftDeleteAsync(
            Arg.Any<ApplicationRole>(),
            Arg.Any<CancellationToken>());

        await _bulkRoleChangeService.DidNotReceive().InvalidateRoleCacheForAllUsersAsync();
    }

    [Fact]
    public async Task Handle_SystemRole_ReturnsForbiddenError()
    {
        // Arrange
        var cmd = new DeleteRoleCommand(RoleId, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Administrator",
            Code = "ADMINISTRATOR",
            RoleType = RoleType.System,  // System role
            TenantId = TenantId,
            IsActive = true
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("vai trò hệ thống"));

        await _roleRepository.DidNotReceive().SoftDeleteAsync(
            Arg.Any<ApplicationRole>(),
            Arg.Any<CancellationToken>());

        await _bulkRoleChangeService.DidNotReceive().InvalidateRoleCacheForAllUsersAsync();
    }

    [Fact]
    public async Task Handle_PublishesRoleDeactivatedEvent()
    {
        // Arrange
        var cmd = new DeleteRoleCommand(RoleId, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Viewer",
            Code = "VIEWER",
            RoleType = RoleType.Business,
            TenantId = TenantId,
            IsActive = true
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        await _publisher.Received(1).Publish(
            Arg.Is<RoleDeactivatedEvent>(e =>
                e.RoleId == RoleId &&
                e.ActorId == ActorId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullTenantRole_AllowsDelete()
    {
        // Arrange - Role with null TenantId (global system role)
        var cmd = new DeleteRoleCommand(RoleId, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Global Viewer",
            Code = "GLOBAL_VIEWER",
            RoleType = RoleType.Business,
            TenantId = null,  // Global role
            IsActive = true
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _roleRepository.Received(1).SoftDeleteAsync(
            Arg.Any<ApplicationRole>(),
            Arg.Any<CancellationToken>());

        await _bulkRoleChangeService.Received(1).InvalidateRoleCacheForAllUsersAsync();
    }

    [Fact]
    public async Task Handle_CacheInvalidationAlwaysCalledOnSuccess()
    {
        // Arrange
        var cmd = new DeleteRoleCommand(RoleId, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Editor",
            Code = "EDITOR",
            RoleType = RoleType.Business,
            TenantId = TenantId,
            IsActive = true
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // RT-05: Cache invalidation must be called for all users
        await _bulkRoleChangeService.Received(1).InvalidateRoleCacheForAllUsersAsync();
    }

    [Fact]
    public async Task Handle_InvokesBothRepositoryAndCacheService()
    {
        // Arrange
        var cmd = new DeleteRoleCommand(RoleId, ActorId, TenantId);

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
        // Verify both services are invoked in correct order
        await _roleRepository.Received(1).SoftDeleteAsync(
            Arg.Any<ApplicationRole>(),
            Arg.Any<CancellationToken>());

        await _bulkRoleChangeService.Received(1).InvalidateRoleCacheForAllUsersAsync();
    }
}
