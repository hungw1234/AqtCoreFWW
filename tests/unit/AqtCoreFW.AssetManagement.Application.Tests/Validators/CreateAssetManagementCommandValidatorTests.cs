using AqtCoreFW.AssetManagement.Application.Commands.CreateAssetManagement;
using FluentAssertions;

namespace AqtCoreFW.AssetManagement.Application.Tests.Validators;

public sealed class CreateAssetManagementCommandValidatorTests
{
    private readonly CreateAssetManagementCommandValidator _sut = new();

    private static CreateAssetManagementCommand Valid() =>
        new(Guid.NewGuid(), "Laptop", "Office device", Guid.NewGuid());

    [Fact]
    public void Valid_Passes() => _sut.Validate(Valid()).IsValid.Should().BeTrue();

    public static TheoryData<CreateAssetManagementCommand, string> Invalid() => new()
    {
        { Valid() with { Title = "" }, nameof(CreateAssetManagementCommand.Title) },
        { Valid() with { Title = new string('x', 201) }, nameof(CreateAssetManagementCommand.Title) },
        { Valid() with { Description = new string('x', 2001) }, nameof(CreateAssetManagementCommand.Description) },
        { Valid() with { TenantId = Guid.Empty }, nameof(CreateAssetManagementCommand.TenantId) },
        { Valid() with { CreatedBy = Guid.Empty }, nameof(CreateAssetManagementCommand.CreatedBy) },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Invalid_FailsOnExpectedProperty(
        CreateAssetManagementCommand command,
        string property) =>
        _sut.Validate(command).Errors.Should().Contain(error => error.PropertyName == property);
}
