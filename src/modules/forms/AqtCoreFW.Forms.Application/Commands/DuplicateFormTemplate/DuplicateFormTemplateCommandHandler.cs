using AqtCoreFW.Forms.Application.Commands.CreateFormTemplate;
using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Exceptions;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.DuplicateFormTemplate;

/// <summary>
/// Loads source template (with fields + options), creates a new Draft clone.
/// All active fields are copied with new IDs; options are copied per field.
/// Parent-child field relationships (TableField children) are preserved by
/// mapping old parentFieldId → new field id before adding children.
/// idMap keys/values are long (T1 FormField.Id, DB-assigned).
/// </summary>
public sealed class DuplicateFormTemplateCommandHandler(
    IFormTemplateRepository repository,
    IGuidGenerator guidGenerator)
    : IRequestHandler<DuplicateFormTemplateCommand, Result<FormTemplateDto>>
{
    public async Task<Result<FormTemplateDto>> Handle(
        DuplicateFormTemplateCommand request,
        CancellationToken cancellationToken)
    {
        // Verify source template exists in this tenant
        var source = await repository.GetByPublicIdWithFieldsAsync(
            request.SourcePublicId, request.TenantId, cancellationToken);

        if (source is null)
            return Result.Fail(FormErrors.TemplateNotFound(request.SourcePublicId));

        // Ensure new code is unique within this tenant
        var existing = await repository.GetByCodeAsync(request.NewCode, request.TenantId, cancellationToken);
        if (existing is not null)
            return Result.Fail(FormErrors.DuplicateTemplateCode(request.NewCode));

        // Create new Draft template with the same StorageMode
        FormTemplate clone;
        try
        {
            clone = FormTemplate.Create(
                guidGenerator,
                request.TenantId,
                request.NewName,
                request.NewName,   // NameVi same as Name — caller can update after
                request.NewCode,
                source.StorageMode,
                request.CreatedBy);
        }
        catch (InvalidFormSchemaException ex)
        {
            return Result.Fail(new SharedKernel.Errors.ValidationError(ex.Message, "Code"));
        }

        // Copy active top-level fields first, track old→new ID mapping for child resolution.
        // Keys are long (T1 FormField.Id, DB-assigned). New IDs are 0 pre-save — EF assigns on commit.
        var activeFields = source.Fields.Where(f => f.IsActive).ToList();
        var idMap = new Dictionary<long, long>(); // oldFieldId → newFieldId

        var topLevel = activeFields.Where(f => f.ParentFieldId is null).ToList();
        foreach (var src in topLevel)
        {
            var newField = clone.AddField(
                src.FieldKey, src.Type,
                src.LabelVi, src.LabelEn,
                src.DisplayOrder, src.Required,
                src.ValidationRulesJson,
                request.CreatedBy,
                src.DataSourceJson,
                src.FormulaJson,
                parentFieldId: null,
                src.VisibilityRulesJson,
                isPii: src.IsPii);

            idMap[src.Id] = newField.Id;

            // Copy options (EnumRef etc.)
            foreach (var opt in src.Options)
            {
                newField.AddOption(FormFieldOption.Create(
                    newField.Id, opt.Value, opt.LabelVi, opt.LabelEn, opt.DisplayOrder));
            }
        }

        // Copy child fields (TableField children), resolving parent IDs via map
        var children = activeFields.Where(f => f.ParentFieldId.HasValue).ToList();
        foreach (var src in children)
        {
            if (!idMap.TryGetValue(src.ParentFieldId!.Value, out var newParentId))
                continue; // parent was inactive — skip child too

            var newField = clone.AddField(
                src.FieldKey, src.Type,
                src.LabelVi, src.LabelEn,
                src.DisplayOrder, src.Required,
                src.ValidationRulesJson,
                request.CreatedBy,
                src.DataSourceJson,
                src.FormulaJson,
                parentFieldId: newParentId,
                src.VisibilityRulesJson,
                isPii: src.IsPii);

            idMap[src.Id] = newField.Id;

            foreach (var opt in src.Options)
            {
                newField.AddOption(FormFieldOption.Create(
                    newField.Id, opt.Value, opt.LabelVi, opt.LabelEn, opt.DisplayOrder));
            }
        }

        // Copy signer roles — GetByPublicIdWithFieldsAsync already includes SignerRoles.
        // Without this loop a multi-signer template silently degrades to single-signer after duplication.
        foreach (var srcRole in source.SignerRoles)
        {
            clone.AddSignerRole(
                srcRole.RoleKey,
                srcRole.RoleLabelVi,
                srcRole.DisplayOrder,
                srcRole.IsRequired,
                srcRole.PlacementJson,
                request.CreatedBy);
        }

        await repository.AddAsync(clone, cancellationToken);
        return Result.Ok(CreateFormTemplateCommandHandler.MapToDto(clone));
    }
}
