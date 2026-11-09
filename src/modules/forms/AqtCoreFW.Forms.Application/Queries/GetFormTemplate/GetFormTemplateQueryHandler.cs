using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.GetFormTemplate;

public sealed class GetFormTemplateQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetFormTemplateQuery, Result<FormTemplateDto>>
{
    public async Task<Result<FormTemplateDto>> Handle(
        GetFormTemplateQuery request,
        CancellationToken cancellationToken)
    {
        // Query by PublicId (Guid AK) — T2 external addressing. SELECT Id to drive field sub-queries.
        const string templateSql = """
            SELECT Id, PublicId, TenantId, Name, NameVi, Code, StorageMode, Status, Version, CreatedAt,
                   RequiresConsent, ConsentText, ApprovalWorkflowDefinitionId, IsPublic
            FROM forms.FormTemplates
            WHERE PublicId = @TemplatePublicId AND TenantId = @TenantId AND IsDeleted = 0
            """;

        const string fieldsSql = """
            SELECT Id, FieldKey, Type, LabelVi, LabelEn, DisplayOrder,
                   Required, IsActive, AddedInVersion, ValidationRulesJson,
                   VisibilityRulesJson, RequiredIfJson, ParentFieldId
            FROM forms.FormFields
            WHERE FormTemplateId = @InternalId AND IsDeleted = 0
            ORDER BY DisplayOrder
            """;

        const string optionsSql = """
            SELECT o.Id, o.FormFieldId, o.Value, o.LabelVi, o.LabelEn, o.DisplayOrder
            FROM forms.FormFieldOptions o
            INNER JOIN forms.FormFields f ON f.Id = o.FormFieldId
            WHERE f.FormTemplateId = @InternalId AND o.IsDeleted = 0
            ORDER BY o.DisplayOrder
            """;

        var row = await db.QueryFirstOrDefaultAsync<FormTemplateRow>(
            templateSql, new { request.TemplatePublicId, request.TenantId }, cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError($"FormTemplate {request.TemplatePublicId} không tìm thấy."));

        var fieldRows = (await db.QueryAsync<FormFieldRow>(
            fieldsSql, new { InternalId = row.Id }, cancellationToken)).ToList();

        var optionRows = (await db.QueryAsync<FormFieldOptionRow>(
            optionsSql, new { InternalId = row.Id }, cancellationToken)).ToList();

        // Group options by FormFieldId (long, T1 FK)
        var optionsByField = optionRows.GroupBy(o => o.FormFieldId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var fields = fieldRows.Select(f => MapField(f, optionsByField)).ToList();

        return Result.Ok(new FormTemplateDto(
            row.PublicId, row.TenantId, row.Name, row.NameVi,
            row.Code, row.StorageMode, row.Status,
            row.Version, row.CreatedAt, fields,
            RequiresConsent: row.RequiresConsent,
            ConsentText: row.ConsentText,
            ApprovalWorkflowDefinitionId: row.ApprovalWorkflowDefinitionId,
            IsPublic: row.IsPublic));
    }

    private static FormFieldDto MapField(
        FormFieldRow f,
        Dictionary<long, List<FormFieldOptionRow>> optionsByField)
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

        var options = optionsByField.TryGetValue(f.Id, out var opts)
            ? opts.Select(o => new FormFieldOptionDto(o.Id, o.Value, o.LabelVi, o.LabelEn, o.DisplayOrder))
                  .ToList()
            : null;

        return new FormFieldDto(
            f.Id, f.FieldKey, f.Type,
            f.LabelVi, f.LabelEn,
            f.DisplayOrder, f.Required, f.IsActive, f.AddedInVersion,
            rules, options,
            VisibilityRulesJson: f.VisibilityRulesJson,
            RequiredIfJson: f.RequiredIfJson,
            ParentFieldId: f.ParentFieldId);
    }

    // Dapper requires parameterless ctor — positional records with nullable params fail materialization
    private sealed class FormTemplateRow
    {
        public long Id { get; set; }      // internal PK, used for field sub-queries
        public Guid PublicId { get; set; }  // external AK projected into DTO
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string NameVi { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string StorageMode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Version { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        // PDPL consent + approval-workflow config — projected so the FE Settings tab reads back
        // the persisted state instead of record defaults (fixes write-only consent/workflow toggles).
        public bool RequiresConsent { get; set; }
        public string? ConsentText { get; set; }
        public Guid? ApprovalWorkflowDefinitionId { get; set; }
        public bool IsPublic { get; set; }  // drives the FE public-access toggle state
    }

    // Dapper requires parameterless ctor — positional records with nullable params fail materialization
    private sealed class FormFieldRow
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
        // top-level fields. Projected into DTO so the FE table widget discovers child columns by id
        // instead of the prefix-based fallback that AddFormField regex blocks (no dots in FieldKey).
        public long? ParentFieldId { get; set; }
    }

    // Dapper requires parameterless ctor — positional records with nullable params fail materialization
    private sealed class FormFieldOptionRow
    {
        public long Id { get; set; }
        public long FormFieldId { get; set; }
        public string Value { get; set; } = string.Empty;
        public string LabelVi { get; set; } = string.Empty;
        public string LabelEn { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
    }
}
