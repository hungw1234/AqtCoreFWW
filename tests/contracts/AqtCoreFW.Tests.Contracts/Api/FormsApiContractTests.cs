using System.Text.Json;
using AqtCoreFW.Forms.Application.DTOs;

namespace AqtCoreFW.Tests.Contracts.Api;

/// <summary>
/// Contract tests for Forms module DTOs - TC-CON-API-006.
/// Phase-07: FormTemplateDto.Id is Guid (T2 PublicId AK - external addressing);
///           FormFieldDto.Id is long (T1 bigint PK).
/// </summary>
public sealed class FormsApiContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public void FormTemplateDto_JsonShape_AllFieldsPresent()
    {
        // Named-args (FORMS-FORMFIELDDTO-POSITIONAL-CONTRACT-TEST-DRIFT): FormFieldDto is a positional
        // record with 15 params (11 required + 4 tail-optional). A future insert of a param MID-record
        // would silently shift positional args here and mask the drift - exactly the trap in
        // `feedback-formtemplate-addfield-ispii-positional-trap`. Named-args make param-order changes
        // fail-loud at compile time and keep this contract test faithful to the DTO shape it asserts.
        // The `FormFieldDtoPositionalConstructionGuardTests` arch guard enforces this rule repo-wide.
        var field = new FormFieldDto(
            Id: 1L,
            FieldKey: "full_name",
            Type: "Text",
            LabelVi: "Họ tên",
            LabelEn: "Full Name",
            DisplayOrder: 1,
            Required: true,
            IsActive: true,
            AddedInVersion: 1,
            ValidationRules: null,
            Options: null);

        var dto = new FormTemplateDto(
            Guid.NewGuid(), Guid.NewGuid(), "Citizen Request",
            "Đơn yêu cầu", "FORM-001", "Json", "Published",
            1, DateTimeOffset.UtcNow, [field]);

        var json = JsonSerializer.Serialize(dto);
        var root = JsonDocument.Parse(json).RootElement;

        root.TryGetProperty("Id", out _).Should().BeTrue();
        root.TryGetProperty("Name", out _).Should().BeTrue();
        root.TryGetProperty("Code", out _).Should().BeTrue();
        root.TryGetProperty("Status", out _).Should().BeTrue();
        root.TryGetProperty("Version", out _).Should().BeTrue();
        root.TryGetProperty("Fields", out var fields).Should().BeTrue();
        fields.ValueKind.Should().Be(JsonValueKind.Array);
        fields.GetArrayLength().Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void FormTemplateDto_RoundTrip_Lossless()
    {
        var dto = new FormTemplateDto(
            Guid.NewGuid(), Guid.NewGuid(), "Test", "Test",
            "T-001", "Json", "Draft", 1, DateTimeOffset.UtcNow, []);

        var json = JsonSerializer.Serialize(dto);
        var d = JsonSerializer.Deserialize<FormTemplateDto>(json);

        d.Should().NotBeNull();
        d!.Code.Should().Be(dto.Code);
        d.Version.Should().Be(dto.Version);
        d.StorageMode.Should().Be(dto.StorageMode);
    }
}
