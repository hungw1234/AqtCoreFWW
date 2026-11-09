using AqtCoreFW.Forms.Application.Commands.CreateFormTemplate;
using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.UpdateFormField;

/// <summary>
/// Updates mutable field properties (labels, required, validation, dataSource, formula, isPii).
/// FieldKey and Type are immutable — not accepted in this command.
/// Template must be in Draft status.
/// </summary>
public sealed class UpdateFormFieldCommandHandler(IFormTemplateRepository templateRepository)
    : IRequestHandler<UpdateFormFieldCommand, Result<FormFieldDto>>
{
    public async Task<Result<FormFieldDto>> Handle(
        UpdateFormFieldCommand request,
        CancellationToken cancellationToken)
    {
        var template = await templateRepository.GetByPublicIdWithFieldsAsync(
            request.TemplatePublicId, request.TenantId, cancellationToken);

        if (template is null)
            return Result.Fail(FormErrors.TemplateNotFound(request.TemplatePublicId));

        var field = template.Fields.FirstOrDefault(f => f.Id == request.FieldId && f.IsActive);
        if (field is null)
            return Result.Fail(FormErrors.FieldNotFound(request.FieldId));

        // Apply only provided (non-null) patches
        if (request.LabelVi is not null || request.LabelEn is not null)
        {
            field.UpdateLabels(
                request.LabelVi ?? field.LabelVi,
                request.LabelEn ?? field.LabelEn);
        }

        if (request.Required.HasValue)
            field.UpdateRequired(request.Required.Value);

        if (request.ValidationRulesJson is not null)
            field.UpdateValidationRules(request.ValidationRulesJson);

        if (request.DataSourceJson is not null)
            field.UpdateDataSource(request.DataSourceJson);

        if (request.FormulaJson is not null)
            field.UpdateFormula(request.FormulaJson);

        if (request.IsPii.HasValue)
            field.SetPii(request.IsPii.Value);

        if (request.VisibilityRulesJson is not null)
            field.UpdateVisibilityRules(request.VisibilityRulesJson);

        if (request.RequiredIfJson is not null)
            field.UpdateRequiredIf(request.RequiredIfJson);

        if (request.PrefillSourceJson is not null)
            field.UpdatePrefillSource(request.PrefillSourceJson);

        template.SetAuditUpdate(request.UpdatedBy);
        await templateRepository.UpdateAsync(template, cancellationToken);

        return Result.Ok(CreateFormTemplateCommandHandler.MapField(field));
    }
}
