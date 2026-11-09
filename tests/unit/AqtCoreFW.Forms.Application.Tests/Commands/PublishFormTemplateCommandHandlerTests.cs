using AqtCoreFW.Forms.Application.Commands.PublishFormTemplate;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for PublishFormTemplateCommandHandler.
/// Verifies Draft→Active (Json) and Draft→Materializing (Table) transitions,
/// access-denied guard, and materialization queue enqueue behaviour.
/// Phase-07: FormTemplate retiered T1→T2; commands now use PublicId (Guid AK).
/// </summary>
public sealed class PublishFormTemplateCommandHandlerTests
{
    private readonly IFormTemplateRepository _repository;
    private readonly IMaterializationQueue   _queue;
    private readonly IGuidGenerator          _guidGenerator;
    private readonly PublishFormTemplateCommandHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId   = Guid.NewGuid();

    public PublishFormTemplateCommandHandlerTests()
    {
        _repository    = Substitute.For<IFormTemplateRepository>();
        _queue         = Substitute.For<IMaterializationQueue>();
        _guidGenerator = Substitute.For<IGuidGenerator>();
        _guidGenerator.NewIdMonotonic().Returns(_ => Guid.NewGuid());

        _handler = new PublishFormTemplateCommandHandler(
            _repository, _queue, NullLogger<PublishFormTemplateCommandHandler>.Instance);
    }

    // --- Template not found ---

    [Fact]
    public async Task Handle_TemplateNotFound_ReturnsError()
    {
        _repository
            .GetByPublicIdWithFieldsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(null));

        var result = await _handler.Handle(BuildCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Json mode: Draft → Active ---

    [Fact]
    public async Task Handle_DraftJsonTemplate_PublishesAndReturnsSuccess()
    {
        var template = CreateDraftTemplate(StorageMode.Json);
        SetupTemplate(template);

        var result = await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        template.Status.Should().Be(FormTemplateStatus.Active);
    }

    [Fact]
    public async Task Handle_DraftJsonTemplate_PersistsStatusChange()
    {
        var template = CreateDraftTemplate(StorageMode.Json);
        SetupTemplate(template);

        await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        await _repository.Received(1).UpdateAsync(
            Arg.Is<FormTemplate>(t => t.Id == template.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DraftJsonTemplate_DoesNotEnqueueMaterialization()
    {
        var template = CreateDraftTemplate(StorageMode.Json);
        SetupTemplate(template);

        await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        await _queue.DidNotReceive().EnqueueAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    // --- BP MVP P1: atomic schema snapshot on Publish ---

    [Fact]
    public async Task Handle_DraftJsonTemplate_StagesVersionSnapshot_AtomicWithUpdate()
    {
        var template = CreateDraftTemplate(StorageMode.Json);
        SetupTemplate(template);

        FormTemplateVersion? captured = null;
        await _repository.AddVersionAsync(
            Arg.Do<FormTemplateVersion>(v => captured = v), Arg.Any<CancellationToken>());

        var result = await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(template.TenantId);
        captured.FormTemplateId.Should().Be(template.Id);
        captured.VersionNumber.Should().Be(template.Version);
        // Parse to assert structure (substring "version" would also match "schemaEnvelopeVersion")
        var doc = System.Text.Json.JsonDocument.Parse(captured.SchemaJson);
        doc.RootElement.GetProperty("schemaEnvelopeVersion").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("version").GetInt32().Should().Be(template.Version);
        doc.RootElement.GetProperty("fields").EnumerateArray()
            .Select(f => f.GetProperty("key").GetString())
            .Should().Contain("field1");
        // AddVersionAsync MUST be called before UpdateAsync so SaveChanges flushes both in one tx.
        Received.InOrder(() =>
        {
            _repository.AddVersionAsync(Arg.Any<FormTemplateVersion>(), Arg.Any<CancellationToken>());
            _repository.UpdateAsync(Arg.Any<FormTemplate>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_DraftTableTemplate_AlsoStagesSnapshot()
    {
        var template = CreateDraftTemplate(StorageMode.Table);
        SetupTemplate(template);

        await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        await _repository.Received(1).AddVersionAsync(
            Arg.Is<FormTemplateVersion>(v =>
                v.FormTemplateId == template.Id && v.VersionNumber == template.Version),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TemplateWithMultipleFields_SnapshotIncludesAllActiveFields()
    {
        var template = CreateDraftTemplate(StorageMode.Json);
        template.AddField("field2", FormFieldType.Text, "Email", "Email", 2, false, null, TestUserId);
        template.AddField("field3", FormFieldType.Number, "SL", "Qty", 3, true, null, TestUserId);
        SetupTemplate(template);

        FormTemplateVersion? captured = null;
        await _repository.AddVersionAsync(
            Arg.Do<FormTemplateVersion>(v => captured = v), Arg.Any<CancellationToken>());

        await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.SchemaJson.Should()
            .Contain("\"field1\"")
            .And.Contain("\"field2\"")
            .And.Contain("\"field3\"");
    }

    [Fact]
    public async Task Handle_Failure_DoesNotStageSnapshot()
    {
        var template = FormTemplate.Create(
            _guidGenerator, TestTenantId, "Empty", "Trống", "empty_form", StorageMode.Json, TestUserId);
        // No fields → Publish throws → handler returns Fail before snapshot stage.
        SetupTemplate(template);

        await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        await _repository.DidNotReceive().AddVersionAsync(
            Arg.Any<FormTemplateVersion>(), Arg.Any<CancellationToken>());
    }

    // --- Table mode: Draft → Materializing + enqueue ---

    [Fact]
    public async Task Handle_DraftTableTemplate_SetsMaterializingAndEnqueues()
    {
        var template = CreateDraftTemplate(StorageMode.Table);
        SetupTemplate(template);

        var result = await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        template.Status.Should().Be(FormTemplateStatus.Materializing);
        await _queue.Received(1).EnqueueAsync(template.Id, Arg.Any<CancellationToken>());
    }

    // --- Already-published guard (domain throws) ---

    [Fact]
    public async Task Handle_AlreadyActiveTemplate_ReturnsValidationError()
    {
        // Publish once to make it Active
        var template = CreateDraftTemplate(StorageMode.Json);
        template.Publish(TestUserId); // Draft → Active
        SetupTemplate(template);

        // Attempt to publish again — domain should throw InvalidFormSchemaException
        var result = await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Cross-tenant access denied ---

    [Fact]
    public async Task Handle_WrongTenant_ReturnsAccessDeniedError()
    {
        var otherTenantId = Guid.NewGuid();
        var template = FormTemplate.Create(
            _guidGenerator, otherTenantId, "Other", "Khác", "other_code", StorageMode.Json, TestUserId);
        template.AddField("f1", FormFieldType.Text, "L", "L", 1, false, null, TestUserId);

        // Repository returns the template but TenantId does not match TestTenantId
        _repository
            .GetByPublicIdWithFieldsAsync(template.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(template));

        var result = await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- No active fields guard (domain throws) ---

    [Fact]
    public async Task Handle_TemplateWithNoFields_ReturnsSchemaError()
    {
        // Template created with no fields — domain.Publish throws
        var template = FormTemplate.Create(
            _guidGenerator, TestTenantId, "Empty", "Trống", "empty_form", StorageMode.Json, TestUserId);
        SetupTemplate(template);

        var result = await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private FormTemplate CreateDraftTemplate(StorageMode mode)
    {
        var t = FormTemplate.Create(
            _guidGenerator, TestTenantId, "Template", "Biểu mẫu", "pub_template", mode, TestUserId);
        t.AddField("field1", FormFieldType.Text, "Tên", "Name", 1, true, null, TestUserId);
        return t;
    }

    private void SetupTemplate(FormTemplate template)
    {
        // FormTemplate is T2: GetByPublicIdWithFieldsAsync takes Guid publicId
        _repository
            .GetByPublicIdWithFieldsAsync(template.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(template));
    }

    private static PublishFormTemplateCommand BuildCommand(Guid templatePublicId) =>
        new(templatePublicId, TestTenantId, TestUserId);
}
