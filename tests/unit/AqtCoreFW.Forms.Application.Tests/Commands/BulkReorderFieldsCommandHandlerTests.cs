using AqtCoreFW.Forms.Application.Commands.BulkReorderFields;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for BulkReorderFieldsCommandHandler.
/// Covers happy-path reorder, unknown FieldId guard, duplicate order guard,
/// and atomic single-UpdateAsync call.
/// Phase-07: BulkReorderFieldsCommand accepts TemplatePublicId (Guid AK).
/// Handler resolves Guid → internal long Id via IFormTemplateRepository.
/// </summary>
public sealed class BulkReorderFieldsCommandHandlerTests
{
    private readonly IFormFieldRepository             _fieldRepo;
    private readonly IFormTemplateRepository          _templateRepo;
    private readonly BulkReorderFieldsCommandHandler  _handler;
    private readonly IGuidGenerator                   _guidGen;

    private static readonly Guid TestTenantId  = Guid.NewGuid();
    private static readonly Guid TestUserId    = Guid.NewGuid();

    public BulkReorderFieldsCommandHandlerTests()
    {
        _fieldRepo    = Substitute.For<IFormFieldRepository>();
        _templateRepo = Substitute.For<IFormTemplateRepository>();
        _guidGen      = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _handler      = new BulkReorderFieldsCommandHandler(_fieldRepo, _templateRepo);
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_ValidOrder_ReturnsSuccess()
    {
        var template = CreateTemplate();
        var (f1, f2) = CreateTwoFields(template.Id);
        SetupTemplate(template);
        SetupFields(template.Id, f1, f2);

        var command = BuildCommand(
            template.PublicId,
            new FieldOrderItem(f1.Id, 2),
            new FieldOrderItem(f2.Id, 1));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidOrder_UpdatesDisplayOrderOnFields()
    {
        var template = CreateTemplate();
        var (f1, f2) = CreateTwoFields(template.Id);
        SetupTemplate(template);
        SetupFields(template.Id, f1, f2);

        var command = BuildCommand(
            template.PublicId,
            new FieldOrderItem(f1.Id, 5),
            new FieldOrderItem(f2.Id, 3));

        await _handler.Handle(command, CancellationToken.None);

        f1.DisplayOrder.Should().Be(5);
        f2.DisplayOrder.Should().Be(3);
    }

    [Fact]
    public async Task Handle_ValidOrder_CallsUpdateAsyncExactlyOnce()
    {
        // Handler applies all changes in-memory then calls UpdateAsync once (atomic batch)
        var template = CreateTemplate();
        var (f1, f2) = CreateTwoFields(template.Id);
        SetupTemplate(template);
        SetupFields(template.Id, f1, f2);

        var command = BuildCommand(
            template.PublicId,
            new FieldOrderItem(f1.Id, 2),
            new FieldOrderItem(f2.Id, 1));

        await _handler.Handle(command, CancellationToken.None);

        await _fieldRepo.Received(1).UpdateAsync(Arg.Any<FormField>(), Arg.Any<CancellationToken>());
    }

    // --- Template not found ---

    [Fact]
    public async Task Handle_TemplateNotFound_ReturnsError()
    {
        _templateRepo
            .GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(null));

        var command = BuildCommand(Guid.NewGuid());
        var result  = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Unknown FieldId guard ---

    [Fact]
    public async Task Handle_UnknownFieldId_ReturnsError()
    {
        var template = CreateTemplate();
        var (f1, _)  = CreateTwoFields(template.Id);
        SetupTemplate(template);
        SetupFields(template.Id, f1); // only f1 in template

        var command = BuildCommand(
            template.PublicId,
            new FieldOrderItem(f1.Id, 1),
            new FieldOrderItem(999L, 2)); // unknown field id

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UnknownFieldId_DoesNotCallUpdate()
    {
        var template = CreateTemplate();
        var (f1, _)  = CreateTwoFields(template.Id);
        SetupTemplate(template);
        SetupFields(template.Id, f1);

        var command = BuildCommand(
            template.PublicId,
            new FieldOrderItem(999L, 1)); // unknown field id

        await _handler.Handle(command, CancellationToken.None);

        await _fieldRepo.DidNotReceive().UpdateAsync(Arg.Any<FormField>(), Arg.Any<CancellationToken>());
    }

    // --- Duplicate order guard ---

    [Fact]
    public async Task Handle_DuplicateOrderValues_ReturnsError()
    {
        var template = CreateTemplate();
        var (f1, f2) = CreateTwoFields(template.Id);
        SetupTemplate(template);
        SetupFields(template.Id, f1, f2);

        var command = BuildCommand(
            template.PublicId,
            new FieldOrderItem(f1.Id, 1),
            new FieldOrderItem(f2.Id, 1)); // same order as f1

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Empty items list ---

    [Fact]
    public async Task Handle_EmptyItemsList_ReturnsSuccessWithoutCallingUpdate()
    {
        var template = CreateTemplate();
        SetupTemplate(template);
        SetupFields(template.Id); // no fields needed

        var command = new BulkReorderFieldsCommand(template.PublicId, TestTenantId, []);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _fieldRepo.DidNotReceive().UpdateAsync(Arg.Any<FormField>(), Arg.Any<CancellationToken>());
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private FormTemplate CreateTemplate()
    {
        return FormTemplate.Create(
            _guidGen, TestTenantId, "Template", "Biểu mẫu", "reorder_tmpl", StorageMode.Json, TestUserId);
    }

    private static (FormField f1, FormField f2) CreateTwoFields(long templateId)
    {
        // FormField is T1 (long Id DB-assigned). Set unique Ids via reflection
        // so handler's ToDictionary(f => f.Id) doesn't collide on Id=0.
        var f1 = FormField.Create(templateId, "field_a", FormFieldType.Text, "A", "A", 1, false, 1);
        var f2 = FormField.Create(templateId, "field_b", FormFieldType.Text, "B", "B", 2, false, 1);
        SetFieldId(f1, 1L);
        SetFieldId(f2, 2L);
        return (f1, f2);
    }

    private static void SetFieldId(FormField field, long id)
    {
        var idProp = typeof(AqtCoreFW.SharedKernel.Domain.Entity)
            .GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
        idProp.GetSetMethod(nonPublic: true)!.Invoke(field, [id]);
    }

    private void SetupTemplate(FormTemplate template)
    {
        _templateRepo
            .GetByPublicIdAsync(template.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(template));
    }

    private void SetupFields(long templateId, params FormField[] fields)
    {
        _fieldRepo
            .GetByTemplateIdAsync(templateId, activeOnly: true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FormField>>(fields.ToList()));
    }

    private static BulkReorderFieldsCommand BuildCommand(
        Guid templatePublicId,
        params FieldOrderItem[] items) =>
        new(templatePublicId, TestTenantId, items.ToList());
}
