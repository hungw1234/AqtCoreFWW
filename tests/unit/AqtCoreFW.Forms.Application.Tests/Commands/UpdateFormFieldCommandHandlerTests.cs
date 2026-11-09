using AqtCoreFW.Forms.Application.Commands.UpdateFormField;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for UpdateFormFieldCommandHandler.
/// Covers label updates, partial patches, not-found guards, IsPii, and validation rules.
/// Phase-07: FormTemplate retiered T1→T2; commands now use TemplatePublicId (Guid AK).
/// </summary>
public sealed class UpdateFormFieldCommandHandlerTests
{
    private readonly IFormTemplateRepository _templateRepo;
    private readonly IGuidGenerator          _guidGenerator;
    private readonly UpdateFormFieldCommandHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId   = Guid.NewGuid();

    public UpdateFormFieldCommandHandlerTests()
    {
        _templateRepo  = Substitute.For<IFormTemplateRepository>();
        _guidGenerator = Substitute.For<IGuidGenerator>();
        _guidGenerator.NewIdMonotonic().Returns(_ => Guid.NewGuid());

        _handler = new UpdateFormFieldCommandHandler(_templateRepo);
    }

    // --- Label + Required update ---

    [Fact]
    public async Task Handle_ValidCommand_UpdatesLabelAndRequired()
    {
        var (template, field) = CreateTemplateWithField();
        SetupTemplate(template);

        var cmd = BuildCommand(template.PublicId, field.Id,
            labelVi: "Tên mới", labelEn: "New name", required: true);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.LabelVi.Should().Be("Tên mới");
        result.Value.LabelEn.Should().Be("New name");
        result.Value.Required.Should().BeTrue();
    }

    // --- Partial update: only labelVi provided ---

    [Fact]
    public async Task Handle_PartialUpdate_OnlyChangesProvidedFields()
    {
        var (template, field) = CreateTemplateWithField(labelVi: "Ban đầu", labelEn: "Initial");
        SetupTemplate(template);

        // Only provide labelVi — labelEn and required should remain unchanged
        var cmd = BuildCommand(template.PublicId, field.Id,
            labelVi: "Đã cập nhật", labelEn: null, required: null);

        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.LabelVi.Should().Be("Đã cập nhật");
        result.Value.LabelEn.Should().Be("Initial");
    }

    // --- Template not found ---

    [Fact]
    public async Task Handle_NonExistentTemplate_ReturnsNotFound()
    {
        _templateRepo
            .GetByPublicIdWithFieldsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(null));

        var cmd = BuildCommand(Guid.NewGuid(), 88L);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Field not found or inactive ---

    [Fact]
    public async Task Handle_NonExistentField_ReturnsNotFound()
    {
        var (template, _) = CreateTemplateWithField();
        SetupTemplate(template);

        var cmd = BuildCommand(template.PublicId, 999L); // unknown field id
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_InactiveField_ReturnsError()
    {
        var (template, field) = CreateTemplateWithField();
        // Deactivate the field (soft-delete)
        template.DeactivateField(field.Id, TestUserId);
        SetupTemplate(template);

        var cmd = BuildCommand(template.PublicId, field.Id);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- ValidationRules update ---

    [Fact]
    public async Task Handle_UpdatesValidationRules()
    {
        var (template, field) = CreateTemplateWithField();
        SetupTemplate(template);

        const string rulesJson = """{"minLength":2,"maxLength":100}""";
        var cmd = BuildCommand(template.PublicId, field.Id, validationRulesJson: rulesJson);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // ValidationRules deserialized from JSON
        result.Value.ValidationRules.Should().NotBeNull();
        result.Value.ValidationRules!.MinLength.Should().Be(2);
        result.Value.ValidationRules.MaxLength.Should().Be(100);
    }

    // --- IsPii update ---

    [Fact]
    public async Task Handle_UpdatesIsPii()
    {
        var (template, field) = CreateTemplateWithField();
        SetupTemplate(template);

        var cmd = BuildCommand(template.PublicId, field.Id, isPii: true);
        var result = await _handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Verify repo UpdateAsync was called (side-effect assertion)
        await _templateRepo.Received(1).UpdateAsync(
            Arg.Is<FormTemplate>(t => t.Id == template.Id),
            Arg.Any<CancellationToken>());
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private (FormTemplate template, FormField field) CreateTemplateWithField(
        string labelVi = "Nhãn", string labelEn = "Label")
    {
        var template = FormTemplate.Create(
            _guidGenerator, TestTenantId, "Template", "Biểu mẫu", "upd_field_tmpl", StorageMode.Json, TestUserId);
        var field = template.AddField(
            "test_field", FormFieldType.Text, labelVi, labelEn,
            displayOrder: 1, required: false, validationRulesJson: null, TestUserId);
        return (template, field);
    }

    private void SetupTemplate(FormTemplate template)
    {
        _templateRepo
            // FormTemplate is T2: GetByPublicIdWithFieldsAsync takes Guid publicId
            .GetByPublicIdWithFieldsAsync(template.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(template));
    }

    private static UpdateFormFieldCommand BuildCommand(
        Guid templatePublicId,
        long fieldId,
        string? labelVi = null,
        string? labelEn = null,
        bool? required = null,
        string? validationRulesJson = null,
        bool? isPii = null) =>
        new(
            TemplatePublicId:    templatePublicId,
            FieldId:             fieldId,
            TenantId:            TestTenantId,
            LabelVi:             labelVi,
            LabelEn:             labelEn,
            Required:            required,
            ValidationRulesJson: validationRulesJson,
            DataSourceJson:      null,
            FormulaJson:         null,
            IsPii:               isPii,
            UpdatedBy:           TestUserId);
}
