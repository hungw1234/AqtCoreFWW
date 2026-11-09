using AqtCoreFW.Files.Application.Commands.CreateDocumentTemplate;
using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Commands.PublishDocumentTemplate;

// Phase-4b B3b: DocumentTemplate is T1 — long PK, so IRepository<DocumentTemplate, long>.
public sealed class PublishDocumentTemplateCommandHandler(
    IRepository<DocumentTemplate, long> repository)
    : IRequestHandler<PublishDocumentTemplateCommand, Result<DocumentTemplateDto>>
{
    public async Task<Result<DocumentTemplateDto>> Handle(
        PublishDocumentTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetByIdAsync(request.TemplateId, cancellationToken);
        if (result.IsFailed)
            return Result.Fail(new NotFoundError($"DocumentTemplate '{request.TemplateId}' not found."));

        var template = result.Value;
        template.Publish(request.PublishedBy);
        await repository.UpdateAsync(template, cancellationToken);

        return Result.Ok(CreateDocumentTemplateCommandHandler.MapToDto(template));
    }
}
