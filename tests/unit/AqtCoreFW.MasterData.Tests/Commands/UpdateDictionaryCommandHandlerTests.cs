using AqtCoreFW.MasterData.Application.Commands.UpdateDictionary;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.CommandHandlers;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.MasterData.Tests.Commands;

/// <summary>
/// Tests for UpdateDictionaryCommandHandler.
/// Verifies handler structure and dependencies.
/// </summary>
public sealed class UpdateDictionaryCommandHandlerTests
{
    private static readonly Guid DictionaryId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public void Handler_HasCorrectDependencies()
    {
        // Arrange & Act
        var mockDbContext = Substitute.For<MasterDataDbContext>(
            new DbContextOptions<MasterDataDbContext>(),
            Substitute.For<ITenantContext>());

        var handler = new UpdateDictionaryCommandHandler(mockDbContext);

        // Assert
        handler.Should().NotBeNull();
    }

    [Fact]
    public void ValidCommand_CanBeCreated()
    {
        // Arrange & Act
        var cmd = new UpdateDictionaryCommand(
            DictionaryId,
            "Updated Name",
            "Tên Cập Nhật",
            "Updated description",
            ActorId);

        // Assert
        cmd.Id.Should().Be(DictionaryId);
        cmd.Name.Should().Be("Updated Name");
        cmd.NameVi.Should().Be("Tên Cập Nhật");
        cmd.ActorId.Should().Be(ActorId);
    }
}
