using AqtCoreFW.Identity.Application.Commands.CreateExternalIdentity;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for CreateExternalIdentityCommandHandler.
/// Verifies: success, duplicate provider check, user existence validation, and tenant isolation
/// (a non-SystemAdmin caller may only link identities to users in their own tenant).
/// </summary>
public sealed class CreateExternalIdentityCommandHandlerTests
{
    private readonly IExternalIdentityRepository _repo = Substitute.For<IExternalIdentityRepository>();
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CreateExternalIdentityCommandHandler _sut;

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    public CreateExternalIdentityCommandHandlerTests()
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

        _sut = new CreateExternalIdentityCommandHandler(_repo, _userManager);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccess()
    {
        // Arrange — caller and target user share TenantA.
        var cmd = NewCommand(callerTenant: TenantA, isPrivileged: false);

        var user = new ApplicationUser { Id = UserId, Email = "john@example.com", TenantId = TenantA };
        _userManager.FindByIdAsync(UserId.ToString()).Returns(user);
        _repo.GetByUserAndProviderAsync(UserId, ExternalIdentityProvider.OAuth, Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).AddAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsFailResult()
    {
        // Arrange
        var cmd = NewCommand(callerTenant: TenantA, isPrivileged: false);
        _userManager.FindByIdAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Handle_CrossTenantUser_NonPrivileged_ReturnsNotFound()
    {
        // Arrange — caller in TenantA tries to link an identity to a user in TenantB.
        var cmd = NewCommand(callerTenant: TenantA, isPrivileged: false);

        var userInB = new ApplicationUser { Id = UserId, Email = "victim@example.com", TenantId = TenantB };
        _userManager.FindByIdAsync(UserId.ToString()).Returns(userInB);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert — NotFound (never leak that the user exists in another tenant); no link created.
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Should().BeOfType<NotFoundError>();
        await _repo.DidNotReceive().AddAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CrossTenantUser_Privileged_Succeeds()
    {
        // Arrange — SystemAdmin may link across tenants.
        var cmd = NewCommand(callerTenant: TenantA, isPrivileged: true);

        var userInB = new ApplicationUser { Id = UserId, Email = "user@example.com", TenantId = TenantB };
        _userManager.FindByIdAsync(UserId.ToString()).Returns(userInB);
        _repo.GetByUserAndProviderAsync(UserId, ExternalIdentityProvider.OAuth, Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).AddAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateProvider_ReturnsConflictError()
    {
        // Arrange
        var cmd = NewCommand(callerTenant: TenantA, isPrivileged: false);

        var user = new ApplicationUser { Id = UserId, Email = "john@example.com", TenantId = TenantA };
        _userManager.FindByIdAsync(UserId.ToString()).Returns(user);

        var existingIdentity = ExternalIdentity.Create(
            UserId, ExternalIdentityProvider.OAuth, "google-user-456",
            "Existing User", "existing@example.com", ActorId);
        _repo.GetByUserAndProviderAsync(UserId, ExternalIdentityProvider.OAuth, Arg.Any<CancellationToken>())
            .Returns(existingIdentity);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("already has a linked"));
    }

    [Fact]
    public async Task Handle_ValidCommand_InvokesRepositoryAdd()
    {
        // Arrange
        var cmd = new CreateExternalIdentityCommand(
            UserId, ExternalIdentityProvider.LDAP, "octocat", "The Octocat", null, ActorId,
            CallerTenantId: TenantA, IsPrivileged: false);

        var user = new ApplicationUser { Id = UserId, Email = "octocat@github.com", TenantId = TenantA };
        _userManager.FindByIdAsync(UserId.ToString()).Returns(user);
        _repo.GetByUserAndProviderAsync(UserId, ExternalIdentityProvider.LDAP, Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        await _repo.Received(1).AddAsync(
            Arg.Is<ExternalIdentity>(e =>
                e.UserId == UserId &&
                e.Provider == ExternalIdentityProvider.LDAP &&
                e.ExternalId == "octocat"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullCallerTenant_NullOwnerTenant_NonPrivileged_ReturnsNotFound()
    {
        // Edge: a tenantless non-privileged caller must NOT match a tenantless user. The
        // CallerTenantId.HasValue gate blocks the C# null==null trap (SQL blocks it implicitly).
        var cmd = new CreateExternalIdentityCommand(
            UserId, ExternalIdentityProvider.OAuth, "google-user-123", "John Doe", "john@example.com",
            ActorId, CallerTenantId: null, IsPrivileged: false);
        _userManager.FindByIdAsync(UserId.ToString())
            .Returns(new ApplicationUser { Id = UserId, TenantId = null });

        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Should().BeOfType<NotFoundError>();
        await _repo.DidNotReceive().AddAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>());
    }

    private static CreateExternalIdentityCommand NewCommand(Guid callerTenant, bool isPrivileged) =>
        new(UserId, ExternalIdentityProvider.OAuth, "google-user-123", "John Doe", "john@example.com",
            ActorId, CallerTenantId: callerTenant, IsPrivileged: isPrivileged);
}
