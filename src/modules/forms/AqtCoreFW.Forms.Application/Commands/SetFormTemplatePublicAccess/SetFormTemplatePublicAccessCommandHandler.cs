using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.SetFormTemplatePublicAccess;

public sealed class SetFormTemplatePublicAccessCommandHandler(
    IFormTemplateRepository repository,
    IReadDbConnection db)
    : IRequestHandler<SetFormTemplatePublicAccessCommand, Result>
{
    // Another IsPublic=1 template (ANY tenant) already owning this Code makes the anonymous by-Code
    // resolution ambiguous. The probe runs on a public_forms_bypass connection — the SAME mechanism the
    // resolution uses — because the FormTemplates RLS FILTER hides other tenants' rows from this admin's
    // tenant-pinned context under a least-privilege login; only the bypass flag reveals cross-tenant
    // IsPublic=1 rows. Predicate mirrors the UX_FormTemplate_PublicCode filtered unique index exactly.
    private const string PublicCodeTakenSql = """
        SELECT TOP 1 1 FROM forms.FormTemplates
        WHERE Code = @Code AND IsPublic = 1 AND IsDeleted = 0 AND Id <> @ExcludeId
        """;

    public async Task<Result> Handle(SetFormTemplatePublicAccessCommand cmd, CancellationToken ct)
    {
        // Tenant-scoped load: an admin can only flip a template inside their own tenant.
        var template = await repository.GetByPublicIdAsync(cmd.PublicId, cmd.TenantId, ct);
        if (template is null)
            return Result.Fail(new NotFoundError($"Form template {cmd.PublicId} not found."));

        // Enabling public access must not create a globally-ambiguous public Code. Disabling is never
        // blocked. The filtered unique index is the DB backstop (and closes the probe→write race); this
        // pre-check turns its raw duplicate-key error into a friendly, localized 409.
        if (cmd.IsPublic)
        {
            var taken = await db.QueryFirstOrDefaultWithPublicFormsBypassAsync<int?>(
                PublicCodeTakenSql, new { template.Code, ExcludeId = template.Id }, ct);
            if (taken is not null)
                return Result.Fail(FormErrors.PublicCodeConflict(template.Code));
        }

        template.SetPublicAccess(cmd.IsPublic, cmd.UpdatedBy);

        await repository.UpdateAsync(template, ct);
        return Result.Ok();
    }
}
