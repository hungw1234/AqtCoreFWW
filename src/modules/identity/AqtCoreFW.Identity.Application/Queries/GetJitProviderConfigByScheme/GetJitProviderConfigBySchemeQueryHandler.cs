using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.GetJitProviderConfigByScheme;

/// <summary>Read-side query via Dapper — returns single config by auth scheme name.</summary>
public sealed class GetJitProviderConfigBySchemeQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetJitProviderConfigBySchemeQuery, Result<JitProviderConfigDto>>
{
    public async Task<Result<JitProviderConfigDto>> Handle(
        GetJitProviderConfigBySchemeQuery query, CancellationToken ct)
    {
        var sql = """
            SELECT j.Id, j.Scheme, j.DisplayName, j.ProviderType, j.JitEnabled,
                   j.DefaultRoleName, j.RequireApproval, j.ClaimMappingJson,
                   j.DefaultTenantId, j.AllowedDomainsJson, j.MaxProvisionsPerHour,
                   j.IsActive
            FROM [identity].JitProviderConfigs j
            WHERE j.Scheme = @Scheme
              AND j.IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<JitProviderConfigDto>(sql, new { query.Scheme });

        if (row is null)
            return Result.Fail<JitProviderConfigDto>(
                new NotFoundError($"JitProviderConfig with scheme '{query.Scheme}' not found"));

        return Result.Ok(row);
    }
}
