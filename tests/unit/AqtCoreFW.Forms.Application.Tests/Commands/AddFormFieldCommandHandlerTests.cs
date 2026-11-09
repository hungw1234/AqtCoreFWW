using AqtCoreFW.Forms.Application.Commands.AddFormField;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for AddFormFieldCommandHandler.
/// Covers template-not-found, duplicate FieldKey, options wiring, Active+Table ALTER path.
/// Phase-07: FormTemplate retiered T1→T2 — factory requires IGuidGenerator; handler resolves
/// TemplatePublicId (Guid) via GetByPublicIdWithFieldsAsync.
/// </summary>
public sealed class AddFormFieldCommandHandlerTests
{
    private readonly IFormTemplateRepository _templateRepo;
    private readonly IFormTableMigrator      _tableMigrator;
    private readonly AddFormFieldCommandHandler _handler;
    private readonly IGuidGenerator _guidGen;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId   = Guid.NewGuid();

    public AddFormFieldCommandHandlerTests()
    {
        _templateRepo  = Substitute.For<IFormTemplateRepository>();
        _tableMigrator = Substitute.For<IFormTableMigrator>();
        _guidGen       = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _handler       = new AddFormFieldCommandHandler(_templateRepo, _tableMigrator);
    }

    // --- Template not found ---

    [Fact]
    public async Task Handle_TemplateNotFound_ReturnsError()
    {
        _templateRepo
            .GetByPublicIdWithFieldsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(null));

        var result = await _handler.Handle(BuildCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_ValidField_ReturnsSuccessWithFieldDto()
    {
        var template = CreateDraftTemplate();
        SetupTemplate(template);

        var result = await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FieldKey.Should().Be("new_field");
    }

    [Fact]
    public async Task Handle_ValidField_PersistsViaTemplateRepository()
    {
        var template = CreateDraftTemplate();
        SetupTemplate(template);

        await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        await _templateRepo.Received(1).UpdateAsync(
            Arg.Is<FormTemplate>(t => t.Id == template.Id),
            Arg.Any<CancellationToken>());
    }

    // --- Duplicate FieldKey guard ---

    [Fact]
    public async Task Handle_DuplicateFieldKey_ReturnsValidationError()
    {
        var template = CreateDraftTemplate();
        // Pre-add a field with the same key
        template.AddField("new_field", FormFieldType.Text, "Existing", "Existing", 1, false, null, TestUserId);
        SetupTemplate(template);

        var result = await _handler.Handle(BuildCommand(template.PublicId, fieldKey: "new_field"), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Options are wired onto the returned field ---

    [Fact]
    public async Task Handle_FieldWithOptions_ReturnedDtoContainsOptions()
    {
        var template = CreateDraftTemplate();
        SetupTemplate(template);

        var options = new List<AddFormFieldOptionDto>
        {
            new("opt1", "Lựa chọn 1", "Option 1", 1),
            new("opt2", "Lựa chọn 2", "Option 2", 2)
        };
        var command = BuildCommand(template.PublicId, type: FormFieldType.EnumRef, options: options);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Options.Should().HaveCount(2);
    }

    // --- Active + Table mode → triggers ALTER TABLE ---

    [Fact]
    public async Task Handle_ActiveTableTemplate_CallsAddColumnOnMigrator()
    {
        // Create template, add at least one field, then publish to Active (Table mode)
        var template = FormTemplate.Create(
            _guidGen, TestTenantId, "T", "T", "tbl_template", StorageMode.Table, TestUserId);
        template.AddField("existing", FormFieldType.Text, "L", "L", 1, false, null, TestUserId);
        template.Publish(TestUserId);       // Draft → Materializing
        template.MarkActive(TestUserId);    // Materializing → Active
        SetupTemplate(template);

        await _handler.Handle(BuildCommand(template.PublicId, fieldKey: "new_col"), CancellationToken.None);

        await _tableMigrator.Received(1).AddColumnAsync(
            template.Id,
            Arg.Is<FormField>(f => f.FieldKey == "new_col"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DraftTemplate_DoesNotCallMigrator()
    {
        var template = CreateDraftTemplate();
        SetupTemplate(template);

        await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        await _tableMigrator.DidNotReceive()
            .AddColumnAsync(Arg.Any<long>(), Arg.Any<FormField>(), Arg.Any<CancellationToken>());
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private FormTemplate CreateDraftTemplate()
    {
        return FormTemplate.Create(
            _guidGen, TestTenantId, "Template", "Biểu mẫu", "add_field_tmpl", StorageMode.Json, TestUserId);
    }

    private void SetupTemplate(FormTemplate template)
    {
        // Handler resolves via PublicId (Guid AK)
        _templateRepo
            .GetByPublicIdWithFieldsAsync(template.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(template));
    }

    private static AddFormFieldCommand BuildCommand(
        Guid templatePublicId,
        string fieldKey = "new_field",
        FormFieldType type = FormFieldType.Text,
        List<AddFormFieldOptionDto>? options = null) =>
        new(
            TemplatePublicId:    templatePublicId,
            TenantId:            TestTenantId,
            FieldKey:            fieldKey,
            Type:                type,
            LabelVi:             "Nhãn",
            LabelEn:             "Label",
            DisplayOrder:        1,
            Required:            false,
            ValidationRulesJson: null,
            Options:             options,
            UpdatedBy:           TestUserId);
}
