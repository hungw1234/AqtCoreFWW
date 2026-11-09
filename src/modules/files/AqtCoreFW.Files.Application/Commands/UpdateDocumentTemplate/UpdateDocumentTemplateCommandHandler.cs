using AqtCoreFW.Files.Application.Commands.CreateDocumentTemplate;
using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Commands.UpdateDocumentTemplate;

// Phase-4b B3b: DocumentTemplate is T1 — long PK, so IRepository<DocumentTemplate, long>.
public sealed class UpdateDocumentTemplateCommandHandler(
    IRepository<DocumentTemplate, long> templateRepository)
    : IRequestHandler<UpdateDocumentTemplateCommand, Result<DocumentTemplateDto>>
{
    public async Task<Result<DocumentTemplateDto>> Handle(
        UpdateDocumentTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var result = await templateRepository.GetByIdAsync(request.TemplateId, cancellationToken);
        if (result.IsFailed)
            return Result.Fail(new NotFoundError($"DocumentTemplate '{request.TemplateId}' not found."));

        var template = result.Value;

        template.Update(request.Name, request.Description, request.TemplateContent, request.ModifiedBy);
        await templateRepository.UpdateAsync(template, cancellationToken);

        return Result.Ok(CreateDocumentTemplateCommandHandler.MapToDto(template));
    }
}
