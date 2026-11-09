using AqtCoreFW.Files.Application.Commands.PublishDocumentTemplate;
using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Files.Application.Tests.Commands;

/// <summary>
/// Unit tests for PublishDocumentTemplateCommandHandler.
/// Validates: success publish, template not found, status transition, already published.
/// Phase-4b B3b: TemplateId is long (T1 entity — bigint identity PK, no PublicId AK).
/// </summary>
public sealed class PublishDocumentTemplateCommandHandlerTests
{
    private readonly IRepository<DocumentTemplate, long> _repository;
    private readonly PublishDocumentTemplateCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly long TemplateId = 42L;
    private static readonly Guid PublishedBy = Guid.NewGuid();

    public PublishDocumentTemplateCommandHandlerTests()
    {
        _repository = Substitute.For<IRepository<DocumentTemplate, long>>();
        _handler = new PublishDocumentTemplateCommandHandler(_repository);
    }

    // --- Success path ---

    [Fact]
    public async Task Handle_DraftTemplate_PublishesSuccessfully()
    {
        var template = DocumentTemplate.Create(
            "Template", "TMPL", null, DocumentOutputFormat.Pdf,
            "<content/>", TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(template));

        var command = new PublishDocumentTemplateCommand(
            TemplateId: TemplateId,
            PublishedBy: PublishedBy,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(DocumentTemplateStatus.Active);
    }

    [Fact]
    public async Task Handle_ValidCommand_UpdatesRepository()
    {
        var template = DocumentTemplate.Create(
            "Template", "TMPL", null, DocumentOutputFormat.Docx,
            "<content/>", TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(template));

        var command = new PublishDocumentTemplateCommand(
            TemplateId: TemplateId,
            PublishedBy: PublishedBy,
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).UpdateAsync(
            Arg.Is<DocumentTemplate>(t => t.Status == DocumentTemplateStatus.Active),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TransitionsDraftToActive()
    {
        var template = DocumentTemplate.Create(
            "Invoice", "INV", "Invoice template", DocumentOutputFormat.Pdf,
            "<invoice/>", TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(template));

        var command = new PublishDocumentTemplateCommand(
            TemplateId: TemplateId,
            PublishedBy: PublishedBy,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Status.Should().Be(DocumentTemplateStatus.Active);
        result.Value.Name.Should().Be("Invoice");
    }

    [Fact]
    public async Task Handle_PreservesTemplateContent()
    {
        var content = "<template><header/><body/></template>";
        var template = DocumentTemplate.Create(
            "Report", "RPT", "Report template", DocumentOutputFormat.Html,
            content, TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(template));

        var command = new PublishDocumentTemplateCommand(
            TemplateId: TemplateId,
            PublishedBy: PublishedBy,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.TemplateContent.Should().Be(content);
        result.Value.OutputFormat.Should().Be(DocumentOutputFormat.Html);
    }

    [Fact]
    public async Task Handle_RecordsPublishedMetadata()
    {
        var template = DocumentTemplate.Create(
            "Template", "TMPL", null, DocumentOutputFormat.Pdf,
            "<content/>", TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(template));

        var command = new PublishDocumentTemplateCommand(
            TemplateId: TemplateId,
            PublishedBy: PublishedBy,
            TenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // --- Failure scenarios ---

    [Fact]
    public async Task Handle_TemplateNotFound_ReturnsFailed()
    {
        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new NotFoundError("Template not found")));

        var command = new PublishDocumentTemplateCommand(
            TemplateId: TemplateId,
            PublishedBy: PublishedBy,
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

        var command = new PublishDocumentTemplateCommand(
            TemplateId: TemplateId,
            PublishedBy: PublishedBy,
            TenantId: TenantId);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.DidNotReceive().UpdateAsync(
            Arg.Any<DocumentTemplate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RepositoryException_Propagates()
    {
        var template = DocumentTemplate.Create(
            "Template", "TMPL", null, DocumentOutputFormat.Pdf,
            "<content/>", TenantId, Guid.NewGuid());

        _repository.GetByIdAsync(TemplateId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(template));

        _repository.UpdateAsync(Arg.Any<DocumentTemplate>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("Publish failed")));

        var command = new PublishDocumentTemplateCommand(
            TemplateId: TemplateId,
            PublishedBy: PublishedBy,
            TenantId: TenantId);

        var act = async () => await _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Publish failed*");
    }
}
