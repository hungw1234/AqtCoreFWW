using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Services;

namespace AqtCoreFW.Forms.Infrastructure.Tests.Validation;

/// <summary>
/// Unit tests for FormValidationService.ValidateAddressField. The address model is 2-level
/// (Tỉnh → Phường): a submitted value must carry province + ward. The intermediate district level
/// was removed with the hệ-34 administrative reform — a payload that still includes district stays
/// valid (the extra key is ignored), so pre-reform submissions keep validating. FormValidationService
/// is a pure stateless service — instantiated directly with no mocks.
/// </summary>
public sealed class AddressFieldValidationTests
{
    private static readonly long TemplateId = 1L;

    private readonly FormValidationService _sut = new();

    /// <summary>An optional (required:false) AddressField — shape validation runs whenever a value is present.</summary>
    private static FormField MakeAddressField(string key = "address") =>
        FormField.Create(
            TemplateId, key,
            FormFieldType.AddressField,
            "Địa chỉ", "Address",
            displayOrder: 1,
            required: false,
            addedInVersion: 1);

    private static Dictionary<string, object?> Data(string key, object? val) =>
        new() { [key] = val };

    [Fact]
    public async Task Address_ProvinceAndWard_NoError()
    {
        // The valid 2-level shape: province + ward, no district.
        var data = Data("address", """{"province":"01","ward":"01001"}""");

        var result = await _sut.ValidateAsync([MakeAddressField()], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty("province + ward is the valid 2-level address shape");
    }

    [Fact]
    public async Task Address_WithLegacyDistrict_StillNoError()
    {
        // A pre-reform payload that still carries district must keep validating — the extra key is ignored.
        var data = Data("address", """{"province":"01","district":"011","ward":"01001"}""");

        var result = await _sut.ValidateAsync([MakeAddressField()], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty("a legacy district key is ignored, not rejected");
    }

    [Fact]
    public async Task Address_MissingWard_ReturnsError()
    {
        var data = Data("address", """{"province":"01"}""");

        var result = await _sut.ValidateAsync([MakeAddressField()], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => FieldOf(e) == "address");
    }

    [Fact]
    public async Task Address_MissingProvince_ReturnsError()
    {
        var data = Data("address", """{"ward":"01001"}""");

        var result = await _sut.ValidateAsync([MakeAddressField()], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => FieldOf(e) == "address");
    }

    [Fact]
    public async Task Address_NullValue_NoError()
    {
        // An absent optional address is valid — ValidateAddressField is skipped for an empty value.
        var data = Data("address", null);

        var result = await _sut.ValidateAsync([MakeAddressField()], data);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    /// <summary>
    /// Extracts the field key stored as metadata "Property" on a ValidationError
    /// (set via the ValidationError constructor).
    /// </summary>
    private static string? FieldOf(AqtCoreFW.SharedKernel.Errors.ValidationError error) =>
        error.Metadata.TryGetValue("Property", out var v) ? v?.ToString() : null;
}
