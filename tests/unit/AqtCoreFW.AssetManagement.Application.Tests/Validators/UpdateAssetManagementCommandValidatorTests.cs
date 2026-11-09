using AqtCoreFW.AssetManagement.Application.Commands.UpdateAssetManagement;
using FluentAssertions;

namespace AqtCoreFW.AssetManagement.Application.Tests.Validators;

public sealed class UpdateAssetManagementCommandValidatorTests
{
    private readonly UpdateAssetManagementCommandValidator _sut = new();

    [Fact]
    public void InvalidId_FailsOnIdProperty()
    {
        var command = new UpdateAssetManagementCommand(
            0, Guid.NewGuid(), "Laptop", null, Guid.NewGuid());

        _sut.Validate(command).Errors.Should().Contain(error =>
            error.PropertyName == nameof(UpdateAssetManagementCommand.Id));
    }

    [Fact]
    public void EmptyTitle_FailsOnTitleProperty()
    {
        var command = new UpdateAssetManagementCommand(
            1, Guid.NewGuid(), "", null, Guid.NewGuid());

        _sut.Validate(command).Errors.Should().Contain(error =>
            error.PropertyName == nameof(UpdateAssetManagementCommand.Title));
    }
}
