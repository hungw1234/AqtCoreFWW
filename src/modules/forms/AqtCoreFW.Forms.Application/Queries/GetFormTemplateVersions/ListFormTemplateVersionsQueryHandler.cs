using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.GetFormTemplateVersions;

/// <summary>List all snapshots for a template (newest first, summary projection — no SchemaJson).</summary>
public sealed class ListFormTemplateVersionsQueryHandler(IReadDbConnection db)
    : IRequestHandler<ListFormTemplateVersionsQuery, Result<IReadOnlyList<FormTemplateVersionSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<FormTemplateVersionSummaryDto>>> Handle(
        ListFormTemplateVersionsQuery request,
        CancellationToken cancellationToken)
    {
        const string templateSql = """
            SELECT Id FROM forms.FormTemplates
            WHERE PublicId = @TemplatePublicId AND TenantId = @TenantId AND IsDeleted = 0
            """;

        var templateInternalId = await db.QueryFirstOrDefaultAsync<long?>(
            templateSql, new { request.TemplatePublicId, request.TenantId }, cancellationToken);

        if (templateInternalId is null)
            return Result.Fail(new NotFoundError($"FormTemplate {request.TemplatePublicId} không tìm thấy."));

        // InternalId already resolved tenant-scoped above; v.TenantId clause is defense-in-depth
        // for the same reason as the Get handler (see [[phase6-rls-restore-shipped-260512]] memory).
        const string versionsSql = """
            SELECT VersionNumber, CreatedAt, CreatedBy
            FROM forms.FormTemplateVersions
            WHERE FormTemplateId = @InternalId AND TenantId = @TenantId AND IsDeleted = 0
            ORDER BY VersionNumber DESC
            """;

        var rows = (await db.QueryAsync<VersionRow>(
            versionsSql,
            new { InternalId = templateInternalId.Value, request.TenantId },
            cancellationToken)).ToList();

        IReadOnlyList<FormTemplateVersionSummaryDto> dtos = rows
            .Select(r => new FormTemplateVersionSummaryDto(
                request.TemplatePublicId, r.VersionNumber, r.CreatedAt, r.CreatedBy))
            .ToList();

        return Result.Ok(dtos);
    }

    // Dapper requires parameterless ctor — positional records with nullable params fail materialization
    private sealed class VersionRow
    {
        public int VersionNumber { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
    }
}

/// <summary>Single-snapshot read (full SchemaJson). Caller parses JSON.</summary>
public sealed class GetFormTemplateVersionQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetFormTemplateVersionQuery, Result<FormTemplateVersionDto>>
{
    public async Task<Result<FormTemplateVersionDto>> Handle(
        GetFormTemplateVersionQuery request,
        CancellationToken cancellationToken)
    {
        // Defense-in-depth: filter TenantId on BOTH join sides — RLS is first line of defense
        // but production login may run as db_owner (see [[phase6-rls-restore-shipped-260512]]),
        // so application-layer tenant filter must hold even if RLS is bypassed.
        const string sql = """
            SELECT v.VersionNumber, v.SchemaJson, v.CreatedAt, v.CreatedBy
            FROM forms.FormTemplateVersions v
            INNER JOIN forms.FormTemplates t ON t.Id = v.FormTemplateId
            WHERE t.PublicId = @TemplatePublicId
              AND v.VersionNumber = @VersionNumber
              AND v.TenantId = @TenantId
              AND t.TenantId = @TenantId
              AND v.IsDeleted = 0
              AND t.IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<VersionDetailRow>(
            sql,
            new { request.TemplatePublicId, request.VersionNumber, request.TenantId },
            cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError(
                $"FormTemplate {request.TemplatePublicId} v{request.VersionNumber} không tìm thấy."));

        return Result.Ok(new FormTemplateVersionDto(
            request.TemplatePublicId, row.VersionNumber, row.SchemaJson, row.CreatedAt, row.CreatedBy));
    }

    // Dapper requires parameterless ctor — positional records with nullable params fail materialization
    private sealed class VersionDetailRow
    {
        public int VersionNumber { get; set; }
        public string SchemaJson { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
    }
}
