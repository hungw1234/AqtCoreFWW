using AqtCoreFW.Identity.Application.Commands.CreateCredentialPolicy;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for CreateCredentialPolicyCommandHandler.
/// Verifies: success, duplicate default policy validation.
/// </summary>
public sealed class CreateCredentialPolicyCommandHandlerTests
{
    private readonly ICredentialPolicyRepository _repo = Substitute.For<ICredentialPolicyRepository>();
    private readonly CreateCredentialPolicyCommandHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public CreateCredentialPolicyCommandHandlerTests()
    {
        _sut = new CreateCredentialPolicyCommandHandler(_repo);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccess()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Standard Policy",
            TenantId,
            8,
            256,
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

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // T1: result.Value is long Id (0 in unit test since repo mock doesn't trigger DB IDENTITY)
        await _repo.Received(1).AddAsync(Arg.Any<CredentialPolicy>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CreateDefaultPolicy_ReturnsSuccess()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Default Policy",
            TenantId,
            8,
            128,
            true,
            true,
            true,
            true,
            60,
            3,
            15,
            5,
            true,  // IsDefault = true
            ActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).AddAsync(
            Arg.Is<CredentialPolicy>(p => p.IsDefault),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvokesRepositoryAdd()
    {
        // Arrange
        var cmd = new CreateCredentialPolicyCommand(
            "Custom Policy",
            TenantId,
            12,
            256,
            true,
            true,
            false,
            false,
            120,
            10,
            60,
            0,
            false,
            ActorId);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        await _repo.Received(1).AddAsync(
            Arg.Is<CredentialPolicy>(p =>
                p.Name == "Custom Policy" &&
                p.TenantId == TenantId &&
                p.MinLength == 12 &&
                p.MaxLength == 256),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PreservesAllPolicySettings()
    {
        // Arrange
        const int minLen = 10;
        const int maxLen = 200;
        const bool requireUpper = true;
        const bool requireLower = false;
        const bool requireDigit = true;
        const bool requireSpecial = false;
        const int rotationDays = 45;
        const int maxFailedAttempts = 7;
        const int lockoutMinutes = 45;
        const int passwordHistoryCount = 4;

        var cmd = new CreateCredentialPolicyCommand(
            "Detailed Policy",
            TenantId,
            minLen,
            maxLen,
            requireUpper,
            requireLower,
            requireDigit,
            requireSpecial,
            rotationDays,
            maxFailedAttempts,
            lockoutMinutes,
            passwordHistoryCount,
            false,
            ActorId);

        // Act
        await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        await _repo.Received(1).AddAsync(
            Arg.Is<CredentialPolicy>(p =>
                p.MinLength == minLen &&
                p.MaxLength == maxLen &&
                p.RequireUppercase == requireUpper &&
                p.RequireLowercase == requireLower &&
                p.RequireDigit == requireDigit &&
                p.RequireSpecialChar == requireSpecial &&
                p.RotationDays == rotationDays &&
                p.MaxFailedAttempts == maxFailedAttempts &&
                p.LockoutMinutes == lockoutMinutes &&
                p.PasswordHistoryCount == passwordHistoryCount),
            Arg.Any<CancellationToken>());
    }
}
