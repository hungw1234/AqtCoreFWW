using AqtCoreFW.Identity.Application.Commands.DeleteCredentialPolicy;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for DeleteCredentialPolicyCommandHandler.
/// Verifies: success, not found error.
/// </summary>
public sealed class DeleteCredentialPolicyCommandHandlerTests
{
    private readonly ICredentialPolicyRepository _repo = Substitute.For<ICredentialPolicyRepository>();
    private readonly DeleteCredentialPolicyCommandHandler _sut;

    private const long PolicyId = 42L;
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public DeleteCredentialPolicyCommandHandlerTests()
    {
        _sut = new DeleteCredentialPolicyCommandHandler(_repo);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccess()
    {
        // Arrange
        var cmd = new DeleteCredentialPolicyCommand(PolicyId, ActorId);

        var policy = CredentialPolicy.Create(
            "Policy to Delete",
            TenantId,
            8,
            128,
            true,
            true,
            true,
            true,
            90,
            5,
            30,
            3,
            false,
            ActorId);

        _repo.GetByIdAsync(PolicyId, Arg.Any<CancellationToken>()).Returns(policy);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).DeleteAsync(Arg.Any<CredentialPolicy>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsFailResult()
    {
        // Arrange
        var cmd = new DeleteCredentialPolicyCommand(PolicyId, ActorId);

        _repo.GetByIdAsync(PolicyId, Arg.Any<CancellationToken>()).Returns((CredentialPolicy?)null);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Handle_InvokesRepositoryDelete()
    {
        // Arrange
        var cmd = new DeleteCredentialPolicyCommand(PolicyId, ActorId);

        var policy = CredentialPolicy.Create(
            "Standard Policy",
            TenantId,
            10,
            200,
            true,
            true,
            true,
            false,
            60,
            3,
            20,
            2,
            false,
            ActorId);

        _repo.GetByIdAsync(PolicyId, Arg.Any<CancellationToken>()).Returns(policy);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        await _repo.Received(1).DeleteAsync(policy, Arg.Any<CancellationToken>());
    }
}
