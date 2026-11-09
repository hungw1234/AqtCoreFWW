using AqtCoreFW.Forms.Application.Commands.CreateFormTemplate;
using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Exceptions;
using AqtCoreFW.Forms.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.AddFormField;

public sealed class AddFormFieldCommandHandler(
    IFormTemplateRepository templateRepository,
    IFormTableMigrator tableMigrator)
    : IRequestHandler<AddFormFieldCommand, Result<FormFieldDto>>
{
    public async Task<Result<FormFieldDto>> Handle(
        AddFormFieldCommand request,
        CancellationToken cancellationToken)
    {
        // Load template with fields for domain validation (resolved via public Guid AK)
        var template = await templateRepository.GetByPublicIdWithFieldsAsync(
            request.TemplatePublicId, request.TenantId, cancellationToken);

        if (template is null)
            return Result.Fail(FormErrors.TemplateNotFound(request.TemplatePublicId));

        FormField field;
        try
        {
            field = template.AddField(
                request.FieldKey,
                request.Type,
                request.LabelVi,
                request.LabelEn,
                request.DisplayOrder,
                request.Required,
                request.ValidationRulesJson,
                request.UpdatedBy,
                request.DataSourceJson,
                request.FormulaJson,
                request.ParentFieldId,
                request.VisibilityRulesJson,
                prefillSourceJson: request.PrefillSourceJson);
        }
        catch (InvalidFormSchemaException ex)
        {
            return Result.Fail(new SharedKernel.Errors.ValidationError(ex.Message, "FieldKey"));
        }

        // Add static options if provided
        if (request.Options is { Count: > 0 })
        {
            foreach (var opt in request.Options)
            {
                var option = FormFieldOption.Create(
                    field.Id, opt.Value, opt.LabelVi, opt.LabelEn, opt.DisplayOrder);
                field.AddOption(option);
            }
        }

        // If template is Active + Table mode, ALTER the appropriate table to add the new column
        if (template.Status == FormTemplateStatus.Active
            && template.StorageMode == StorageMode.Table)
        {
            if (field.ParentFieldId.HasValue)
            {
                // Child field → ALTER the child table, not the parent
                await tableMigrator.AddChildColumnAsync(
                    template.Id, field.ParentFieldId.Value, field, cancellationToken);
            }
            else
            {
                await tableMigrator.AddColumnAsync(template.Id, field, cancellationToken);
            }
        }

        await templateRepository.UpdateAsync(template, cancellationToken);
        return Result.Ok(CreateFormTemplateCommandHandler.MapField(field));
    }
}
