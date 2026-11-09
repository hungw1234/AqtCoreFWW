using AqtCoreFW.Forms.Application.Commands.DuplicateFormTemplate;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for DuplicateFormTemplateCommandHandler.
/// Covers field copying, signer-role copying, code-uniqueness guard, and not-found guard.
/// </summary>
public sealed class DuplicateFormTemplateCommandHandlerTests
{
    private readonly IFormTemplateRepository _repository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly DuplicateFormTemplateCommandHandler _handler;

    private static readonly Guid TenantId  = Guid.NewGuid();
    private static readonly Guid ActorId   = Guid.NewGuid();
    private static readonly Guid SourceId  = Guid.NewGuid();

    public DuplicateFormTemplateCommandHandlerTests()
    {
        _repository   = Substitute.For<IFormTemplateRepository>();
        _guidGenerator = Substitute.For<IGuidGenerator>();
        _guidGenerator.NewIdMonotonic().Returns(_ => Guid.NewGuid());

        // Default: new code is unique
        _repository
            .GetByCodeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(null));

        _handler = new DuplicateFormTemplateCommandHandler(_repository, _guidGenerator);
    }

    // --- Not-found guard ---

    [Fact]
    public async Task Handle_SourceTemplateNotFound_ReturnsFailure()
    {
        _repository
            .GetByPublicIdWithFieldsAsync(SourceId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(null));

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        // TemplateNotFound message: "FormTemplate {guid} không tìm thấy." — publicId is in the message
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains(SourceId.ToString()) || e.Message.Contains("không tìm thấy"));
    }

    // --- Code-uniqueness guard ---

    [Fact]
    public async Task Handle_DuplicateNewCode_ReturnsFailure()
    {
        var source = CreateDraftSource();
        _repository
            .GetByPublicIdWithFieldsAsync(SourceId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(source));

        var clash = FormTemplate.Create(_guidGenerator, TenantId, "X", "X", "new_code", StorageMode.Json, ActorId);
        _repository
            .GetByCodeAsync("new_code", TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(clash));

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        // DuplicateTemplateCode message: "Code biểu mẫu 'new_code' đã tồn tại trong tenant này."
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("new_code") || e.Message.Contains("đã tồn tại"));
    }

    // --- Happy path: fields copied ---

    [Fact]
    public async Task Handle_SourceWithFields_ClonesFields()
    {
        var source = CreateDraftSource();
        source.AddField("f1", FormFieldType.Text, "F1", "F1", 1, true, null, ActorId);
        source.AddField("f2", FormFieldType.Number, "F2", "F2", 2, false, null, ActorId);

        _repository
            .GetByPublicIdWithFieldsAsync(SourceId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(source));

        FormTemplate? persisted = null;
        await _repository.AddAsync(
            Arg.Do<FormTemplate>(t => persisted = t),
            Arg.Any<CancellationToken>());

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        persisted.Should().NotBeNull();
        persisted!.Fields.Should().HaveCount(2);
        persisted.Fields.Select(f => f.FieldKey).Should().BeEquivalentTo(["f1", "f2"]);
    }

    // --- M1: signer roles copied ---

    [Fact]
    public async Task Duplicate_TemplateWithSignerRoles_ClonesRoles()
    {
        var source = CreateDraftSource();
        source.AddSignerRole("approver", "Người phê duyệt", displayOrder: 1, isRequired: true,  placementJson: "{\"x\":10}", ActorId);
        source.AddSignerRole("witness",  "Nhân chứng",      displayOrder: 2, isRequired: false, placementJson: null,       ActorId);

        _repository
            .GetByPublicIdWithFieldsAsync(SourceId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(source));

        FormTemplate? persisted = null;
        await _repository.AddAsync(
            Arg.Do<FormTemplate>(t => persisted = t),
            Arg.Any<CancellationToken>());

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        persisted.Should().NotBeNull();
        persisted!.SignerRoles.Should().HaveCount(2);
        persisted.SignerRoles.Should().Satisfy(
            r => r.RoleKey == "approver" && r.DisplayOrder == 1 && r.IsRequired  && r.PlacementJson == "{\"x\":10}",
            r => r.RoleKey == "witness"  && r.DisplayOrder == 2 && !r.IsRequired && r.PlacementJson == null);
    }

    [Fact]
    public async Task Duplicate_TemplateWithNoSignerRoles_ClonesWithNoRoles()
    {
        var source = CreateDraftSource();
        source.AddField("f1", FormFieldType.Text, "F1", "F1", 1, true, null, ActorId);

        _repository
            .GetByPublicIdWithFieldsAsync(SourceId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(source));

        FormTemplate? persisted = null;
        await _repository.AddAsync(
            Arg.Do<FormTemplate>(t => persisted = t),
            Arg.Any<CancellationToken>());

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        persisted!.SignerRoles.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_TemplateWithFieldsAndSignerRoles_ClonesAll()
    {
        var source = CreateDraftSource();
        source.AddField("f1", FormFieldType.Text, "F1", "F1", 1, true, null, ActorId);
        source.AddSignerRole("approver", "Người phê duyệt", displayOrder: 1, isRequired: true, placementJson: null, ActorId);

        _repository
            .GetByPublicIdWithFieldsAsync(SourceId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(source));

        FormTemplate? persisted = null;
        await _repository.AddAsync(
            Arg.Do<FormTemplate>(t => persisted = t),
            Arg.Any<CancellationToken>());

        await _handler.Handle(BuildCommand(), CancellationToken.None);

        persisted!.Fields.Should().HaveCount(1);
        persisted.SignerRoles.Should().HaveCount(1);
        persisted.SignerRoles.Single().RoleKey.Should().Be("approver");
    }

    // --- Helpers ---

    private FormTemplate CreateDraftSource()
        => FormTemplate.Create(_guidGenerator, TenantId, "Source", "Source VI", "source_code", StorageMode.Json, ActorId);

    private DuplicateFormTemplateCommand BuildCommand()
        => new(SourceId, TenantId, "Clone", "new_code", ActorId);
}
