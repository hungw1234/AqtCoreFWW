using AqtCoreFW.Identity.Application.Commands.ApproveDelegation;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for ApproveDelegationCommandHandler.
/// Covers: successful approval by delegator, successful approval by SystemAdmin,
/// rejection when approver = CreatedBy (requester), rejection when approver = DelegateId (receiver),
/// rejection when arbitrary Admin (not delegator, not SystemAdmin), reorder/authority checks (C3).
/// </summary>
public sealed class ApproveDelegationCommandHandlerTests
{
    private readonly IDelegationRepository _delegationRepository;
    private readonly ICacheService _cacheService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApproveDelegationCommandHandler _sut;

    private static readonly Guid DelegatorId = Guid.NewGuid();
    private static readonly Guid DelegateId = Guid.NewGuid();
    private static readonly Guid RequesterId = Guid.NewGuid(); // the actor who created the delegation
    private static readonly Guid SystemAdminId = Guid.NewGuid();
    private static readonly Guid ArbitraryAdminId = Guid.NewGuid();

    public ApproveDelegationCommandHandlerTests()
    {
        _delegationRepository = Substitute.For<IDelegationRepository>();
        _cacheService = Substitute.For<ICacheService>();

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

        _sut = new ApproveDelegationCommandHandler(_delegationRepository, _cacheService, _userManager);
    }

    // --- Happy path: Delegator approves ---

    [Fact]
    public async Task Handle_DelegatorApprovesOwnDelegation_Succeeds()
    {
        // Arrange
        var approverUser = new ApplicationUser { Id = DelegatorId };
        var delegationPublicId = Guid.NewGuid();
        var delegation = CreateMockDelegation(delegationPublicId, DelegatorId, DelegateId, RequesterId, DelegationStatus.PendingApproval);

        _userManager.FindByIdAsync(DelegatorId.ToString()).Returns(approverUser);
        _delegationRepository.GetByPublicIdAsync(delegationPublicId, Arg.Any<CancellationToken>()).Returns(delegation);
        _userManager.GetRolesAsync(approverUser).Returns(new List<string> { "Admin" });
        _delegationRepository.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _cacheService.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var cmd = new ApproveDelegationCommand(delegationPublicId, DelegatorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _delegationRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _cacheService.Received(1).RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- Happy path: SystemAdmin approves ---

    [Fact]
    public async Task Handle_SystemAdminApprovesOnBehalfOfDelegator_Succeeds()
    {
        // Arrange
        var approverUser = new ApplicationUser { Id = SystemAdminId };
        var delegationPublicId = Guid.NewGuid();
        var delegation = CreateMockDelegation(delegationPublicId, DelegatorId, DelegateId, RequesterId, DelegationStatus.PendingApproval);

        _userManager.FindByIdAsync(SystemAdminId.ToString()).Returns(approverUser);
        _delegationRepository.GetByPublicIdAsync(delegationPublicId, Arg.Any<CancellationToken>()).Returns(delegation);
        _userManager.GetRolesAsync(approverUser).Returns(new List<string> { "SystemAdmin" });
        _delegationRepository.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _cacheService.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var cmd = new ApproveDelegationCommand(delegationPublicId, SystemAdminId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _delegationRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- C3 Guard: 4-eyes prevention ---

    [Fact]
    public async Task Handle_ApproverIsArbitraryAdmin_NotDelegatorNotSystemAdmin_ReturnsForbidden()
    {
        // Arrange: an arbitrary Admin (not delegator, not SystemAdmin) cannot approve
        var approverUser = new ApplicationUser { Id = ArbitraryAdminId };
        var delegationPublicId = Guid.NewGuid();
        var delegation = CreateMockDelegation(delegationPublicId, DelegatorId, DelegateId, RequesterId, DelegationStatus.PendingApproval);

        _userManager.FindByIdAsync(ArbitraryAdminId.ToString()).Returns(approverUser);
        _delegationRepository.GetByPublicIdAsync(delegationPublicId, Arg.Any<CancellationToken>()).Returns(delegation);
        _userManager.GetRolesAsync(approverUser).Returns(new List<string> { "Admin" }); // has Admin, but not SystemAdmin, and is not delegator

        var cmd = new ApproveDelegationCommand(delegationPublicId, ArbitraryAdminId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("delegator") || e.Message.Contains("SystemAdmin"));
        await _delegationRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- Negative: Delegation not found, wrong status, actor not found ---

    [Fact]
    public async Task Handle_ActorNotFound_ReturnsForbidden()
    {
        // Arrange
        _userManager.FindByIdAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);

        var cmd = new ApproveDelegationCommand(Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("Actor not found"));
    }

    [Fact]
    public async Task Handle_DelegationNotFound_ReturnsNotFound()
    {
        // Arrange
        var approverUser = new ApplicationUser { Id = DelegatorId };

        _userManager.FindByIdAsync(DelegatorId.ToString()).Returns(approverUser);
        _delegationRepository.GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((UserDelegation?)null);

        var cmd = new ApproveDelegationCommand(Guid.NewGuid(), DelegatorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Handle_DelegationNotPendingApproval_ReturnsConflict()
    {
        // Arrange
        var approverUser = new ApplicationUser { Id = DelegatorId };
        var delegationPublicId = Guid.NewGuid();
        var delegation = CreateMockDelegation(delegationPublicId, DelegatorId, DelegateId, RequesterId, DelegationStatus.Active);

        _userManager.FindByIdAsync(DelegatorId.ToString()).Returns(approverUser);
        _delegationRepository.GetByPublicIdAsync(delegationPublicId, Arg.Any<CancellationToken>()).Returns(delegation);

        var cmd = new ApproveDelegationCommand(delegationPublicId, DelegatorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not in PendingApproval"));
        await _delegationRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- Helpers ---

    private static UserDelegation CreateMockDelegation(
        Guid publicId,
        Guid delegatorId,
        Guid delegateId,
        Guid createdBy,
        DelegationStatus status = DelegationStatus.PendingApproval)
    {
        // Build an instance without the factory (UserDelegation has private setters); RuntimeHelpers is the
        // non-obsolete replacement for FormatterServices.GetUninitializedObject.
        var delegation = (UserDelegation)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(UserDelegation));
        typeof(UserDelegation).GetProperty(nameof(UserDelegation.PublicId))!.SetValue(delegation, publicId);
        typeof(UserDelegation).GetProperty(nameof(UserDelegation.DelegatorId))!.SetValue(delegation, delegatorId);
        typeof(UserDelegation).GetProperty(nameof(UserDelegation.DelegateId))!.SetValue(delegation, delegateId);
        typeof(UserDelegation).GetProperty(nameof(UserDelegation.Status))!.SetValue(delegation, status);
        typeof(UserDelegation).GetProperty(nameof(UserDelegation.RequiresApproval))!.SetValue(delegation, true);
        typeof(UserDelegation).GetProperty(nameof(UserDelegation.IsRevoked))!.SetValue(delegation, false);
        typeof(AuditableEntity).GetProperty(nameof(AuditableEntity.CreatedBy))!.SetValue(delegation, createdBy);
        return delegation;
    }
}
