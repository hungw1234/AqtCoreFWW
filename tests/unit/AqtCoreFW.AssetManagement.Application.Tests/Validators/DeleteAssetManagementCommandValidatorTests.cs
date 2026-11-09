using AqtCoreFW.AssetManagement.Application.Commands.DeleteAssetManagement;
using FluentAssertions;

namespace AqtCoreFW.AssetManagement.Application.Tests.Validators;

public sealed class DeleteAssetManagementCommandValidatorTests
{
    private readonly DeleteAssetManagementCommandValidator _sut = new();

    [Fact]
    public void InvalidId_FailsOnIdProperty()
    {
        var command = new DeleteAssetManagementCommand(0, Guid.NewGuid());

        _sut.Validate(command).Errors.Should().Contain(error =>
            error.PropertyName == nameof(DeleteAssetManagementCommand.Id));
    }

    [Fact]
    public void EmptyTenantId_FailsOnTenantIdProperty()
    {
        var command = new DeleteAssetManagementCommand(1, Guid.Empty);

        _sut.Validate(command).Errors.Should().Contain(error =>
            error.PropertyName == nameof(DeleteAssetManagementCommand.TenantId));
    }
}
