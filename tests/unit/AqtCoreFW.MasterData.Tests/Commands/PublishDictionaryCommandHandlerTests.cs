using AqtCoreFW.MasterData.Application.Commands.PublishDictionary;
using AqtCoreFW.MasterData.Infrastructure.CommandHandlers;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.MasterData.Tests.Commands;

/// <summary>
/// Tests for PublishDictionaryCommandHandler.
/// Verifies handler structure and dependencies.
/// </summary>
public sealed class PublishDictionaryCommandHandlerTests
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

        var handler = new PublishDictionaryCommandHandler(mockDbContext);

        // Assert
        handler.Should().NotBeNull();
    }

    [Fact]
    public void ValidCommand_CanBeCreated()
    {
        // Arrange & Act
        var cmd = new PublishDictionaryCommand(DictionaryId, "Publishing notes", ActorId);

        // Assert
        cmd.Id.Should().Be(DictionaryId);
        cmd.ActorId.Should().Be(ActorId);
        cmd.Notes.Should().Be("Publishing notes");
    }
}
