using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.GetFormAnalytics;

public sealed class GetFormAnalyticsQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetFormAnalyticsQuery, Result<FormAnalyticsDto>>
{
    public async Task<Result<FormAnalyticsDto>> Handle(
        GetFormAnalyticsQuery request,
        CancellationToken cancellationToken)
    {
        // Resolve public Guid AK → internal long Id (FormSubmissions FK = long)
        const string resolveSql = """
            SELECT Id FROM forms.FormTemplates
            WHERE PublicId = @TemplatePublicId AND TenantId = @TenantId AND IsDeleted = 0
            """;

        var internalId = await db.QueryFirstOrDefaultAsync<long?>(
            resolveSql, new { request.TemplatePublicId, request.TenantId }, cancellationToken);

        if (internalId is null)
            return Result.Fail(new NotFoundError($"FormTemplate {request.TemplatePublicId} không tìm thấy."));

        // Status counts in a single query
        const string countSql = """
            SELECT
                COUNT(*) AS TotalSubmissions,
                SUM(CASE WHEN Status = 'Pending'  THEN 1 ELSE 0 END) AS PendingCount,
                SUM(CASE WHEN Status = 'Approved' THEN 1 ELSE 0 END) AS ApprovedCount,
                SUM(CASE WHEN Status = 'Rejected' THEN 1 ELSE 0 END) AS RejectedCount
            FROM forms.FormSubmissions
            WHERE FormTemplateId = @InternalId AND TenantId = @TenantId AND IsDeleted = 0
            """;

        // Submissions per calendar day over the last 30 days
        const string byDateSql = """
            SELECT
                CONVERT(varchar(10), SubmittedAt, 23) AS Date,
                COUNT(*) AS Count
            FROM forms.FormSubmissions
            WHERE FormTemplateId = @InternalId
              AND TenantId = @TenantId
              AND IsDeleted = 0
              AND SubmittedAt >= DATEADD(day, -30, GETUTCDATE())
            GROUP BY CONVERT(varchar(10), SubmittedAt, 23)
            ORDER BY Date
            """;

        var p = new { InternalId = internalId.Value, request.TenantId };

        var counts = await db.QueryFirstOrDefaultAsync<CountRow>(countSql, p, cancellationToken);
        var byDate = (await db.QueryAsync<SubmissionsByDateDto>(byDateSql, p, cancellationToken)).ToList();

        // AvgFieldsFilled: count top-level JSON keys via OPENJSON (accurate for any JSON shape)
        const string avgSql = """
            SELECT AVG(CAST(kc.KeyCount AS FLOAT))
            FROM forms.FormSubmissions s
            CROSS APPLY (SELECT COUNT(*) AS KeyCount FROM OPENJSON(s.SubmissionData)) kc
            WHERE s.FormTemplateId = @InternalId AND s.TenantId = @TenantId
              AND s.IsDeleted = 0 AND s.SubmissionData IS NOT NULL
              AND ISJSON(s.SubmissionData) = 1
            """;

        var avgRaw = await db.QueryFirstOrDefaultAsync<double?>(avgSql, p, cancellationToken);
        var avg = Math.Round(avgRaw ?? 0, 1);

        var dto = new FormAnalyticsDto(
            counts?.TotalSubmissions ?? 0,
            counts?.PendingCount ?? 0,
            counts?.ApprovedCount ?? 0,
            counts?.RejectedCount ?? 0,
            avg,
            byDate);

        return Result.Ok(dto);
    }

    private sealed record CountRow(
        int TotalSubmissions,
        int PendingCount,
        int ApprovedCount,
        int RejectedCount);
}
