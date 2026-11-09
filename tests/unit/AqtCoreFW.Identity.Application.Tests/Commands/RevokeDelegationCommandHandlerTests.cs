using System.Reflection;
using AqtCoreFW.Identity.Application.Commands.RevokeDelegation;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for RevokeDelegationCommandHandler authorization.
/// Covers: delegator revokes own, SystemAdmin/Admin override, rejection of an arbitrary caller who is
/// neither the delegator nor an administrator (broken-access-control guard), not-found, already-revoked.
/// </summary>
public sealed class RevokeDelegationCommandHandlerTests
{
    private readonly IDelegationRepository _delegationRepository;
    private readonly ICacheService _cacheService;
    private readonly IGuidGenerator _guidGenerator;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RevokeDelegationCommandHandler _sut;

    private static readonly Guid DelegatorId = Guid.NewGuid();
    private static readonly Guid DelegateId = Guid.NewGuid();
    private static readonly Guid RequesterId = Guid.NewGuid();
    private static readonly Guid SystemAdminId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid StrangerId = Guid.NewGuid();

    public RevokeDelegationCommandHandlerTests()
    {
        _delegationRepository = Substitute.For<IDelegationRepository>();
        _cacheService = Substitute.For<ICacheService>();
        _guidGenerator = Substitute.For<IGuidGenerator>();
        _guidGenerator.NewIdRandom().Returns(_ => Guid.NewGuid());

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

        _delegationRepository.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _cacheService.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _sut = new RevokeDelegationCommandHandler(_delegationRepository, _cacheService, _guidGenerator, _userManager);
    }

    // --- Happy path: the delegator may revoke their own delegation without any admin role ---

    [Fact]
    public async Task Handle_DelegatorRevokesOwnDelegation_Succeeds()
    {
        var publicId = Guid.NewGuid();
        var delegation = CreateMockDelegation(publicId, DelegatorId, DelegateId, RequesterId);
        _delegationRepository.GetByPublicIdAsync(publicId, Arg.Any<CancellationToken>()).Returns(delegation);

        var result = await _sut.Handle(new RevokeDelegationCommand(publicId, DelegatorId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        delegation.Status.Should().Be(DelegationStatus.Revoked);
        await _delegationRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _cacheService.Received(1).RemoveAsync($"delegation:{DelegateId}", Arg.Any<CancellationToken>());
        // The delegator is the owner — no UserManager lookup is needed to authorize.
        await _userManager.DidNotReceive().FindByIdAsync(Arg.Any<string>());
    }

    // --- Happy path: a SystemAdmin (not the delegator) may override ---

    [Fact]
    public async Task Handle_SystemAdminRevokesOthersDelegation_Succeeds()
    {
        var publicId = Guid.NewGuid();
        var delegation = CreateMockDelegation(publicId, DelegatorId, DelegateId, RequesterId);
        var actor = new ApplicationUser { Id = SystemAdminId };
        _delegationRepository.GetByPublicIdAsync(publicId, Arg.Any<CancellationToken>()).Returns(delegation);
        _userManager.FindByIdAsync(SystemAdminId.ToString()).Returns(actor);
        _userManager.IsInRoleAsync(actor, Roles.SystemAdmin).Returns(true);

        var result = await _sut.Handle(new RevokeDelegationCommand(publicId, SystemAdminId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _delegationRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- Happy path: an Admin (not the delegator) may override ---

    [Fact]
    public async Task Handle_AdminRevokesOthersDelegation_Succeeds()
    {
        var publicId = Guid.NewGuid();
        var delegation = CreateMockDelegation(publicId, DelegatorId, DelegateId, RequesterId);
        var actor = new ApplicationUser { Id = AdminId };
        _delegationRepository.GetByPublicIdAsync(publicId, Arg.Any<CancellationToken>()).Returns(delegation);
        _userManager.FindByIdAsync(AdminId.ToString()).Returns(actor);
        _userManager.IsInRoleAsync(actor, Roles.SystemAdmin).Returns(false);
        _userManager.IsInRoleAsync(actor, Roles.Admin).Returns(true);

        var result = await _sut.Handle(new RevokeDelegationCommand(publicId, AdminId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _delegationRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- Guard: a caller who is neither the delegator nor an administrator is rejected ---

    [Fact]
    public async Task Handle_NonOwnerNonAdmin_ReturnsForbidden_AndDoesNotRevoke()
    {
        var publicId = Guid.NewGuid();
        var delegation = CreateMockDelegation(publicId, DelegatorId, DelegateId, RequesterId);
        var actor = new ApplicationUser { Id = StrangerId };
        _delegationRepository.GetByPublicIdAsync(publicId, Arg.Any<CancellationToken>()).Returns(delegation);
        _userManager.FindByIdAsync(StrangerId.ToString()).Returns(actor);
        // No role mocks → IsInRoleAsync returns false for both Admin and SystemAdmin.

        var result = await _sut.Handle(new RevokeDelegationCommand(publicId, StrangerId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("delegator") || e.Message.Contains("administrator"));
        delegation.Status.Should().NotBe(DelegationStatus.Revoked);
        await _delegationRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _cacheService.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- Negative: delegation not found ---

    [Fact]
    public async Task Handle_DelegationNotFound_ReturnsNotFound()
    {
        _delegationRepository.GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((UserDelegation?)null);

        var result = await _sut.Handle(new RevokeDelegationCommand(Guid.NewGuid(), DelegatorId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
        await _delegationRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- Negative: already revoked (authorized actor) returns Conflict ---

    [Fact]
    public async Task Handle_AlreadyRevoked_ReturnsConflict()
    {
        var publicId = Guid.NewGuid();
        var delegation = CreateMockDelegation(publicId, DelegatorId, DelegateId, RequesterId, isRevoked: true);
        _delegationRepository.GetByPublicIdAsync(publicId, Arg.Any<CancellationToken>()).Returns(delegation);

        var result = await _sut.Handle(new RevokeDelegationCommand(publicId, DelegatorId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("already revoked"));
        await _delegationRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- Helpers ---

    private static UserDelegation CreateMockDelegation(
        Guid publicId,
        Guid delegatorId,
        Guid delegateId,
        Guid createdBy,
        bool isRevoked = false)
    {
        // UserDelegation has private setters; build an uninitialized instance and set state via reflection.
        var delegation = (UserDelegation)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(UserDelegation));
        typeof(UserDelegation).GetProperty(nameof(UserDelegation.PublicId))!.SetValue(delegation, publicId);
        typeof(UserDelegation).GetProperty(nameof(UserDelegation.DelegatorId))!.SetValue(delegation, delegatorId);
        typeof(UserDelegation).GetProperty(nameof(UserDelegation.DelegateId))!.SetValue(delegation, delegateId);
        typeof(UserDelegation).GetProperty(nameof(UserDelegation.Status))!.SetValue(delegation, isRevoked ? DelegationStatus.Revoked : DelegationStatus.Active);
        typeof(UserDelegation).GetProperty(nameof(UserDelegation.IsRevoked))!.SetValue(delegation, isRevoked);
        typeof(AuditableEntity).GetProperty(nameof(AuditableEntity.CreatedBy))!.SetValue(delegation, createdBy);

        // Revoke() raises DelegationRevokedEvent into the private _domainEvents list, whose field
        // initializer does NOT run under GetUninitializedObject — seed it so Revoke() does not NPE.
        var domainEventsField = typeof(UserDelegation).GetField("_domainEvents", BindingFlags.NonPublic | BindingFlags.Instance)!;
        domainEventsField.SetValue(delegation, Activator.CreateInstance(domainEventsField.FieldType));

        return delegation;
    }
}
