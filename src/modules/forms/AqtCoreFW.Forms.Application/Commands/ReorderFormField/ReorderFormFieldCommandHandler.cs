using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.ReorderFormField;

public sealed class ReorderFormFieldCommandHandler(
    IFormFieldRepository fieldRepository,
    IFormTemplateRepository templateRepository)
    : IRequestHandler<ReorderFormFieldCommand, Result>
{
    public async Task<Result> Handle(ReorderFormFieldCommand request, CancellationToken cancellationToken)
    {
        // Resolve public Guid AK → internal long Id for field ownership check
        var template = await templateRepository.GetByPublicIdAsync(
            request.TemplatePublicId, request.TenantId, cancellationToken);
        if (template is null)
            return Result.Fail(FormErrors.TemplateNotFound(request.TemplatePublicId));

        var result = await fieldRepository.GetByIdAsync(request.FieldId, cancellationToken);
        if (result.IsFailed)
            return Result.Fail(FormErrors.FieldNotFound(request.FieldId));

        var field = result.Value;

        // Verify field belongs to the specified template (using internal Id)
        if (field.FormTemplateId != template.Id)
            return Result.Fail(FormErrors.FieldNotFound(request.FieldId));

        field.UpdateDisplayOrder(request.NewOrder);
        await fieldRepository.UpdateAsync(field, cancellationToken);
        return Result.Ok();
    }
}
