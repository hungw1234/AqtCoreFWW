using AqtCoreFW.Identity.Application.Commands.RegisterUser;
using FluentValidation.TestHelper;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Validator tests for RegisterUserCommand — focus on the optional CCCD (SigningNationalId) rule
/// added for signing-identity mapping. Password/email rules are exercised via the valid baseline.
/// </summary>
public sealed class RegisterUserCommandValidatorTests
{
    private readonly RegisterUserCommandValidator _sut = new();

    private static RegisterUserCommand Valid() => new(
        FullName: "Nguyễn Văn A",
        Email: "a@example.com",
        Password: "Abcdef123!@#",
        DepartmentCode: "CB001",
        TenantId: null,
        SigningNationalId: null);

    [Fact]
    public void ValidBaseline_Passes()
        => _sut.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Valid12DigitCccd_Passes()
        => _sut.TestValidate(Valid() with { SigningNationalId = "017091004878" })
            .ShouldNotHaveValidationErrorFor(x => x.SigningNationalId);

    [Theory]
    [InlineData("12345")]          // too short
    [InlineData("0170910048781")]  // 13 digits
    [InlineData("01709100487a")]   // non-digit char
    public void InvalidCccd_Fails(string cccd)
        => _sut.TestValidate(Valid() with { SigningNationalId = cccd })
            .ShouldHaveValidationErrorFor(x => x.SigningNationalId);
}
