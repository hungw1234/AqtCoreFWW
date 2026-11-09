using AqtCoreFW.Identity.Application.Commands.UpdateRole;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Events;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for UpdateRoleCommandHandler.
/// Verifies: success, not found, system role rename blocked, tenant isolation.
/// </summary>
public sealed class UpdateRoleCommandHandlerTests
{
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly UpdateRoleCommandHandler _sut;

    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public UpdateRoleCommandHandlerTests()
    {
        _guidGen.NewIdRandom().Returns(Guid.NewGuid());
        _sut = new UpdateRoleCommandHandler(_roleRepository, _publisher, _guidGen);
    }

    [Fact]
    public async Task Handle_ValidCommand_UpdatesRoleSuccessfully()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(RoleId, "Chuyên viên mới", "Description mới", ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Chuyên viên cũ",
            Code = "CHUYEN_VIEN",
            RoleType = RoleType.Business,
            TenantId = TenantId,
            IsActive = true,
            Description = "Description cũ"
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _roleRepository.Received(1).UpdateAsync(
            Arg.Is<ApplicationRole>(r =>
                r.Id == RoleId &&
                r.Name == "Chuyên viên mới" &&
                r.Description == "Description mới"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RoleNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(RoleId, "Manager", null, ActorId, TenantId);

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns((ApplicationRole?)null);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("không tồn tại"));

        await _roleRepository.DidNotReceive().UpdateAsync(
            Arg.Any<ApplicationRole>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TenantMismatch_ReturnsForbiddenError()
    {
        // Arrange
        var otherTenantId = Guid.NewGuid();
        var cmd = new UpdateRoleCommand(RoleId, "Manager", null, ActorId, TenantId);

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

        await _roleRepository.DidNotReceive().UpdateAsync(
            Arg.Any<ApplicationRole>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SystemRoleRename_ReturnsForbiddenError()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(RoleId, "New Administrator", null, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Administrator",  // Original name
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

        await _roleRepository.DidNotReceive().UpdateAsync(
            Arg.Any<ApplicationRole>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SystemRoleNameUnchanged_UpdatesSuccessfully()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(RoleId, "Administrator", "New description", ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Administrator",  // Same name
            Code = "ADMINISTRATOR",
            RoleType = RoleType.System,
            TenantId = TenantId,
            IsActive = true,
            Description = "Old description"
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _roleRepository.Received(1).UpdateAsync(
            Arg.Is<ApplicationRole>(r =>
                r.Name == "Administrator" &&
                r.Description == "New description"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PublishesRoleUpdatedEvent()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(RoleId, "Thư ký mới", null, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Thư ký cũ",
            Code = "THU_KY",
            RoleType = RoleType.Business,
            TenantId = TenantId,
            IsActive = true
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        await _publisher.Received(1).Publish(
            Arg.Is<RoleUpdatedEvent>(e =>
                e.RoleId == RoleId &&
                e.Name == "Thư ký mới" &&
                e.ActorId == ActorId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullDescriptionAllowed_UpdatesSuccessfully()
    {
        // Arrange
        var cmd = new UpdateRoleCommand(RoleId, "Manager", null, ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Manager Old",
            Code = "MANAGER",
            RoleType = RoleType.Business,
            TenantId = TenantId,
            IsActive = true,
            Description = "Old description"
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _roleRepository.Received(1).UpdateAsync(
            Arg.Is<ApplicationRole>(r =>
                r.Name == "Manager" &&
                r.Description == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullTenantRole_AllowsUpdate()
    {
        // Arrange - Role with null TenantId (global system role) can be updated by any tenant
        // The handler checks: if (role.TenantId is not null && role.TenantId != cmd.TenantId)
        // So if role.TenantId is null, the check passes and update is allowed
        var cmd = new UpdateRoleCommand(RoleId, "Global Manager", "New description", ActorId, TenantId);

        var role = new ApplicationRole
        {
            Id = RoleId,
            Name = "Global Manager Old",
            Code = "GLOBAL_MANAGER",
            RoleType = RoleType.Business,  // Not system, so name can be changed
            TenantId = null,  // Global role (null passes tenant isolation check)
            IsActive = true
        };

        _roleRepository.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _roleRepository.Received(1).UpdateAsync(
            Arg.Is<ApplicationRole>(r =>
                r.Name == "Global Manager" &&
                r.Description == "New description"),
            Arg.Any<CancellationToken>());
    }
}
