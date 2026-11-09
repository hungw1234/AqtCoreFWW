using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Queries.GetRetentionPolicies;

public sealed class GetRetentionPoliciesQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetRetentionPoliciesQuery, Result<IReadOnlyList<RetentionPolicyDto>>>
{
    public async Task<Result<IReadOnlyList<RetentionPolicyDto>>> Handle(
        GetRetentionPoliciesQuery request,
        CancellationToken cancellationToken)
    {
        var activeFilter = request.IsActive.HasValue ? "AND IsActive = @IsActive" : string.Empty;

        var sql = $"""
            SELECT Id, Name, DocumentType, RetainDays,
                   ArchiveAfterDays, DestroyAfterDays, IsActive, TenantId
            FROM files.RetentionPolicies
            WHERE TenantId = @TenantId
              AND IsDeleted = 0
              {activeFilter}
            ORDER BY DocumentType, Name
            """;

        var rows = await db.QueryAsync<RetentionPolicyDto>(
            sql, new { request.TenantId, request.IsActive }, cancellationToken);

        return Result.Ok<IReadOnlyList<RetentionPolicyDto>>(rows.ToList());
    }
}
