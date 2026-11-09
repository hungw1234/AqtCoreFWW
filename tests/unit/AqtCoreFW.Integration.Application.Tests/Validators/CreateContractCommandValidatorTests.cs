using AqtCoreFW.Integration.Application.Commands.CreateContract;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace AqtCoreFW.Integration.Application.Tests.Validators;

/// <summary>
/// Unit tests for CreateContractCommandValidator.
/// Verifies required fields, length limits, and date ordering rules.
/// PartnerPublicId is Guid (Phase-07 PR-B Partner T2 AK).
/// </summary>
public sealed class CreateContractCommandValidatorTests
{
    private readonly CreateContractCommandValidator _sut = new();

    private static readonly DateTime ValidEffective =
        new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ValidExpiry =
        new(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Guid SamplePartnerPublicId = Guid.NewGuid();

    private static CreateContractCommand MakeCmd(
        Guid? tenantId = null,
        Guid? partnerPublicId = null,
        string title = "Test Contract",
        string? description = null,
        DateTime? effectiveDate = null,
        DateTime? expiryDate = null,
        string? dataScopeJson = null) =>
        new(tenantId ?? Guid.NewGuid(),
            partnerPublicId ?? SamplePartnerPublicId,
            title, description,
            effectiveDate ?? ValidEffective,
            expiryDate ?? ValidExpiry,
            dataScopeJson);

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
    public void Empty_PartnerPublicId_Fails() =>
        _sut.TestValidate(MakeCmd(partnerPublicId: Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.PartnerPublicId);

    [Fact]
    public void Empty_Title_Fails() =>
        _sut.TestValidate(MakeCmd(title: ""))
            .ShouldHaveValidationErrorFor(x => x.Title);

    [Fact]
    public void Title_Exceeds_300_Fails() =>
        _sut.TestValidate(MakeCmd(title: new string('x', 301)))
            .ShouldHaveValidationErrorFor(x => x.Title);

    [Fact]
    public void Description_Exceeds_2000_Fails() =>
        _sut.TestValidate(MakeCmd(description: new string('x', 2001)))
            .ShouldHaveValidationErrorFor(x => x.Description);

    [Fact]
    public void Null_Description_Passes()
    {
        var result = _sut.TestValidate(MakeCmd(description: null));
        result.ShouldNotHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void ExpiryDate_Before_EffectiveDate_Fails()
    {
        var cmd = MakeCmd(
            effectiveDate: ValidEffective,
            expiryDate: ValidEffective.AddDays(-1));
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.ExpiryDate);
    }

    [Fact]
    public void ExpiryDate_Equal_To_EffectiveDate_Fails()
    {
        var cmd = MakeCmd(effectiveDate: ValidEffective, expiryDate: ValidEffective);
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.ExpiryDate);
    }

    [Fact]
    public void Null_ExpiryDate_Passes()
    {
        var result = _sut.TestValidate(MakeCmd(expiryDate: null));
        result.ShouldNotHaveValidationErrorFor(x => x.ExpiryDate);
    }
}
