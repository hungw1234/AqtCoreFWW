using AqtCoreFW.Identity.Application.Commands.UpdateTenant;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Unit tests for UpdateTenantCommandHandler.
/// Verifies: success path, not found error, Code immutability (Code not in command).
/// </summary>
public sealed class UpdateTenantCommandHandlerTests
{
    private readonly ITenantRepository _tenantRepository = Substitute.For<ITenantRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly UpdateTenantCommandHandler _sut;

    private static readonly Guid ActorId = Guid.NewGuid();

    public UpdateTenantCommandHandlerTests()
    {
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _sut = new UpdateTenantCommandHandler(_tenantRepository, _publisher);
    }

    [Fact]
    public async Task Handle_ValidCommand_UpdatesNameAndEmail()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var existingTenant = Tenant.Create(_guidGen, "Old Name", "CODE123", "old@example.com", ActorId);
        // Use reflection to set Id for testing
        typeof(Tenant).GetProperty("PublicId")!.SetValue(existingTenant, tenantId);

        _tenantRepository.GetByPublicIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(existingTenant);

        var cmd = new UpdateTenantCommand(
            tenantId,
            "New Name",
            "new@example.com",
            ActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        existingTenant.Name.Should().Be("New Name");
        existingTenant.ContactEmail.Should().Be("new@example.com");
        // Code should remain unchanged
        existingTenant.Code.Should().Be("CODE123");

        await _tenantRepository.Received(1).UpdateAsync(existingTenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TenantNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantRepository.GetByPublicIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns((Tenant?)null);

        var cmd = new UpdateTenantCommand(
            tenantId,
            "Any Name",
            null,
            ActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("không tồn tại"));

        await _tenantRepository.DidNotReceive().UpdateAsync(
            Arg.Any<Tenant>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CodeImmutable_NotInCommand()
    {
        // The UpdateTenantCommand does not include Code property
        // This test verifies the command design ensures Code immutability

        var cmd = new UpdateTenantCommand(
            Guid.NewGuid(),
            "Name",
            "email@example.com",
            ActorId);

        // Assert: Command does not have Code property (compile-time guarantee)
        var properties = typeof(UpdateTenantCommand).GetProperties();
        properties.Should().NotContain(p => p.Name == "Code");
    }

    [Fact]
    public async Task Handle_NullContactEmail_ClearsExistingEmail()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var existingTenant = Tenant.Create(_guidGen, "Name", "CODE", "old@example.com", ActorId);
        typeof(Tenant).GetProperty("PublicId")!.SetValue(existingTenant, tenantId);

        _tenantRepository.GetByPublicIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(existingTenant);

        var cmd = new UpdateTenantCommand(
            tenantId,
            "Name",
            null, // Clear email
            ActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        existingTenant.ContactEmail.Should().BeNull();
    }

    [Fact]
    public async Task Handle_StatusNotChanged_ByUpdateCommand()
    {
        // Arrange: UpdateTenantCommand does NOT change status
        // Status changes via ChangeTenantStatusCommand
        var tenantId = Guid.NewGuid();
        var existingTenant = Tenant.Create(_guidGen, "Name", "CODE", null, ActorId);
        typeof(Tenant).GetProperty("PublicId")!.SetValue(existingTenant, tenantId);
        existingTenant.Status.Should().Be(TenantStatus.Active);

        _tenantRepository.GetByPublicIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(existingTenant);

        var cmd = new UpdateTenantCommand(
            tenantId,
            "Updated Name",
            null,
            ActorId);

        // Act
        var result = await _sut.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        existingTenant.Status.Should().Be(TenantStatus.Active); // Unchanged
    }
}
