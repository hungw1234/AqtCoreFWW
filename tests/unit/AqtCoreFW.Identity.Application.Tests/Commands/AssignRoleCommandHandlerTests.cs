using AqtCoreFW.Identity.Application.Commands.AssignRole;
using AqtCoreFW.Identity.Application.Commands.RevokeToken;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Caching;
using FluentAssertions;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for AssignRoleCommandHandler.
/// Covers: successful role assignment, system-role privilege-escalation guard (C2),
/// business-role bypass of guard, and token revocation.
/// </summary>
public sealed class AssignRoleCommandHandlerTests
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ICacheService _cacheService;
    private readonly IMediator _mediator;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AssignRoleCommandHandler _sut;

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SystemAdminActorId = Guid.NewGuid();
    private static readonly Guid NonAdminActorId = Guid.NewGuid();

    public AssignRoleCommandHandlerTests()
    {
        _userRepository = Substitute.For<IUserRepository>();
        _roleRepository = Substitute.For<IRoleRepository>();
        _cacheService = Substitute.For<ICacheService>();
        _mediator = Substitute.For<IMediator>();

        var store = Substitute.For<IUserStore<ApplicationUser>,
                                   IUserPasswordStore<ApplicationUser>>();
        _userManager = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());

        _sut = new AssignRoleCommandHandler(_userRepository, _roleRepository, _cacheService, _mediator, _userManager);
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_AssignBusinessRoleByNonAdmin_Succeeds()
    {
        // Arrange
        var user = new ApplicationUser { Id = UserId, TenantId = TenantId };
        var businessRole = new ApplicationRole { Id = Guid.NewGuid(), Name = "Bộ trưởng", RoleType = RoleType.Business };

        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetByNameInTenantScopeAsync("Bộ trưởng", TenantId, Arg.Any<CancellationToken>()).Returns(businessRole);
        _userRepository.AddToRoleAsync(UserId, businessRole.Id, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _cacheService.RemoveAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        _mediator.Send(Arg.Any<RevokeTokenCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var cmd = new AssignRoleCommand(UserId, "Bộ trưởng", NonAdminActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _userRepository.Received(1).AddToRoleAsync(UserId, businessRole.Id, Arg.Any<CancellationToken>());
        await _cacheService.Received(1).RemoveAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_AssignBusinessRole_RevokesCachedTokens()
    {
        // Arrange
        var user = new ApplicationUser { Id = UserId, TenantId = TenantId };
        var businessRole = new ApplicationRole { Id = Guid.NewGuid(), Name = "Cán bộ", RoleType = RoleType.Business };

        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetByNameInTenantScopeAsync("Cán bộ", TenantId, Arg.Any<CancellationToken>()).Returns(businessRole);
        _userRepository.AddToRoleAsync(UserId, businessRole.Id, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _cacheService.RemoveAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        _mediator.Send(Arg.Any<RevokeTokenCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var cmd = new AssignRoleCommand(UserId, "Cán bộ", NonAdminActorId);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        await _mediator.Received(1).Send(
            Arg.Is<RevokeTokenCommand>(c => c.UserId == UserId && c.TokenId == null),
            Arg.Any<CancellationToken>());
    }

    // --- C2 Guard: System role requires SystemAdmin actor ---

    [Fact]
    public async Task Handle_AssignSystemRoleAsNonSystemAdmin_ReturnsForbidden()
    {
        // Arrange
        var user = new ApplicationUser { Id = UserId, TenantId = TenantId };
        var systemRole = new ApplicationRole { Id = Guid.NewGuid(), Name = "Admin", RoleType = RoleType.System };

        var nonAdminUser = new ApplicationUser { Id = NonAdminActorId };

        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetByNameInTenantScopeAsync("Admin", TenantId, Arg.Any<CancellationToken>()).Returns(systemRole);
        _userManager.FindByIdAsync(NonAdminActorId.ToString()).Returns(nonAdminUser);
        _userManager.IsInRoleAsync(nonAdminUser, "SystemAdmin").Returns(false);

        var cmd = new AssignRoleCommand(UserId, "Admin", NonAdminActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("SystemAdmin"));
        await _userRepository.DidNotReceive().AddToRoleAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AssignSystemRoleAsSystemAdmin_Succeeds()
    {
        // Arrange
        var user = new ApplicationUser { Id = UserId, TenantId = TenantId };
        var systemRole = new ApplicationRole { Id = Guid.NewGuid(), Name = "SystemAdmin", RoleType = RoleType.System };

        var systemAdminUser = new ApplicationUser { Id = SystemAdminActorId };

        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetByNameInTenantScopeAsync("SystemAdmin", TenantId, Arg.Any<CancellationToken>()).Returns(systemRole);
        _userManager.FindByIdAsync(SystemAdminActorId.ToString()).Returns(systemAdminUser);
        _userManager.IsInRoleAsync(systemAdminUser, "SystemAdmin").Returns(true);
        _userRepository.AddToRoleAsync(UserId, systemRole.Id, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _cacheService.RemoveAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        _mediator.Send(Arg.Any<RevokeTokenCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var cmd = new AssignRoleCommand(UserId, "SystemAdmin", SystemAdminActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _userRepository.Received(1).AddToRoleAsync(UserId, systemRole.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AssignBusinessTypedSystemAdminRoleAsNonAdmin_ReturnsForbidden()
    {
        // A seeder may create the SystemAdmin/Admin role without setting RoleType (defaults to Business).
        // The guard must still fire on the privileged NAME — not only RoleType.System — or it is inert.
        var user = new ApplicationUser { Id = UserId, TenantId = TenantId };
        var misTypedRole = new ApplicationRole { Id = Guid.NewGuid(), Name = "SystemAdmin", RoleType = RoleType.Business };
        var nonAdminUser = new ApplicationUser { Id = NonAdminActorId };

        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetByNameInTenantScopeAsync("SystemAdmin", TenantId, Arg.Any<CancellationToken>()).Returns(misTypedRole);
        _userManager.FindByIdAsync(NonAdminActorId.ToString()).Returns(nonAdminUser);
        _userManager.IsInRoleAsync(nonAdminUser, "SystemAdmin").Returns(false);

        var cmd = new AssignRoleCommand(UserId, "SystemAdmin", NonAdminActorId);

        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("SystemAdmin"));
        await _userRepository.DidNotReceive().AddToRoleAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AssignSystemRoleWithNullActor_ReturnsForbidden()
    {
        // Arrange: UserManager returns null (actor not found)
        var user = new ApplicationUser { Id = UserId, TenantId = TenantId };
        var systemRole = new ApplicationRole { Id = Guid.NewGuid(), Name = "Admin", RoleType = RoleType.System };

        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetByNameInTenantScopeAsync("Admin", TenantId, Arg.Any<CancellationToken>()).Returns(systemRole);
        _userManager.FindByIdAsync(NonAdminActorId.ToString()).Returns((ApplicationUser?)null);

        var cmd = new AssignRoleCommand(UserId, "Admin", NonAdminActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("SystemAdmin"));
    }

    // --- Negative: User or role not found ---

    [Fact]
    public async Task Handle_UserNotFound_ReturnsNotFound()
    {
        // Arrange
        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((ApplicationUser?)null);

        var cmd = new AssignRoleCommand(UserId, "Bộ trưởng", NonAdminActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Handle_RoleNotFound_ReturnsNotFound()
    {
        // Arrange
        var user = new ApplicationUser { Id = UserId, TenantId = TenantId };

        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetByNameInTenantScopeAsync("Nonexistent", TenantId, Arg.Any<CancellationToken>()).Returns((ApplicationRole?)null);

        var cmd = new AssignRoleCommand(UserId, "Nonexistent", NonAdminActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }
}
