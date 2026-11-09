using AqtCoreFW.Forms.Application.Commands.RepublishFormTemplate;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for RepublishFormTemplateCommandHandler (BP MVP P1-FU).
/// Verifies atomic snapshot staging on Active templates, cross-tenant guard, state-guard
/// pass-through from domain, and monotonic VersionNumbers on sequential republishes.
/// </summary>
public sealed class RepublishFormTemplateCommandHandlerTests
{
    private readonly IFormTemplateRepository _repository;
    private readonly IGuidGenerator          _guidGenerator;
    private readonly RepublishFormTemplateCommandHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId   = Guid.NewGuid();

    public RepublishFormTemplateCommandHandlerTests()
    {
        _repository    = Substitute.For<IFormTemplateRepository>();
        _guidGenerator = Substitute.For<IGuidGenerator>();
        _guidGenerator.NewIdMonotonic().Returns(_ => Guid.NewGuid());

        _handler = new RepublishFormTemplateCommandHandler(
            _repository, NullLogger<RepublishFormTemplateCommandHandler>.Instance);
    }

    // --- Not found ---

    [Fact]
    public async Task Handle_TemplateNotFound_ReturnsError()
    {
        _repository
            .GetByPublicIdWithFieldsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(null));

        var result = await _handler.Handle(BuildCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _repository.DidNotReceive().AddVersionAsync(
            Arg.Any<FormTemplateVersion>(), Arg.Any<CancellationToken>());
    }

    // --- Happy path: atomicity ---

    [Fact]
    public async Task Handle_ActiveTemplate_StagesSnapshotAtomicallyWithUpdate()
    {
        var template = CreateActiveTemplate();
        var versionBefore = template.Version;
        SetupTemplate(template);

        FormTemplateVersion? captured = null;
        await _repository.AddVersionAsync(
            Arg.Do<FormTemplateVersion>(v => captured = v), Arg.Any<CancellationToken>());

        var result = await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        template.Version.Should().Be(versionBefore + 1);
        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(template.TenantId);
        captured.FormTemplateId.Should().Be(template.Id);
        // VersionNumber is the POST-bump value, not the pre-bump.
        captured.VersionNumber.Should().Be(template.Version);
        captured.VersionNumber.Should().Be(versionBefore + 1);
        // AddVersionAsync MUST be called before UpdateAsync so SaveChanges flushes both in one tx.
        Received.InOrder(() =>
        {
            _repository.AddVersionAsync(Arg.Any<FormTemplateVersion>(), Arg.Any<CancellationToken>());
            _repository.UpdateAsync(Arg.Any<FormTemplate>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_ActiveTemplate_SnapshotIncludesAllActiveFields()
    {
        var template = CreateActiveTemplate();
        template.AddField("f2", FormFieldType.Text, "F2", "F2", 2, false, null, TestUserId);
        template.AddField("f3", FormFieldType.Number, "Qty", "Qty", 3, true, null, TestUserId);
        SetupTemplate(template);

        FormTemplateVersion? captured = null;
        await _repository.AddVersionAsync(
            Arg.Do<FormTemplateVersion>(v => captured = v), Arg.Any<CancellationToken>());

        await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.SchemaJson.Should()
            .Contain("\"field1\"")
            .And.Contain("\"f2\"")
            .And.Contain("\"f3\"");
        var doc = System.Text.Json.JsonDocument.Parse(captured.SchemaJson);
        doc.RootElement.GetProperty("version").GetInt32().Should().Be(template.Version);
    }

    [Fact]
    public async Task Handle_TwoSequentialRepublish_ProduceDistinctVersionNumbers()
    {
        // Critical: zero schema changes between calls; if domain didn't bump Version on the
        // second call we'd get a duplicate VersionNumber and the (FormTemplateId, VersionNumber)
        // unique index would fail at SaveChanges. Tested at the handler boundary as a regression
        // guard against future "skip bump if no field changes" optimizations.
        var template = CreateActiveTemplate();
        SetupTemplate(template);

        var captured = new List<FormTemplateVersion>();
        await _repository.AddVersionAsync(
            Arg.Do<FormTemplateVersion>(v => captured.Add(v)), Arg.Any<CancellationToken>());

        var r1 = await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);
        var r2 = await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        r1.IsSuccess.Should().BeTrue();
        r2.IsSuccess.Should().BeTrue();
        captured.Should().HaveCount(2);
        captured[0].VersionNumber.Should().NotBe(captured[1].VersionNumber);
        captured[1].VersionNumber.Should().Be(captured[0].VersionNumber + 1);
    }

    // --- State guards (domain throws → handler returns ValidationError) ---

    [Fact]
    public async Task Handle_DraftTemplate_ReturnsValidationError()
    {
        var template = FormTemplate.Create(
            _guidGenerator, TestTenantId, "T", "T", "draft_code", StorageMode.Json, TestUserId);
        template.AddField("f1", FormFieldType.Text, "Tên", "Name", 1, true, null, TestUserId);
        // Skip Publish → stays Draft
        SetupTemplate(template);

        var result = await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _repository.DidNotReceive().AddVersionAsync(
            Arg.Any<FormTemplateVersion>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().UpdateAsync(
            Arg.Any<FormTemplate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MaterializingTemplate_ReturnsValidationError()
    {
        var template = FormTemplate.Create(
            _guidGenerator, TestTenantId, "T", "T", "mat_code", StorageMode.Table, TestUserId);
        template.AddField("f1", FormFieldType.Text, "Tên", "Name", 1, true, null, TestUserId);
        template.Publish(TestUserId); // Table → Materializing
        SetupTemplate(template);

        var result = await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _repository.DidNotReceive().AddVersionAsync(
            Arg.Any<FormTemplateVersion>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InactiveTemplate_ReturnsValidationError()
    {
        var template = CreateActiveTemplate();
        template.Deactivate(TestUserId);
        SetupTemplate(template);

        var result = await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _repository.DidNotReceive().AddVersionAsync(
            Arg.Any<FormTemplateVersion>(), Arg.Any<CancellationToken>());
    }

    // --- Cross-tenant guard ---

    [Fact]
    public async Task Handle_WrongTenant_ReturnsAccessDeniedError()
    {
        var otherTenantId = Guid.NewGuid();
        var template = FormTemplate.Create(
            _guidGenerator, otherTenantId, "Other", "Khác", "other_code", StorageMode.Json, TestUserId);
        template.AddField("f1", FormFieldType.Text, "L", "L", 1, false, null, TestUserId);
        template.Publish(TestUserId);

        _repository
            .GetByPublicIdWithFieldsAsync(template.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(template));

        var result = await _handler.Handle(BuildCommand(template.PublicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _repository.DidNotReceive().AddVersionAsync(
            Arg.Any<FormTemplateVersion>(), Arg.Any<CancellationToken>());
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private FormTemplate CreateActiveTemplate()
    {
        var t = FormTemplate.Create(
            _guidGenerator, TestTenantId, "Template", "Biểu mẫu", "pub_template", StorageMode.Json, TestUserId);
        t.AddField("field1", FormFieldType.Text, "Tên", "Name", 1, true, null, TestUserId);
        t.Publish(TestUserId); // Draft → Active
        return t;
    }

    private void SetupTemplate(FormTemplate template)
    {
        _repository
            .GetByPublicIdWithFieldsAsync(template.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(template));
    }

    private static RepublishFormTemplateCommand BuildCommand(Guid templatePublicId) =>
        new(templatePublicId, TestTenantId, TestUserId);
}
