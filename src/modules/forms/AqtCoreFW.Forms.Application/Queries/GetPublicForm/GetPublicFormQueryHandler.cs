using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Application.Telemetry;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentResults;
using MediatR;
using System.Diagnostics;

namespace AqtCoreFW.Forms.Application.Queries.GetPublicForm;

public sealed class GetPublicFormQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetPublicFormQuery, Result<FormTemplateDto>>
{
    public async Task<Result<FormTemplateDto>> Handle(
        GetPublicFormQuery request,
        CancellationToken cancellationToken)
    {
        const string templateSql = """
            SELECT Id, PublicId, TenantId, Name, NameVi, Code, StorageMode, Status, Version, CreatedAt
            FROM forms.FormTemplates
            WHERE Code = @Code AND Status = 'Active' AND IsDeleted = 0 AND IsPublic = 1
            """;

        const string fieldsSql = """
            SELECT Id, FieldKey, Type, LabelVi, LabelEn, DisplayOrder,
                   Required, IsActive, AddedInVersion, ValidationRulesJson,
                   VisibilityRulesJson, RequiredIfJson, ParentFieldId
            FROM forms.FormFields
            WHERE FormTemplateId = @TemplateId AND IsActive = 1 AND IsDeleted = 0
            ORDER BY DisplayOrder
            """;

        const string optionsSql = """
            SELECT o.Id, o.FormFieldId, o.Value, o.LabelVi, o.LabelEn, o.DisplayOrder
            FROM forms.FormFieldOptions o
            WHERE o.FormFieldId IN (
                SELECT Id FROM forms.FormFields
                WHERE FormTemplateId = @TemplateId AND IsActive = 1 AND IsDeleted = 0
            ) AND o.IsDeleted = 0
            ORDER BY o.DisplayOrder
            """;

        // Anonymous caller has no ambient tenant → resolve the public template by Code on a
        // public_forms_bypass connection so the FormTemplates RLS FILTER carve-out admits the
        // IsPublic=1 row under a non-db_owner login. Child field/option reads below are not
        // RLS-policied, so they need no bypass.
        var row = await db.QueryFirstOrDefaultWithPublicFormsBypassAsync<PublicTemplateRow>(
            templateSql, new { Code = request.TemplateCode }, cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError($"Form '{request.TemplateCode}' không tìm thấy hoặc chưa active."));

        // UX-baseline telemetry: a public template was loaded — the "opened" side of completion-rate.
        // Counted only after the row resolves (a 404 is not an open). Side-effect only.
        FormMetrics.FormOpened.Add(1, new TagList
        {
            { "template_code", row.Code },
            { "is_public", true },
        });

        var fieldRows = (await db.QueryAsync<PublicFieldRow>(
            fieldsSql, new { TemplateId = row.Id }, cancellationToken)).ToList();

        var optionRows = (await db.QueryAsync<PublicOptionRow>(
            optionsSql, new { TemplateId = row.Id }, cancellationToken)).ToList();

        // Group options by FormFieldId (long, T1 FK)
        var optsByField = optionRows.GroupBy(o => o.FormFieldId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var fields = fieldRows.Select(f =>
        {
            FormFieldValidationRulesDto? rules = null;
            if (!string.IsNullOrWhiteSpace(f.ValidationRulesJson))
            {
                try
                {
                    rules = System.Text.Json.JsonSerializer.Deserialize<FormFieldValidationRulesDto>(
                        f.ValidationRulesJson,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { /* ignore malformed */ }
            }

            var opts = optsByField.TryGetValue(f.Id, out var os)
                ? os.Select(o => new FormFieldOptionDto(o.Id, o.Value, o.LabelVi, o.LabelEn, o.DisplayOrder))
                    .ToList<FormFieldOptionDto>()
                : null;

            return new FormFieldDto(
                f.Id, f.FieldKey, f.Type,
                f.LabelVi, f.LabelEn,
                f.DisplayOrder, f.Required, f.IsActive, f.AddedInVersion,
                rules, opts,
                VisibilityRulesJson: f.VisibilityRulesJson,
                RequiredIfJson: f.RequiredIfJson,
                ParentFieldId: f.ParentFieldId);
        }).ToList();

        // H3-BE-Sec: Do not expose TenantId to anonymous callers — use Guid.Empty
        return Result.Ok(new FormTemplateDto(
            row.PublicId, Guid.Empty, row.Name, row.NameVi,
            row.Code, row.StorageMode, row.Status,
            row.Version, row.CreatedAt, fields));
    }

    // Dapper requires parameterless ctor — use class for nullable field safety
    private sealed class PublicTemplateRow
    {
        public long Id { get; set; }
        public Guid PublicId { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string NameVi { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string StorageMode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Version { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class PublicFieldRow
    {
        public long Id { get; set; }
        public string FieldKey { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string LabelVi { get; set; } = string.Empty;
        public string LabelEn { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public bool Required { get; set; }
        public bool IsActive { get; set; }
        public int AddedInVersion { get; set; }
        public string? ValidationRulesJson { get; set; }
        public string? VisibilityRulesJson { get; set; }
        public string? RequiredIfJson { get; set; }
        // Internal FK to parent FormField (bigint T1). Non-null for TableField child columns; null for
        // top-level fields. Projected so the FE table widget discovers child columns by id (public path
        // must match authed path for parent-child relationships to be visible in public forms too).
        public long? ParentFieldId { get; set; }
    }

    private sealed class PublicOptionRow
    {
        public long Id { get; set; }
        public long FormFieldId { get; set; }
        public string Value { get; set; } = string.Empty;
        public string LabelVi { get; set; } = string.Empty;
        public string LabelEn { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
    }
}
