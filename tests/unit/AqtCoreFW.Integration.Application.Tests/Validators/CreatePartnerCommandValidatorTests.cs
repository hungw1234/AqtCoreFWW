using AqtCoreFW.Integration.Application.Commands.CreatePartner;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace AqtCoreFW.Integration.Application.Tests.Validators;

/// <summary>
/// Unit tests for CreatePartnerCommandValidator.
/// Verifies required fields, length limits, and optional field rules.
/// </summary>
public sealed class CreatePartnerCommandValidatorTests
{
    private readonly CreatePartnerCommandValidator _sut = new();

    private static CreatePartnerCommand MakeCmd(
        Guid? tenantId = null,
        string name = "Agency A",
        string code = "AGY-A",
        string? contactEmail = "a@gov.vn",
        string? contactPhone = "+84123456789",
        string? endpoint = "https://api.gov.vn",
        string? authScheme = "Bearer") =>
        new(tenantId ?? Guid.NewGuid(), name, code,
            contactEmail, contactPhone, endpoint, authScheme);

    [Fact]
    public void Valid_Command_Passes()
    {
        var result = _sut.TestValidate(MakeCmd());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_TenantId_Fails() =>
        _sut.TestValidate(MakeCmd(tenantId: Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.TenantId);

    [Fact]
    public void Empty_Name_Fails() =>
        _sut.TestValidate(MakeCmd(name: ""))
            .ShouldHaveValidationErrorFor(x => x.Name);

    [Fact]
    public void Name_Exceeds_200_Fails() =>
        _sut.TestValidate(MakeCmd(name: new string('x', 201)))
            .ShouldHaveValidationErrorFor(x => x.Name);

    [Fact]
    public void Empty_Code_Fails() =>
        _sut.TestValidate(MakeCmd(code: ""))
            .ShouldHaveValidationErrorFor(x => x.Code);

    [Fact]
    public void Code_Exceeds_50_Fails() =>
        _sut.TestValidate(MakeCmd(code: new string('x', 51)))
            .ShouldHaveValidationErrorFor(x => x.Code);

    [Fact]
    public void Invalid_Email_Fails() =>
        _sut.TestValidate(MakeCmd(contactEmail: "not-an-email"))
            .ShouldHaveValidationErrorFor(x => x.ContactEmail);

    [Fact]
    public void Null_Email_Passes()
    {
        var result = _sut.TestValidate(MakeCmd(contactEmail: null));
        result.ShouldNotHaveValidationErrorFor(x => x.ContactEmail);
    }

    [Fact]
    public void Null_Phone_Passes()
    {
        var result = _sut.TestValidate(MakeCmd(contactPhone: null));
        result.ShouldNotHaveValidationErrorFor(x => x.ContactPhone);
    }

    [Fact]
    public void Null_Endpoint_Passes()
    {
        var result = _sut.TestValidate(MakeCmd(endpoint: null));
        result.ShouldNotHaveValidationErrorFor(x => x.Endpoint);
    }

    [Fact]
    public void Null_AuthScheme_Passes()
    {
        var result = _sut.TestValidate(MakeCmd(authScheme: null));
        result.ShouldNotHaveValidationErrorFor(x => x.AuthScheme);
    }
}
