using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.GetFormVersionDiff;

public sealed class GetFormVersionDiffQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetFormVersionDiffQuery, Result<FormVersionDiffDto>>
{
    public async Task<Result<FormVersionDiffDto>> Handle(
        GetFormVersionDiffQuery request,
        CancellationToken cancellationToken)
    {
        // Resolve public Guid AK → internal long Id
        const string resolveSql = """
            SELECT Id FROM forms.FormTemplates
            WHERE PublicId = @TemplatePublicId AND TenantId = @TenantId AND IsDeleted = 0
            """;

        var internalId = await db.QueryFirstOrDefaultAsync<long?>(
            resolveSql, new { request.TemplatePublicId, request.TenantId }, cancellationToken);

        if (internalId is null)
            return Result.Fail(new NotFoundError($"FormTemplate {request.TemplatePublicId} không tìm thấy."));

        // Load all fields for this template (including inactive — needed to detect removals)
        const string fieldsSql = """
            SELECT FieldKey, LabelVi, LabelEn, AddedInVersion, IsActive
            FROM forms.FormFields
            WHERE FormTemplateId = @InternalId
            ORDER BY AddedInVersion, FieldKey
            """;

        var allFields = (await db.QueryAsync<DiffFieldRow>(
            fieldsSql, new { InternalId = internalId.Value }, cancellationToken)).ToList();

        // Fields "at" a version = added in any version <= that version
        var atFrom = allFields.Where(f => f.AddedInVersion <= request.FromVersion).ToList();
        var atTo   = allFields.Where(f => f.AddedInVersion <= request.ToVersion).ToList();

        var fromKeys = atFrom.ToDictionary(f => f.FieldKey);
        var toKeys   = atTo.ToDictionary(f => f.FieldKey);

        var added   = toKeys.Keys.Except(fromKeys.Keys)
                          .Select(k => toKeys[k].ToDto()).ToList();

        var removed = fromKeys.Keys.Except(toKeys.Keys)
                          .Select(k => fromKeys[k].ToDto()).ToList();

        // Modified: present in both but IsActive changed
        var modified = fromKeys.Keys.Intersect(toKeys.Keys)
                           .Where(k => fromKeys[k].IsActive != toKeys[k].IsActive)
                           .Select(k => toKeys[k].ToDto()).ToList();

        return Result.Ok(new FormVersionDiffDto(
            request.FromVersion, request.ToVersion,
            added, removed, modified));
    }

    private sealed record DiffFieldRow(
        string FieldKey, string LabelVi, string LabelEn,
        int AddedInVersion, bool IsActive)
    {
        public DiffFieldDto ToDto() =>
            new(FieldKey, LabelVi, LabelEn, AddedInVersion, IsActive);
    }
}
