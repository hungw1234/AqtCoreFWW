using AqtCoreFW.AssetManagement.Application.Commands.DeleteAssetManagement;
using AqtCoreFW.AssetManagement.Domain.Entities;
using AqtCoreFW.AssetManagement.Domain.Repositories;
using FluentAssertions;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.AssetManagement.Application.Tests.Commands;

public sealed class DeleteAssetManagementCommandHandlerTests
{
    [Fact]
    public async Task Handle_AssetNotFound_ReturnsFailureWithoutSaving()
    {
        var repository = Substitute.For<IAssetManagementRepository>();
        repository.GetByIdAsync(42, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<AssetManagementEntity>("missing"));
        var handler = new DeleteAssetManagementCommandHandler(repository);

        var result = await handler.Handle(
            new DeleteAssetManagementCommand(42, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_SoftDeletesAssetAndSavesChanges()
    {
        var repository = Substitute.For<IAssetManagementRepository>();
        var entity = AssetManagementEntity.Create(
            Guid.NewGuid(), "Laptop", null, Guid.NewGuid());
        repository.GetByIdAsync(42, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(entity));
        var handler = new DeleteAssetManagementCommandHandler(repository);

        var result = await handler.Handle(
            new DeleteAssetManagementCommand(42, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        entity.IsDeleted.Should().BeTrue();
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
