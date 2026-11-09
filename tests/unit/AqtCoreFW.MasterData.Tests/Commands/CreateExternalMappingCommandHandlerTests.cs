using AqtCoreFW.MasterData.Application.Commands.CreateExternalMapping;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.CommandHandlers;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.MasterData.Tests.Commands;

/// <summary>
/// Tests for CreateExternalMappingCommandHandler.
/// Verifies handler structure and command validation.
/// </summary>
public sealed class CreateExternalMappingCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public void Handler_HasCorrectDependencies()
    {
        // Arrange & Act
        var mockDbContext = Substitute.For<MasterDataDbContext>(
            new DbContextOptions<MasterDataDbContext>(),
            Substitute.For<ITenantContext>());

        var handler = new CreateExternalMappingCommandHandler(mockDbContext, Substitute.For<IGuidGenerator>());

        // Assert
        handler.Should().NotBeNull();
    }

    [Fact]
    public void ValidCommand_CanBeCreated()
    {
        // Arrange & Act
        var cmd = new CreateExternalMappingCommand(
            "INTERNAL_CODE",
            "EXTERNAL_SYSTEM",
            "EXT_CODE",
            MappingDirection.Both,
            null,
            DateTime.UtcNow,
            null,
            TenantId,
            ActorId);

        // Assert
        cmd.InternalCode.Should().Be("INTERNAL_CODE");
        cmd.ExternalSystem.Should().Be("EXTERNAL_SYSTEM");
        cmd.ExternalCode.Should().Be("EXT_CODE");
        cmd.Direction.Should().Be(MappingDirection.Both);
        cmd.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public void CommandWithInDirection_CanBeCreated()
    {
        // Arrange & Act
        var cmd = new CreateExternalMappingCommand(
            "CODE1",
            "SYSTEM1",
            "CODE2",
            MappingDirection.In,
            null,
            DateTime.UtcNow,
            null,
            TenantId,
            ActorId);

        // Assert
        cmd.Direction.Should().Be(MappingDirection.In);
    }

    [Fact]
    public void CommandWithOutDirection_CanBeCreated()
    {
        // Arrange & Act
        var cmd = new CreateExternalMappingCommand(
            "CODE1",
            "SYSTEM1",
            "CODE2",
            MappingDirection.Out,
            null,
            DateTime.UtcNow,
            null,
            TenantId,
            ActorId);

        // Assert
        cmd.Direction.Should().Be(MappingDirection.Out);
    }
}
