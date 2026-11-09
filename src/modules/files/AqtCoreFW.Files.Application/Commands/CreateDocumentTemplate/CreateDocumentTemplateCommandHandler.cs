using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Commands.CreateDocumentTemplate;

// Phase-4b B3b: DocumentTemplate is T1 — long PK, so IRepository<DocumentTemplate, long>.
public sealed class CreateDocumentTemplateCommandHandler(
    IRepository<DocumentTemplate, long> repository)
    : IRequestHandler<CreateDocumentTemplateCommand, Result<DocumentTemplateDto>>
{
    public async Task<Result<DocumentTemplateDto>> Handle(
        CreateDocumentTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var template = DocumentTemplate.Create(
            request.Name,
            request.Code,
            request.Description,
            request.OutputFormat,
            request.TemplateContent,
            request.TenantId,
            request.CreatedBy);

        await repository.AddAsync(template, cancellationToken);

        return Result.Ok(MapToDto(template));
    }

    // Phase-4b B3b: T1 entity — Id is long (bigint identity PK). CreatedAt DateTime → DateTimeOffset at boundary.
    internal static DocumentTemplateDto MapToDto(DocumentTemplate t) => new(
        t.Id, t.Name, t.Code, t.Description, t.OutputFormat,
        t.TemplateContent, t.Status, t.TenantId,
        new DateTimeOffset(t.CreatedAt, TimeSpan.Zero), t.CreatedBy);
}
