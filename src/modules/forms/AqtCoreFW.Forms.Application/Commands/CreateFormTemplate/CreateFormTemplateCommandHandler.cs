using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Exceptions;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.CreateFormTemplate;

public sealed class CreateFormTemplateCommandHandler(
    IFormTemplateRepository repository,
    IGuidGenerator guidGenerator)
    : IRequestHandler<CreateFormTemplateCommand, Result<FormTemplateDto>>
{
    public async Task<Result<FormTemplateDto>> Handle(
        CreateFormTemplateCommand request,
        CancellationToken cancellationToken)
    {
        // Enforce Code uniqueness per tenant
        var existing = await repository.GetByCodeAsync(request.Code, request.TenantId, cancellationToken);
        if (existing is not null)
            return Result.Fail(FormErrors.DuplicateTemplateCode(request.Code));

        try
        {
            var template = FormTemplate.Create(
                guidGenerator,
                request.TenantId,
                request.Name,
                request.NameVi,
                request.Code,
                request.StorageMode,
                request.CreatedBy);

            if (request.RequiresConsent || request.ConsentText is not null)
                template.SetConsentConfig(request.RequiresConsent, request.ConsentText, request.CreatedBy);

            if (request.ApprovalWorkflowDefinitionId.HasValue)
                template.SetApprovalWorkflow(request.ApprovalWorkflowDefinitionId, request.CreatedBy);

            await repository.AddAsync(template, cancellationToken);
            return Result.Ok(MapToDto(template));
        }
        catch (InvalidFormSchemaException ex)
        {
            return Result.Fail(new SharedKernel.Errors.ValidationError(ex.Message, "Code"));
        }
    }

    internal static FormTemplateDto MapToDto(FormTemplate t) =>
        new(
            t.PublicId, t.TenantId, t.Name, t.NameVi,
            t.Code, t.StorageMode.ToString(), t.Status.ToString(),
            t.Version, t.CreatedAt,
            t.Fields.Select(MapField).ToList(),
            t.RequiresConsent, t.ConsentText, t.ApprovalWorkflowDefinitionId);

    internal static FormFieldDto MapField(FormField f) =>
        new(
            f.Id, f.FieldKey, f.Type.ToString(),
            f.LabelVi, f.LabelEn,
            f.DisplayOrder, f.Required, f.IsActive, f.AddedInVersion,
            ParseValidationRules(f.ValidationRulesJson),
            f.Options.Select(o => new FormFieldOptionDto(
                o.Id, o.Value, o.LabelVi, o.LabelEn, o.DisplayOrder)).ToList(),
            f.VisibilityRulesJson,
            f.RequiredIfJson,
            f.PrefillSourceJson);

    private static FormFieldValidationRulesDto? ParseValidationRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<FormFieldValidationRulesDto>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}
