using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.GetPublicFormFieldOptions;

/// <summary>
/// C1 — resolves options for one field of a PUBLIC form on the anonymous surface. Same two-step
/// tenant-safe pattern as <c>EvaluatePublicFormulasQueryHandler</c>: (1) Dapper public_forms_bypass
/// id-lookup by Code (RLS carve-out admits only the IsPublic=1 row under a non-db_owner login),
/// (2) EF <c>GetPublicWithFieldsAsync</c> which re-pins SESSION_CONTEXT to the server-resolved tenant.
/// The field is then picked from the template's own Fields by FieldKey — so a field that does not
/// belong to this form's template is unreachable (closes red-team C1: no attacker-chosen fieldId can
/// address a cross-tenant field). Reuses the resolver path identically to the authed handler, so a
/// cascading field on a public form filters exactly as it does when authed.
/// </summary>
public sealed class GetPublicFormFieldOptionsQueryHandler(
    IReadDbConnection db,
    IFormTemplateRepository templateRepository,
    IDataSourceResolverFactory resolverFactory)
    : IRequestHandler<GetPublicFormFieldOptionsQuery, Result<IReadOnlyList<DataSourceOptionDto>>>
{
    private static readonly HashSet<FormFieldType> ReferenceTypes =
    [
        FormFieldType.EnumRef,
        FormFieldType.InternalRef,
        FormFieldType.ExternalRef
    ];

    public async Task<Result<IReadOnlyList<DataSourceOptionDto>>> Handle(
        GetPublicFormFieldOptionsQuery request,
        CancellationToken cancellationToken)
    {
        // Step 1: resolve Id + TenantId by Code via Dapper on a public_forms_bypass connection.
        // Anonymous caller has no ambient tenant → the bypass carve-out admits the IsPublic=1 row
        // under a non-db_owner login. IsPublic=1 is explicit in the WHERE (red-team trục-4: the
        // bypass flag must never surface a private template).
        const string idSql = """
            SELECT Id, TenantId FROM forms.FormTemplates
            WHERE Code = @Code AND Status = 'Active' AND IsDeleted = 0 AND IsPublic = 1
            """;

        var row = await db.QueryFirstOrDefaultWithPublicFormsBypassAsync<TemplateIdRow>(
            idSql, new { Code = request.Code }, cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError(
                $"Form '{request.Code}' không tìm thấy hoặc chưa active."));

        // Step 2: load with fields via EF repository — re-pins SESSION_CONTEXT to the resolved
        // tenant so the read passes the standard FormTemplates RLS FILTER under a non-db_owner login.
        var template = await templateRepository.GetPublicWithFieldsAsync(
            row.Id, row.TenantId, cancellationToken);

        if (template is null)
            return Result.Fail(new NotFoundError($"Form '{request.Code}' không tìm thấy."));

        // Pick the ACTIVE field from THIS template's own fields by its stable key. A fieldKey that does
        // not belong to the template resolves to nothing → 404, so no cross-tenant / cross-template field
        // is ever addressable from the anonymous surface (red-team C1 closed). The `IsActive` filter
        // mirrors every sibling public handler (EvaluatePublicFormulas, GetPublicSignatureDocHash,
        // GetPublicForm) — a deactivated field is off the public surface, and it also disambiguates the
        // legitimate inactive+active same-key pair the AddField uniqueness guard (scoped to IsActive) allows.
        var field = template.Fields.FirstOrDefault(f => f.FieldKey == request.FieldKey && f.IsActive);
        if (field is null)
            return Result.Fail(new NotFoundError(
                $"Field '{request.FieldKey}' không thuộc form '{request.Code}'."));

        // Static options (Dropdown, MultiSelect, etc.) — mapped directly, never reach the resolver.
        if (field.Options.Count > 0)
        {
            var staticOptions = field.Options
                .OrderBy(o => o.DisplayOrder)
                .Select(o => new DataSourceOptionDto(o.Value, o.LabelEn, o.LabelVi))
                .ToList();
            return Result.Ok<IReadOnlyList<DataSourceOptionDto>>(staticOptions);
        }

        // Reference types resolved via data-source infrastructure — tenant is server-resolved,
        // never from the caller. ParentValues forwarded identically to the authed handler.
        if (ReferenceTypes.Contains(field.Type) && !string.IsNullOrWhiteSpace(field.DataSourceJson))
        {
            var resolver = resolverFactory.Create(field.Type);
            var resolved = await resolver.ResolveAsync(
                field.DataSourceJson, row.TenantId, request.ParentValues, cancellationToken);
            return Result.Ok(resolved);
        }

        return Result.Ok<IReadOnlyList<DataSourceOptionDto>>([]);
    }

    // Dapper projection row — same minimal shape as EvaluatePublicFormulasQueryHandler.TemplateIdRow.
    // Internal (not private) so unit tests can stub QueryFirstOrDefaultWithPublicFormsBypassAsync<T>
    // against this exact row type.
    internal sealed class TemplateIdRow
    {
        public long Id { get; set; }
        public Guid TenantId { get; set; }
    }
}
