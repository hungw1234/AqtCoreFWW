using AqtCoreFW.AssetManagement.Application.Commands.UpdateAssetManagement;
using AqtCoreFW.AssetManagement.Domain.Entities;
using AqtCoreFW.AssetManagement.Domain.Repositories;
using FluentAssertions;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.AssetManagement.Application.Tests.Commands;

public sealed class UpdateAssetManagementCommandHandlerTests
{
    [Fact]
    public async Task Handle_AssetNotFound_ReturnsFailureWithoutSaving()
    {
        var repository = Substitute.For<IAssetManagementRepository>();
        repository.GetByIdAsync(42, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<AssetManagementEntity>("missing"));
        var handler = new UpdateAssetManagementCommandHandler(repository);
        var command = new UpdateAssetManagementCommand(
            42, Guid.NewGuid(), "Laptop", null, Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_UpdatesAssetAndSavesChanges()
    {
        var repository = Substitute.For<IAssetManagementRepository>();
        var entity = AssetManagementEntity.Create(
            Guid.NewGuid(), "Laptop", "Old description", Guid.NewGuid());
        repository.GetByIdAsync(42, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(entity));
        var handler = new UpdateAssetManagementCommandHandler(repository);
        var updatedBy = Guid.NewGuid();

        var result = await handler.Handle(
            new UpdateAssetManagementCommand(
                42, Guid.NewGuid(), "Desktop", "New description", updatedBy),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(entity.Id);
        entity.Title.Should().Be("Desktop");
        entity.Description.Should().Be("New description");
        entity.ModifiedBy.Should().Be(updatedBy);
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
