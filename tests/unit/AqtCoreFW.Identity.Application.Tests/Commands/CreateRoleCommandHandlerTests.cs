using AqtCoreFW.Identity.Application.Commands.CreateRole;
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
/// Tests for CreateRoleCommandHandler.
/// Verifies: success path, duplicate code conflict, event publishing, tenant scoping.
/// </summary>
public sealed class CreateRoleCommandHandlerTests
{
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly CreateRoleCommandHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public CreateRoleCommandHandlerTests()
    {
        _guidGen.NewIdRandom().Returns(_ => Guid.NewGuid());
        _sut = new CreateRoleCommandHandler(_roleRepository, _publisher, _guidGen);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithNewRoleId()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Chuyên viên xử lý",
            "CHUYEN_VIEN_XU_LY",
            "Vai trò xử lý hồ sơ",
            RoleType.Business,
            TenantId,
            ActorId);

        _roleRepository.ExistsByCodeAndTenantAsync(cmd.Code, cmd.TenantId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);

        // Verify repository was called
        await _roleRepository.Received(1).AddAsync(
            Arg.Is<ApplicationRole>(r =>
                r.Name == "Chuyên viên xử lý" &&
                r.Code == "CHUYEN_VIEN_XU_LY" &&
                r.RoleType == RoleType.Business &&
                r.TenantId == TenantId &&
                r.IsActive),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateCode_ReturnsConflictError()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Quản lý",
            "QUAN_LY",
            null,
            RoleType.Business,
            TenantId,
            ActorId);

        _roleRepository.ExistsByCodeAndTenantAsync(cmd.Code, cmd.TenantId, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("đã tồn tại"));

        // Verify repository.AddAsync was not called
        await _roleRepository.DidNotReceive().AddAsync(
            Arg.Any<ApplicationRole>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PublishesRoleCreatedEvent()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Thư ký",
            "THU_KY",
            "Vai trò thư ký",
            RoleType.Business,
            TenantId,
            ActorId);

        _roleRepository.ExistsByCodeAndTenantAsync(cmd.Code, cmd.TenantId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        await _publisher.Received(1).Publish(
            Arg.Is<RoleCreatedEvent>(e =>
                e.Code == "THU_KY" &&
                e.Name == "Thư ký" &&
                e.ActorId == ActorId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SystemRole_CreatesSuccessfully()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Administrator",
            "ADMINISTRATOR",
            "System role for administrators",
            RoleType.System,
            TenantId,
            ActorId);

        _roleRepository.ExistsByCodeAndTenantAsync(cmd.Code, cmd.TenantId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _roleRepository.Received(1).AddAsync(
            Arg.Is<ApplicationRole>(r =>
                r.RoleType == RoleType.System &&
                r.IsActive),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PreservesAllRoleProperties()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Trưởng phòng",
            "TRUONG_PHONG",
            "Người quản lý một phòng ban",
            RoleType.Business,
            TenantId,
            ActorId);

        _roleRepository.ExistsByCodeAndTenantAsync(cmd.Code, cmd.TenantId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        await _roleRepository.Received(1).AddAsync(
            Arg.Is<ApplicationRole>(r =>
                r.Name == "Trưởng phòng" &&
                r.Code == "TRUONG_PHONG" &&
                r.Description == "Người quản lý một phòng ban" &&
                r.RoleType == RoleType.Business &&
                r.TenantId == TenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullDescription_AllowedByHandler()
    {
        // Arrange
        var cmd = new CreateRoleCommand(
            "Viewer",
            "VIEWER",
            null,  // No description
            RoleType.Business,
            TenantId,
            ActorId);

        _roleRepository.ExistsByCodeAndTenantAsync(cmd.Code, cmd.TenantId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _roleRepository.Received(1).AddAsync(
            Arg.Is<ApplicationRole>(r => r.Description == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MultiTenant_IsolatesRoles()
    {
        // Arrange
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        var cmd1 = new CreateRoleCommand("Manager", "MANAGER", null, RoleType.Business, tenant1, ActorId);
        var cmd2 = new CreateRoleCommand("Manager", "MANAGER", null, RoleType.Business, tenant2, ActorId);

        _roleRepository.ExistsByCodeAndTenantAsync("MANAGER", tenant1, Arg.Any<CancellationToken>())
            .Returns(false);
        _roleRepository.ExistsByCodeAndTenantAsync("MANAGER", tenant2, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result1 = await _sut.Handle(cmd1, CancellationToken.None);
        var result2 = await _sut.Handle(cmd2, CancellationToken.None);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        result1.Value.Should().NotBe(result2.Value);
    }
}
