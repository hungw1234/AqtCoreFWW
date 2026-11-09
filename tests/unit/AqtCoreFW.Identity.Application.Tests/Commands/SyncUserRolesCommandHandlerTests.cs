using AqtCoreFW.Identity.Application.Commands.RevokeToken;
using AqtCoreFW.Identity.Application.Commands.SyncUserRoles;
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
/// Tests for SyncUserRolesCommandHandler.
/// Covers: successful role sync, system-role privilege-escalation guard (C2 for sync),
/// business-role bypass of guard, and token revocation on change.
/// </summary>
public sealed class SyncUserRolesCommandHandlerTests
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ICacheService _cacheService;
    private readonly IMediator _mediator;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SyncUserRolesCommandHandler _sut;

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SystemAdminActorId = Guid.NewGuid();
    private static readonly Guid NonAdminActorId = Guid.NewGuid();

    public SyncUserRolesCommandHandlerTests()
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

        _sut = new SyncUserRolesCommandHandler(_userRepository, _roleRepository, _cacheService, _mediator, _userManager);
    }

    // --- Happy path: business roles only ---

    [Fact]
    public async Task Handle_SyncBusinessRolesAsNonAdmin_Succeeds()
    {
        // Arrange
        var user = new ApplicationUser { Id = UserId, TenantId = TenantId };
        var businessRole1 = new ApplicationRole { Id = Guid.NewGuid(), Name = "Bộ trưởng", RoleType = RoleType.Business };
        var businessRole2 = new ApplicationRole { Id = Guid.NewGuid(), Name = "Cán bộ", RoleType = RoleType.Business };

        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetByNameInTenantScopeAsync("Bộ trưởng", TenantId, Arg.Any<CancellationToken>()).Returns(businessRole1);
        _roleRepository.GetByNameInTenantScopeAsync("Cán bộ", TenantId, Arg.Any<CancellationToken>()).Returns(businessRole2);
        _userRepository.GetRoleIdsAsync(UserId, Arg.Any<CancellationToken>()).Returns(new[] { businessRole1.Id });
        _userRepository.RemoveFromRolesAsync(UserId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _userRepository.AddToRoleAsync(UserId, businessRole2.Id, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _cacheService.RemoveAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        _mediator.Send(Arg.Any<RevokeTokenCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var cmd = new SyncUserRolesCommand(UserId, new[] { "Bộ trưởng", "Cán bộ" }, NonAdminActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _userRepository.Received(1).RemoveFromRolesAsync(UserId, Arg.Is<IReadOnlyCollection<Guid>>(ids => !ids.Contains(businessRole1.Id)), Arg.Any<CancellationToken>());
        await _userRepository.Received(1).AddToRoleAsync(UserId, businessRole2.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SyncRolesWithChanges_RevokesCachedTokens()
    {
        // Arrange
        var user = new ApplicationUser { Id = UserId, TenantId = TenantId };
        var businessRole1 = new ApplicationRole { Id = Guid.NewGuid(), Name = "Role1", RoleType = RoleType.Business };
        var businessRole2 = new ApplicationRole { Id = Guid.NewGuid(), Name = "Role2", RoleType = RoleType.Business };

        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetByNameInTenantScopeAsync("Role1", TenantId, Arg.Any<CancellationToken>()).Returns(businessRole1);
        _roleRepository.GetByNameInTenantScopeAsync("Role2", TenantId, Arg.Any<CancellationToken>()).Returns(businessRole2);
        _userRepository.GetRoleIdsAsync(UserId, Arg.Any<CancellationToken>()).Returns(new[] { businessRole1.Id });
        _userRepository.RemoveFromRolesAsync(UserId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _userRepository.AddToRoleAsync(UserId, businessRole2.Id, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _cacheService.RemoveAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        _mediator.Send(Arg.Any<RevokeTokenCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var cmd = new SyncUserRolesCommand(UserId, new[] { "Role1", "Role2" }, NonAdminActorId);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        await _mediator.Received(1).Send(
            Arg.Is<RevokeTokenCommand>(c => c.UserId == UserId && c.TokenId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SyncRolesNoChanges_DoesNotRevokeTokens()
    {
        // Arrange
        var user = new ApplicationUser { Id = UserId, TenantId = TenantId };
        var businessRole1 = new ApplicationRole { Id = Guid.NewGuid(), Name = "Role1", RoleType = RoleType.Business };

        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetByNameInTenantScopeAsync("Role1", TenantId, Arg.Any<CancellationToken>()).Returns(businessRole1);
        _userRepository.GetRoleIdsAsync(UserId, Arg.Any<CancellationToken>()).Returns(new[] { businessRole1.Id });
        _userRepository.RemoveFromRolesAsync(UserId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _cacheService.RemoveAsync(Arg.Any<string>()).Returns(Task.CompletedTask);

        var cmd = new SyncUserRolesCommand(UserId, new[] { "Role1" }, NonAdminActorId);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert (token revocation only happens if roleIdsToRemove.Count > 0 || roleIdsToAdd.Count > 0)
        await _mediator.DidNotReceive().Send(Arg.Any<RevokeTokenCommand>(), Arg.Any<CancellationToken>());
    }

    // --- C2 Guard: Adding system role requires SystemAdmin actor ---

    [Fact]
    public async Task Handle_AddSystemRoleAsNonSystemAdmin_ReturnsForbidden()
    {
        // Arrange
        var user = new ApplicationUser { Id = UserId, TenantId = TenantId };
        var systemRole = new ApplicationRole { Id = Guid.NewGuid(), Name = "Admin", RoleType = RoleType.System };

        var nonAdminUser = new ApplicationUser { Id = NonAdminActorId };

        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetByNameInTenantScopeAsync("Admin", TenantId, Arg.Any<CancellationToken>()).Returns(systemRole);
        _userRepository.GetRoleIdsAsync(UserId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        _userManager.FindByIdAsync(NonAdminActorId.ToString()).Returns(nonAdminUser);
        _userManager.IsInRoleAsync(nonAdminUser, "SystemAdmin").Returns(false);

        var cmd = new SyncUserRolesCommand(UserId, new[] { "Admin" }, NonAdminActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("SystemAdmin"));
        await _userRepository.DidNotReceive().RemoveFromRolesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AddBusinessTypedSystemAdminRoleAsNonAdmin_ReturnsForbidden()
    {
        // A seeder may create the SystemAdmin/Admin role defaulted to RoleType.Business. The guard must
        // still fire on the privileged NAME so the C2 protection is not silently inert.
        var user = new ApplicationUser { Id = UserId, TenantId = TenantId };
        var misTypedRole = new ApplicationRole { Id = Guid.NewGuid(), Name = "SystemAdmin", RoleType = RoleType.Business };
        var nonAdminUser = new ApplicationUser { Id = NonAdminActorId };

        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetByNameInTenantScopeAsync("SystemAdmin", TenantId, Arg.Any<CancellationToken>()).Returns(misTypedRole);
        _userRepository.GetRoleIdsAsync(UserId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        _userManager.FindByIdAsync(NonAdminActorId.ToString()).Returns(nonAdminUser);
        _userManager.IsInRoleAsync(nonAdminUser, "SystemAdmin").Returns(false);

        var cmd = new SyncUserRolesCommand(UserId, new[] { "SystemAdmin" }, NonAdminActorId);

        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("SystemAdmin"));
        await _userRepository.DidNotReceive().RemoveFromRolesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AddSystemRoleAsSystemAdmin_Succeeds()
    {
        // Arrange
        var user = new ApplicationUser { Id = UserId, TenantId = TenantId };
        var systemRole = new ApplicationRole { Id = Guid.NewGuid(), Name = "SystemAdmin", RoleType = RoleType.System };

        var systemAdminUser = new ApplicationUser { Id = SystemAdminActorId };

        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetByNameInTenantScopeAsync("SystemAdmin", TenantId, Arg.Any<CancellationToken>()).Returns(systemRole);
        _userRepository.GetRoleIdsAsync(UserId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        _userManager.FindByIdAsync(SystemAdminActorId.ToString()).Returns(systemAdminUser);
        _userManager.IsInRoleAsync(systemAdminUser, "SystemAdmin").Returns(true);
        _userRepository.RemoveFromRolesAsync(UserId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _userRepository.AddToRoleAsync(UserId, systemRole.Id, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _cacheService.RemoveAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        _mediator.Send(Arg.Any<RevokeTokenCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var cmd = new SyncUserRolesCommand(UserId, new[] { "SystemAdmin" }, SystemAdminActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _userRepository.Received(1).AddToRoleAsync(UserId, systemRole.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RemoveSystemRoleAsNonAdmin_Succeeds()
    {
        // Arrange: removing a system role doesn't trigger the guard, only ADDING does
        var user = new ApplicationUser { Id = UserId, TenantId = TenantId };
        var systemRole = new ApplicationRole { Id = Guid.NewGuid(), Name = "Admin", RoleType = RoleType.System };

        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        _userRepository.GetRoleIdsAsync(UserId, Arg.Any<CancellationToken>()).Returns(new[] { systemRole.Id });
        _userRepository.RemoveFromRolesAsync(UserId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(systemRole.Id)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _cacheService.RemoveAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        _mediator.Send(Arg.Any<RevokeTokenCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var cmd = new SyncUserRolesCommand(UserId, Array.Empty<string>(), NonAdminActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _userRepository.Received(1).RemoveFromRolesAsync(UserId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(systemRole.Id)), Arg.Any<CancellationToken>());
    }

    // --- Negative: User or role not found ---

    [Fact]
    public async Task Handle_UserNotFound_ReturnsNotFound()
    {
        // Arrange
        _userRepository.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((ApplicationUser?)null);

        var cmd = new SyncUserRolesCommand(UserId, new[] { "Role1" }, NonAdminActorId);

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

        var cmd = new SyncUserRolesCommand(UserId, new[] { "Nonexistent" }, NonAdminActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }
}
