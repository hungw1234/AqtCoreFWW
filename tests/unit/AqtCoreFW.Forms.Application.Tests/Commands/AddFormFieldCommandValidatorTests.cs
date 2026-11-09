using AqtCoreFW.Forms.Application.Commands.AddFormField;
using AqtCoreFW.Forms.Domain.Enums;
using FluentValidation.TestHelper;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for AddFormFieldCommandValidator.
/// P6-FU1 (2026-05-16): focused coverage on PrefillSourceJson rules (length + JSON well-formedness).
/// Existing rules (FieldKey, LabelVi/En, DisplayOrder, DataSourceJson, FormulaJson) are exercised
/// indirectly by AddFormFieldCommandHandlerTests; backfilling is out of scope for this FU.
/// </summary>
public sealed class AddFormFieldCommandValidatorTests
{
    private readonly AddFormFieldCommandValidator _validator = new();

    private static AddFormFieldCommand BuildValid() =>
        new(
            TemplatePublicId:    Guid.NewGuid(),
            TenantId:            Guid.NewGuid(),
            FieldKey:            "customerName",
            Type:                FormFieldType.Text,
            LabelVi:             "Tên khách hàng",
            LabelEn:             "Customer name",
            DisplayOrder:        0,
            Required:            false,
            ValidationRulesJson: null,
            Options:             null,
            UpdatedBy:           Guid.NewGuid());

    // --- PrefillSourceJson (P6-FU1) ---

    [Fact]
    public void Validate_NullPrefillSourceJson_NoError()
    {
        var cmd = BuildValid() with { PrefillSourceJson = null };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.PrefillSourceJson);
    }

    [Fact]
    public void Validate_EmptyPrefillSourceJson_NoError()
    {
        // Empty string skipped by .When() guard — symmetric with Update validator.
        var cmd = BuildValid() with { PrefillSourceJson = string.Empty };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.PrefillSourceJson);
    }

    [Fact]
    public void Validate_ValidPrefillSourceJson_NoError()
    {
        var cmd = BuildValid() with
        {
            PrefillSourceJson = """{"sourceFormTemplateId":"22222222-2222-2222-2222-222222222222","sourceFieldKey":"phone"}"""
        };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.PrefillSourceJson);
    }

    [Fact]
    public void Validate_MalformedPrefillSourceJson_HasError()
    {
        var cmd = BuildValid() with { PrefillSourceJson = "{broken" };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.PrefillSourceJson);
    }

    [Fact]
    public void Validate_OversizePrefillSourceJson_HasError()
    {
        var padding = new string('x', 2001 - "{\"k\":\"\"}".Length);
        var cmd = BuildValid() with { PrefillSourceJson = "{\"k\":\"" + padding + "\"}" };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.PrefillSourceJson);
    }
}
