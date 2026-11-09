using AqtCoreFW.Identity.Application.Commands.ResetPassword;
using AqtCoreFW.Identity.Application.Commands.RevokeToken;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Events;
using AqtCoreFW.SharedKernel.Domain.Events;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for ResetPasswordCommandHandler.
/// Verifies: token generation, event publishing, token revocation, email validation.
/// </summary>
public sealed class ResetPasswordCommandHandlerTests
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISender _sender;
    private readonly IDomainEventPublisher _events;
    private readonly ResetPasswordCommandHandler _sut;

    private static readonly Guid UserId = Guid.NewGuid();

    public ResetPasswordCommandHandlerTests()
    {
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

        _sender = Substitute.For<ISender>();
        _events = Substitute.For<IDomainEventPublisher>();
        var gen = Substitute.For<AqtCoreFW.SharedKernel.Abstractions.IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _sut = new ResetPasswordCommandHandler(_userManager, _sender, _events, gen);
    }

    [Fact]
    public async Task Handle_ExistingUser_ReturnsSuccess()
    {
        var user = new ApplicationUser { Id = UserId, Email = "test@test.vn", FullName = "Test" };
        _userManager.FindByIdAsync(UserId.ToString()).Returns(user);
        _userManager.GeneratePasswordResetTokenAsync(user).Returns("reset-token-abc");

        var result = await _sut.Handle(new ResetPasswordCommand(UserId, UserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ExistingUser_PublishesPasswordResetEvent()
    {
        var user = new ApplicationUser { Id = UserId, Email = "test@test.vn", FullName = "Test" };
        _userManager.FindByIdAsync(UserId.ToString()).Returns(user);
        _userManager.GeneratePasswordResetTokenAsync(user).Returns("tok");

        await _sut.Handle(new ResetPasswordCommand(UserId, UserId), CancellationToken.None);

        await _events.Received(1).PublishAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(e =>
                e.OfType<PasswordResetRequestedEvent>().Any(p => p.UserId == UserId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingUser_RevokesAllTokens()
    {
        var user = new ApplicationUser { Id = UserId, Email = "test@test.vn", FullName = "Test" };
        _userManager.FindByIdAsync(UserId.ToString()).Returns(user);
        _userManager.GeneratePasswordResetTokenAsync(user).Returns("tok");

        await _sut.Handle(new ResetPasswordCommand(UserId, UserId), CancellationToken.None);

        await _sender.Received(1).Send(
            Arg.Is<RevokeTokenCommand>(c => c.UserId == UserId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsFailResult()
    {
        _userManager.FindByIdAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);

        var result = await _sut.Handle(new ResetPasswordCommand(UserId, UserId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UserWithNoEmail_ReturnsFailResult()
    {
        var user = new ApplicationUser { Id = UserId, Email = null, FullName = "Test" };
        _userManager.FindByIdAsync(UserId.ToString()).Returns(user);

        var result = await _sut.Handle(new ResetPasswordCommand(UserId, UserId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("email"));
    }
}
