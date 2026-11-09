using AqtCoreFW.Identity.Application.Commands.UpdateExternalIdentity;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for UpdateExternalIdentityCommandHandler.
/// Verifies: success, not found, and tenant isolation (a non-SystemAdmin caller may only mutate
/// links whose owning user is in their tenant; cross-tenant returns NotFound, never leaking existence).
/// </summary>
public sealed class UpdateExternalIdentityCommandHandlerTests
{
    private readonly IExternalIdentityRepository _repo = Substitute.For<IExternalIdentityRepository>();
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UpdateExternalIdentityCommandHandler _sut;

    private const long ExternalIdentityId = 42L;
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    public UpdateExternalIdentityCommandHandlerTests()
    {
        var store = Substitute.For<IUserStore<ApplicationUser>>();
        _userManager = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<Microsoft.Extensions.Options.IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<UserManager<ApplicationUser>>>());

        _sut = new UpdateExternalIdentityCommandHandler(_repo, _userManager);
    }

    private static ExternalIdentity SeedEntity() => ExternalIdentity.Create(
        UserId, ExternalIdentityProvider.OAuth, "google-user-123", "Old Name", "old@example.com", ActorId);

    private void OwnerInTenant(Guid tenantId) =>
        _userManager.FindByIdAsync(UserId.ToString())
            .Returns(new ApplicationUser { Id = UserId, TenantId = tenantId });

    private static UpdateExternalIdentityCommand NewCommand(Guid? callerTenant, bool isPrivileged) =>
        new(ExternalIdentityId, "Updated Name", "updated@example.com", null, ActorId,
            CallerTenantId: callerTenant, IsPrivileged: isPrivileged);

    [Fact]
    public async Task Handle_ValidCommand_SameTenant_ReturnsSuccess()
    {
        // Arrange
        _repo.GetByIdAsync(ExternalIdentityId, Arg.Any<CancellationToken>()).Returns(SeedEntity());
        OwnerInTenant(TenantA);

        // Act
        var result = await _sut.Handle(NewCommand(TenantA, isPrivileged: false), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).UpdateAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsFailResult()
    {
        // Arrange
        _repo.GetByIdAsync(ExternalIdentityId, Arg.Any<CancellationToken>()).Returns((ExternalIdentity?)null);

        // Act
        var result = await _sut.Handle(NewCommand(TenantA, isPrivileged: false), CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Handle_CrossTenant_NonPrivileged_ReturnsNotFound()
    {
        // Arrange — entity owned by a user in TenantB; caller is in TenantA.
        _repo.GetByIdAsync(ExternalIdentityId, Arg.Any<CancellationToken>()).Returns(SeedEntity());
        OwnerInTenant(TenantB);

        // Act
        var result = await _sut.Handle(NewCommand(TenantA, isPrivileged: false), CancellationToken.None);

        // Assert — NotFound, and no mutation persisted.
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Should().BeOfType<NotFoundError>();
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CrossTenant_Privileged_Succeeds()
    {
        // Arrange — SystemAdmin bypasses the tenant bind (no owner lookup needed).
        _repo.GetByIdAsync(ExternalIdentityId, Arg.Any<CancellationToken>()).Returns(SeedEntity());

        // Act
        var result = await _sut.Handle(NewCommand(TenantA, isPrivileged: true), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).UpdateAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>());
        await _userManager.DidNotReceive().FindByIdAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_NullCallerTenant_NullOwnerTenant_NonPrivileged_ReturnsNotFound()
    {
        // Edge: tenantless non-privileged caller must NOT match a tenantless owner (.HasValue gate).
        _repo.GetByIdAsync(ExternalIdentityId, Arg.Any<CancellationToken>()).Returns(SeedEntity());
        _userManager.FindByIdAsync(UserId.ToString())
            .Returns(new ApplicationUser { Id = UserId, TenantId = null });

        var result = await _sut.Handle(NewCommand(callerTenant: null, isPrivileged: false), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Should().BeOfType<NotFoundError>();
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UpdatesDisplayName()
    {
        // Arrange
        const string newDisplayName = "New Display Name";
        var cmd = new UpdateExternalIdentityCommand(
            ExternalIdentityId, newDisplayName, "test@example.com", null, ActorId,
            CallerTenantId: TenantA, IsPrivileged: false);
        _repo.GetByIdAsync(ExternalIdentityId, Arg.Any<CancellationToken>()).Returns(SeedEntity());
        OwnerInTenant(TenantA);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        await _repo.Received(1).UpdateAsync(
            Arg.Is<ExternalIdentity>(e => e.DisplayName == newDisplayName),
            Arg.Any<CancellationToken>());
    }
}
