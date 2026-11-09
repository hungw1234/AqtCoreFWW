using AqtCoreFW.Files.Application.Commands.UpdateDocumentTemplate;
using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Files.Application.Tests.Commands;

/// <summary>
/// Unit tests for UpdateDocumentTemplateCommandHandler.
/// Validates: success update, template not found, DTO mapping.
/// Phase-4b B3b: TemplateId is long (T1 entity — bigint identity PK, no PublicId AK).
/// </summary>
public sealed class UpdateDocumentTemplateCommandHandlerTests
{
    private readonly IRepository<DocumentTemplate, long> _repository;
    private readonly UpdateDocumentTemplateCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly long TemplateId = 42L;
    private static readonly Guid ModifiedBy = Guid.NewGuid();

    public UpdateDocumentTemplateCommandHandlerTests()
    {
        _repository = Substitute.For<IRepository<DocumentTemplate, long>>();
        _handler = new UpdateDocumentTemplateCommandHandler(_repository);
    }

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccess()
    {
        var template = DocumentTemplate.Create(
            "Old Name", "OLD", "Old description", DocumentOutputFormat.Pdf,
            "<old/>", TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(template));

        var command = new UpdateDocumentTemplateCommand(
            TemplateId: TemplateId,
            Name: "New Name",
            Description: "New description",
            TemplateContent: "<new/>",
            ModifiedBy: ModifiedBy,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("New Name");
    }

    [Fact]
    public async Task Handle_ValidCommand_UpdatesRepository()
    {
        var template = DocumentTemplate.Create(
            "Name", "CODE", null, DocumentOutputFormat.Pdf,
            "<content/>", TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(template));

        var command = new UpdateDocumentTemplateCommand(
            TemplateId: TemplateId,
            Name: "Updated Name",
            Description: "Updated description",
            TemplateContent: "<updated/>",
            ModifiedBy: ModifiedBy,
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).UpdateAsync(
            Arg.Is<DocumentTemplate>(t => t.Name == "Updated Name"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UpdatesAllProperties()
    {
        var template = DocumentTemplate.Create(
            "Original", "ORIG", "Original desc", DocumentOutputFormat.Docx,
            "<orig/>", TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(template));

        var newName = "Modified Template";
        var newDescription = "Modified description";
        var newContent = "<modified/>";

        var command = new UpdateDocumentTemplateCommand(
            TemplateId: TemplateId,
            Name: newName,
            Description: newDescription,
            TemplateContent: newContent,
            ModifiedBy: ModifiedBy,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Name.Should().Be(newName);
        result.Value.Description.Should().Be(newDescription);
        result.Value.TemplateContent.Should().Be(newContent);
    }

    [Fact]
    public async Task Handle_WithoutDescription_UpdatesSuccessfully()
    {
        var template = DocumentTemplate.Create(
            "Name", "CODE", "Old desc", DocumentOutputFormat.Pdf,
            "<content/>", TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(template));

        var command = new UpdateDocumentTemplateCommand(
            TemplateId: TemplateId,
            Name: "Updated",
            Description: null,
            TemplateContent: "<updated/>",
            ModifiedBy: ModifiedBy,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Description.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PreservesTemplateMetadata()
    {
        var createdBy = Guid.NewGuid();
        var template = DocumentTemplate.Create(
            "Name", "CODE", "Description", DocumentOutputFormat.Html,
            "<content/>", TenantId, createdBy);

        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(template));

        var command = new UpdateDocumentTemplateCommand(
            TemplateId: TemplateId,
            Name: "New Name",
            Description: "New description",
            TemplateContent: "<new/>",
            ModifiedBy: ModifiedBy,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        // Code should remain unchanged (part of template identity)
        result.Value.Code.Should().Be("CODE");
        result.Value.OutputFormat.Should().Be(DocumentOutputFormat.Html);
    }

    // --- Failure scenarios ---

    [Fact]
    public async Task Handle_TemplateNotFound_ReturnsFailed()
    {
        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("Template not found")));

        var command = new UpdateDocumentTemplateCommand(
            TemplateId: TemplateId,
            Name: "Name",
            Description: null,
            TemplateContent: "<content/>",
            ModifiedBy: ModifiedBy,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    [Fact]
    public async Task Handle_TemplateNotFound_DoesNotCallUpdate()
    {
        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("Template not found")));

        var command = new UpdateDocumentTemplateCommand(
            TemplateId: TemplateId,
            Name: "Name",
            Description: null,
            TemplateContent: "<content/>",
            ModifiedBy: ModifiedBy,
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.DidNotReceive().UpdateAsync(
            Arg.Any<DocumentTemplate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RepositoryException_Propagates()
    {
        var template = DocumentTemplate.Create(
            "Name", "CODE", null, DocumentOutputFormat.Pdf,
            "<content/>", TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(template));

        _repository.UpdateAsync(Arg.Any<DocumentTemplate>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB error")));

        var command = new UpdateDocumentTemplateCommand(
            TemplateId: TemplateId,
            Name: "Name",
            Description: null,
            TemplateContent: "<content/>",
            ModifiedBy: ModifiedBy,
            TenantId: TenantId);

        var act = async () => await _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB error*");
    }
}
