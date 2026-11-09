using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Exceptions;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.UpdateFormTemplateCode;

public sealed class UpdateFormTemplateCodeCommandHandler(
    IFormTemplateRepository templateRepository,
    IReadDbConnection db)
    : IRequestHandler<UpdateFormTemplateCodeCommand, Result>
{
    // A public (IsPublic=1) template in ANOTHER tenant already owning the new Code would make the
    // anonymous by-Code resolution ambiguous — the UX_FormTemplate_PublicCode filtered unique index
    // rejects it with a raw duplicate-key error. Predicate mirrors that index exactly. The probe runs
    // on a public_forms_bypass connection (the SAME mechanism the resolution uses) because the
    // FormTemplates RLS FILTER hides other tenants' rows from this admin's tenant-pinned context under
    // a least-privilege login; only the bypass flag reveals the cross-tenant IsPublic=1 row.
    private const string PublicCodeTakenSql = """
        SELECT TOP 1 1 FROM forms.FormTemplates
        WHERE Code = @Code AND IsPublic = 1 AND IsDeleted = 0 AND Id <> @ExcludeId
        """;

    public async Task<Result> Handle(
        UpdateFormTemplateCodeCommand request,
        CancellationToken cancellationToken)
    {
        var template = await templateRepository.GetByPublicIdWithFieldsAsync(
            request.TemplatePublicId, request.TenantId, cancellationToken);

        if (template is null)
            return Result.Fail(FormErrors.TemplateNotFound(request.TemplatePublicId));

        // Enforce Code uniqueness per tenant (excluding self)
        var existing = await templateRepository.GetByCodeAsync(
            request.NewCode, request.TenantId, cancellationToken);

        if (existing is not null && existing.Id != template.Id)
            return Result.Fail(FormErrors.DuplicateTemplateCode(request.NewCode));

        // Only a public template's rename can collide the global public-Code index (it filters
        // WHERE IsPublic=1) — a private template stays out of it. And SetCode below is Draft-only, so a
        // non-Draft template never reaches the UPDATE that could trip the index (it fails GOV_FRM_010
        // first). Gating on BOTH conditions makes the guard fire exactly when a raw index-500 is
        // reachable: no false 409 for private or non-Draft templates, no wasted bypass connection. The
        // index remains the DB backstop that also closes the probe→write race.
        if (template.IsPublic && template.Status == FormTemplateStatus.Draft)
        {
            var taken = await db.QueryFirstOrDefaultWithPublicFormsBypassAsync<int?>(
                PublicCodeTakenSql, new { Code = request.NewCode, ExcludeId = template.Id }, cancellationToken);
            if (taken is not null)
                return Result.Fail(FormErrors.PublicCodeConflict(request.NewCode));
        }

        try
        {
            template.SetCode(request.NewCode, request.UpdatedBy);
        }
        catch (InvalidFormSchemaException ex)
        {
            return Result.Fail(new SharedKernel.Errors.ValidationError(ex.Message, "Code"));
        }

        await templateRepository.UpdateAsync(template, cancellationToken);
        return Result.Ok();
    }
}
