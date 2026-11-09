using AqtCoreFW.Identity.Application.Commands.UpdateUser;
using FluentValidation.TestHelper;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Validator tests for UpdateUserCommand — focus on the optional CCCD (SigningNationalId) rule.
/// </summary>
public sealed class UpdateUserCommandValidatorTests
{
    private readonly UpdateUserCommandValidator _sut = new();

    private static UpdateUserCommand Valid() => new(
        UserId: Guid.NewGuid(),
        FullName: "Nguyễn Văn A",
        DepartmentCode: "CB001",
        OrgUnitId: null,
        ActorId: Guid.NewGuid(),
        SigningNationalId: null);

    [Fact]
    public void NullSigningNationalId_Passes()
        => _sut.TestValidate(Valid())
            .ShouldNotHaveValidationErrorFor(x => x.SigningNationalId);

    [Fact]
    public void Valid12DigitCccd_Passes()
        => _sut.TestValidate(Valid() with { SigningNationalId = "017091004878" })
            .ShouldNotHaveValidationErrorFor(x => x.SigningNationalId);

    [Theory]
    [InlineData("12345")]          // too short
    [InlineData("0170910048781")]  // 13 digits
    [InlineData("01709100487a")]   // non-digit char
    [InlineData("017 91004878")]   // embedded space
    public void InvalidCccd_Fails(string cccd)
        => _sut.TestValidate(Valid() with { SigningNationalId = cccd })
            .ShouldHaveValidationErrorFor(x => x.SigningNationalId);

    [Fact]
    public void EmptyFullName_Fails()
        => _sut.TestValidate(Valid() with { FullName = "" })
            .ShouldHaveValidationErrorFor(x => x.FullName);
}
