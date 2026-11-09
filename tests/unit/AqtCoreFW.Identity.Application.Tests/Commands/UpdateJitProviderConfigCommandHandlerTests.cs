using AqtCoreFW.Identity.Application.Commands.UpdateJitProviderConfig;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using NSubstitute;
using FluentAssertions;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for UpdateJitProviderConfigCommandHandler.
/// Verifies: config lookup, update application, persistence.
/// </summary>
public sealed class UpdateJitProviderConfigCommandHandlerTests
{
    private readonly IJitProviderConfigRepository _repository;
    private readonly UpdateJitProviderConfigCommandHandler _sut;

    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private const long ConfigId = 42L;

    public UpdateJitProviderConfigCommandHandlerTests()
    {
        _repository = Substitute.For<IJitProviderConfigRepository>();
        _sut = new UpdateJitProviderConfigCommandHandler(_repository);
    }

    [Fact]
    public async Task Handle_ExistingConfig_UpdatesAndPersists()
    {
        // Arrange
        var existingConfig = JitProviderConfig.Create(
            "SSO", "Old Name", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TenantId, null, 0, ActorId);

        _repository.GetByIdAsync(ConfigId, Arg.Any<CancellationToken>()).Returns(existingConfig);
        _repository.UpdateAsync(Arg.Any<JitProviderConfig>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var cmd = new UpdateJitProviderConfigCommand(
            ConfigId, "Updated Name", true, "Editor", true, null, TenantId, null, 200, ActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _repository.Received(1).UpdateAsync(
            Arg.Is<JitProviderConfig>(c =>
                c.DisplayName == "Updated Name" &&
                c.DefaultRoleName == "Editor" &&
                c.RequireApproval &&
                c.MaxProvisionsPerHour == 200),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ConfigNotFound_ReturnsNotFoundError()
    {
        // Arrange
        _repository.GetByIdAsync(ConfigId, Arg.Any<CancellationToken>()).Returns((JitProviderConfig?)null);

        var cmd = new UpdateJitProviderConfigCommand(
            ConfigId, "Name", true, "Viewer", false, null, TenantId, null, 0, ActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<JitProviderConfig>(), Arg.Any<CancellationToken>());
    }
}
