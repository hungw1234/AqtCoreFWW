using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Exceptions;
using AqtCoreFW.Forms.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.DeactivateFormField;

public sealed class DeactivateFormFieldCommandHandler(IFormTemplateRepository templateRepository)
    : IRequestHandler<DeactivateFormFieldCommand, Result>
{
    public async Task<Result> Handle(
        DeactivateFormFieldCommand request,
        CancellationToken cancellationToken)
    {
        var template = await templateRepository.GetByPublicIdWithFieldsAsync(
            request.TemplatePublicId, request.TenantId, cancellationToken);

        if (template is null)
            return Result.Fail(FormErrors.TemplateNotFound(request.TemplatePublicId));

        try
        {
            template.DeactivateField(request.FieldId, request.UpdatedBy);
        }
        catch (InvalidFormSchemaException ex)
        {
            return Result.Fail(new SharedKernel.Errors.ValidationError(ex.Message, "FieldId"));
        }

        await templateRepository.UpdateAsync(template, cancellationToken);
        return Result.Ok();
    }
}
