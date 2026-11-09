using AqtCoreFW.AssetManagement.Application.Commands.CreateAssetManagement;
using AqtCoreFW.AssetManagement.Domain.Entities;
using AqtCoreFW.AssetManagement.Domain.Repositories;
using AqtCoreFW.SharedKernel.Contracts;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.AssetManagement.Application.Tests.Commands;

public sealed class CreateAssetManagementCommandHandlerTests
{
    [Fact]
    public async Task CreateHandler_PersistsEntityAndReturnsSuccess()
    {
        var repository = Substitute.For<IAssetManagementRepository>();
        var featureFlags = Substitute.For<IFeatureFlagService>();
        featureFlags.IsEnabled("feature:assetmanagement.create", Arg.Any<string>())
            .Returns(true);
        repository.AddAsync(Arg.Any<AssetManagementEntity>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var handler = new CreateAssetManagementCommandHandler(repository, featureFlags);
        var command = new CreateAssetManagementCommand(
            Guid.NewGuid(), "Laptop", "Office device", Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        featureFlags.Received(1).IsEnabled(
            "feature:assetmanagement.create", command.TenantId.ToString());
        await repository.Received(1).AddAsync(
            Arg.Is<AssetManagementEntity>(asset =>
                asset.Title == command.Title &&
                asset.Description == command.Description &&
                asset.TenantId == command.TenantId &&
                asset.CreatedBy == command.CreatedBy),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateHandler_ReturnsForbiddenAndDoesNotPersistWhenFeatureIsDisabled()
    {
        var repository = Substitute.For<IAssetManagementRepository>();
        var featureFlags = Substitute.For<IFeatureFlagService>();
        featureFlags.IsEnabled("feature:assetmanagement.create", Arg.Any<string>())
            .Returns(false);
        var handler = new CreateAssetManagementCommandHandler(repository, featureFlags);
        var command = new CreateAssetManagementCommand(
            Guid.NewGuid(), "Laptop", null, Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Should().BeOfType<ForbiddenError>()
            .Which.Message.Should().Be("Asset management creation is disabled.");
        await repository.DidNotReceive().AddAsync(
            Arg.Any<AssetManagementEntity>(), Arg.Any<CancellationToken>());
    }
}
