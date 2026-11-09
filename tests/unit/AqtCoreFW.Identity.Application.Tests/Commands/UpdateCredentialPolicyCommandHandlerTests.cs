using AqtCoreFW.Identity.Application.Commands.UpdateCredentialPolicy;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for UpdateCredentialPolicyCommandHandler.
/// Verifies: success, not found error.
/// </summary>
public sealed class UpdateCredentialPolicyCommandHandlerTests
{
    private readonly ICredentialPolicyRepository _repo = Substitute.For<ICredentialPolicyRepository>();
    private readonly UpdateCredentialPolicyCommandHandler _sut;

    private const long PolicyId = 42L;
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public UpdateCredentialPolicyCommandHandlerTests()
    {
        _sut = new UpdateCredentialPolicyCommandHandler(_repo);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccess()
    {
        // Arrange
        var cmd = new UpdateCredentialPolicyCommand(
            PolicyId,
            "Updated Policy",
            10,
            200,
            true,
            true,
            true,
            false,
            60,
            5,
            20,
            2,
            false,
            ActorId);

        var policy = CredentialPolicy.Create(
            "Old Policy",
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
        await _repo.Received(1).UpdateAsync(Arg.Any<CredentialPolicy>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsFailResult()
    {
        // Arrange
        var cmd = new UpdateCredentialPolicyCommand(
            PolicyId,
            "Updated Policy",
            10,
            200,
            true,
            true,
            true,
            false,
            60,
            5,
            20,
            2,
            false,
            ActorId);

        _repo.GetByIdAsync(PolicyId, Arg.Any<CancellationToken>()).Returns((CredentialPolicy?)null);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Handle_UpdatesAllFields()
    {
        // Arrange
        const string newName = "Enhanced Policy";
        const int minLen = 12;
        const int maxLen = 256;

        var cmd = new UpdateCredentialPolicyCommand(
            PolicyId,
            newName,
            minLen,
            maxLen,
            false,
            true,
            true,
            true,
            45,
            3,
            15,
            5,
            true,
            ActorId);

        var policy = CredentialPolicy.Create(
            "Original Policy",
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
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        await _repo.Received(1).UpdateAsync(
            Arg.Is<CredentialPolicy>(p =>
                p.Name == newName &&
                p.MinLength == minLen &&
                p.MaxLength == maxLen &&
                p.IsDefault == true),
            Arg.Any<CancellationToken>());
    }
}
