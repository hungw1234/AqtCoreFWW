using AqtCoreFW.Files.Application.Commands.CreateDocumentTemplate;
using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Files.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateDocumentTemplateCommandHandler.
/// Validates: success creation, persistence, DTO mapping, initial Draft status.
/// </summary>
public sealed class CreateDocumentTemplateCommandHandlerTests
{
    private readonly IRepository<DocumentTemplate, long> _repository;
    private readonly CreateDocumentTemplateCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();

    public CreateDocumentTemplateCommandHandlerTests()
    {
        _repository = Substitute.For<IRepository<DocumentTemplate, long>>();
        _handler = new CreateDocumentTemplateCommandHandler(_repository);
    }

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithDto()
    {
        var command = new CreateDocumentTemplateCommand(
            Name: "Invoice Template",
            Code: "INV-001",
            Description: "Standard invoice template",
            OutputFormat: DocumentOutputFormat.Pdf,
            TemplateContent: "<template>Invoice</template>",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Name.Should().Be("Invoice Template");
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsViaRepository()
    {
        var command = new CreateDocumentTemplateCommand(
            Name: "Report Template",
            Code: "RPT-001",
            Description: "Monthly report",
            OutputFormat: DocumentOutputFormat.Docx,
            TemplateContent: "<template>Report</template>",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<DocumentTemplate>(t => t.Name == "Report Template"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MapsAllProperties()
    {
        var command = new CreateDocumentTemplateCommand(
            Name: "Contract Template",
            Code: "CTR-001",
            Description: "Service agreement",
            OutputFormat: DocumentOutputFormat.Pdf,
            TemplateContent: "<contract>Terms</contract>",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Name.Should().Be("Contract Template");
        result.Value.Code.Should().Be("CTR-001");
        result.Value.Description.Should().Be("Service agreement");
        result.Value.OutputFormat.Should().Be(DocumentOutputFormat.Pdf);
        result.Value.TemplateContent.Should().Be("<contract>Terms</contract>");
        result.Value.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public async Task Handle_TemplateCreatedInDraftStatus()
    {
        var command = new CreateDocumentTemplateCommand(
            Name: "Letter Template",
            Code: "LTR-001",
            Description: null,
            OutputFormat: DocumentOutputFormat.Docx,
            TemplateContent: "<letter/>",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Status.Should().Be(DocumentTemplateStatus.Draft);
    }

    [Fact]
    public async Task Handle_WithoutDescription_CreatesSuccessfully()
    {
        var command = new CreateDocumentTemplateCommand(
            Name: "Simple Template",
            Code: "SIMP-001",
            Description: null,
            OutputFormat: DocumentOutputFormat.Html,
            TemplateContent: "<template/>",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Description.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DifferentOutputFormats()
    {
        var formats = new[] { DocumentOutputFormat.Pdf, DocumentOutputFormat.Docx, DocumentOutputFormat.Html };

        foreach (var format in formats)
        {
            var command = new CreateDocumentTemplateCommand(
                Name: $"Template-{format}",
                Code: $"CODE-{format}",
                Description: null,
                OutputFormat: format,
                TemplateContent: "<template/>",
                TenantId: TenantId,
                CreatedBy: CreatedBy);

            var result = await _handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.OutputFormat.Should().Be(format);
        }
    }

    [Fact]
    public async Task Handle_SetsCreatedAtTimestamp()
    {
        var command = new CreateDocumentTemplateCommand(
            Name: "Timestamped Template",
            Code: "TS-001",
            Description: null,
            OutputFormat: DocumentOutputFormat.Pdf,
            TemplateContent: "<template/>",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        result.Value.CreatedBy.Should().Be(CreatedBy);
    }

    // --- Failure scenarios ---

    [Fact]
    public async Task Handle_RepositoryException_Propagates()
    {
        _repository
            .AddAsync(Arg.Any<DocumentTemplate>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB unavailable")));

        var command = new CreateDocumentTemplateCommand(
            Name: "Template",
            Code: "CODE",
            Description: null,
            OutputFormat: DocumentOutputFormat.Pdf,
            TemplateContent: "<template/>",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var act = async () => await _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB unavailable*");
    }

    [Fact]
    public async Task Handle_CancellationTokenRespected()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _repository
            .AddAsync(Arg.Any<DocumentTemplate>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromCanceled(cts.Token));

        var command = new CreateDocumentTemplateCommand(
            Name: "Template",
            Code: "CODE",
            Description: null,
            OutputFormat: DocumentOutputFormat.Pdf,
            TemplateContent: "<template/>",
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var act = async () => await _handler.Handle(command, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
