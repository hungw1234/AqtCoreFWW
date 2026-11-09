using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Audit.Application.Queries.GetCompliancePolicies;

public sealed class GetCompliancePoliciesQueryHandler(
    [FromKeyedServices(ReadConnectionKeys.Audit)] IReadDbConnection db)
    : IRequestHandler<GetCompliancePoliciesQuery, Result<IReadOnlyList<CompliancePolicyDto>>>
{
    public async Task<Result<IReadOnlyList<CompliancePolicyDto>>> Handle(
        GetCompliancePoliciesQuery request,
        CancellationToken cancellationToken)
    {
        var sql = request.IncludeDisabled
            ? "SELECT Id, Name, Category, Rules, Enforcement, IsEnabled, CreatedAt FROM audit.CompliancePolicies ORDER BY Name"
            : "SELECT Id, Name, Category, Rules, Enforcement, IsEnabled, CreatedAt FROM audit.CompliancePolicies WHERE IsEnabled = 1 ORDER BY Name";

        var rows = await db.QueryAsync<CompliancePolicyDto>(sql, null, cancellationToken);
        return Result.Ok<IReadOnlyList<CompliancePolicyDto>>(rows.ToList());
    }
}
