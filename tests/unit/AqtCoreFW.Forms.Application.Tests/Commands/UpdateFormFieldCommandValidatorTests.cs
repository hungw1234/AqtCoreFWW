using AqtCoreFW.Forms.Application.Commands.UpdateFormField;
using FluentValidation.TestHelper;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for UpdateFormFieldCommandValidator.
/// Validates required GUIDs and optional field length constraints.
/// Phase-07: FormTemplate retiered T1→T2; TemplatePublicId is now Guid (was long TemplateId).
/// </summary>
public sealed class UpdateFormFieldCommandValidatorTests
{
    private readonly UpdateFormFieldCommandValidator _validator = new();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();
    // FieldId is long (T1 FormField internal PK)
    private const long FieldId = 1L;

    // --- Valid command passes ---

    [Fact]
    public void Validate_ValidCommand_NoErrors()
    {
        var cmd = BuildValid();
        _validator.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    // --- TemplatePublicId ---

    [Fact]
    public void Validate_EmptyTemplatePublicId_HasError()
    {
        var cmd = BuildValid() with { TemplatePublicId = Guid.Empty };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.TemplatePublicId);
    }

    // --- FieldId ---

    [Fact]
    public void Validate_ZeroFieldId_HasError()
    {
        var cmd = BuildValid() with { FieldId = 0L };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.FieldId);
    }

    // --- TenantId ---

    [Fact]
    public void Validate_EmptyTenantId_HasError()
    {
        var cmd = BuildValid() with { TenantId = Guid.Empty };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.TenantId);
    }

    // --- UpdatedBy ---

    [Fact]
    public void Validate_EmptyUpdatedBy_HasError()
    {
        var cmd = BuildValid() with { UpdatedBy = Guid.Empty };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.UpdatedBy);
    }

    // --- LabelVi length ---

    [Fact]
    public void Validate_LabelViTooLong_HasError()
    {
        var cmd = BuildValid() with { LabelVi = new string('x', 501) };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.LabelVi);
    }

    [Fact]
    public void Validate_LabelViAtMaxLength_NoError()
    {
        var cmd = BuildValid() with { LabelVi = new string('x', 500) };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.LabelVi);
    }

    [Fact]
    public void Validate_NullLabelVi_NoError()
    {
        var cmd = BuildValid() with { LabelVi = null };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.LabelVi);
    }

    // --- LabelEn length ---

    [Fact]
    public void Validate_LabelEnTooLong_HasError()
    {
        var cmd = BuildValid() with { LabelEn = new string('x', 501) };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.LabelEn);
    }

    [Fact]
    public void Validate_NullLabelEn_NoError()
    {
        var cmd = BuildValid() with { LabelEn = null };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.LabelEn);
    }

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
        // Empty string = clear pointer (per command doc); validation should skip JSON check.
        var cmd = BuildValid() with { PrefillSourceJson = string.Empty };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.PrefillSourceJson);
    }

    [Fact]
    public void Validate_ValidPrefillSourceJson_NoError()
    {
        var cmd = BuildValid() with
        {
            PrefillSourceJson = """{"sourceFormTemplateId":"11111111-1111-1111-1111-111111111111","sourceFieldKey":"customerName"}"""
        };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.PrefillSourceJson);
    }

    [Fact]
    public void Validate_MalformedPrefillSourceJson_HasError()
    {
        var cmd = BuildValid() with { PrefillSourceJson = "{not valid json" };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.PrefillSourceJson);
    }

    [Fact]
    public void Validate_OversizePrefillSourceJson_HasError()
    {
        // 2001 chars of valid-looking JSON (filler padding inside a string value to stay parseable)
        var padding = new string('x', 2001 - "{\"k\":\"\"}".Length);
        var cmd = BuildValid() with { PrefillSourceJson = "{\"k\":\"" + padding + "\"}" };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.PrefillSourceJson);
    }

    // ─── Helper ────────────────────────────────────────────────────────────────

    private static UpdateFormFieldCommand BuildValid() =>
        new(
            TemplatePublicId:    Guid.NewGuid(),
            FieldId:             FieldId,
            TenantId:            TenantId,
            LabelVi:             "Nhãn tiếng Việt",
            LabelEn:             "English label",
            Required:            null,
            ValidationRulesJson: null,
            DataSourceJson:      null,
            FormulaJson:         null,
            IsPii:               null,
            UpdatedBy:           UserId);
}
