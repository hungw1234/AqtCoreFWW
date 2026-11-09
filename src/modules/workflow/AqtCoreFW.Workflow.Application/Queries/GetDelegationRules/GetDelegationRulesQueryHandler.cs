using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetDelegationRules;

public sealed class GetDelegationRulesQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetDelegationRulesQuery, Result<IReadOnlyList<DelegationRuleDto>>>
{
    public async Task<Result<IReadOnlyList<DelegationRuleDto>>> Handle(
        GetDelegationRulesQuery request,
        CancellationToken cancellationToken)
    {
        var userFilter = request.DelegatorUserId.HasValue
            ? "AND DelegatorUserId = @DelegatorUserId"
            : string.Empty;

        var sql = $"""
            SELECT Id, DelegatorUserId, DelegateUserId,
                   ValidFrom, ValidUntil, IsActive, TenantId,
                   CreatedAt, CreatedBy
            FROM workflow.DelegationRules
            WHERE TenantId = @TenantId
              AND IsDeleted = 0
              {userFilter}
            ORDER BY ValidFrom DESC
            """;

        var rows = await db.QueryAsync<RuleRow>(
            sql,
            new { request.TenantId, request.DelegatorUserId },
            cancellationToken);

        var result = rows
            .Select(r => new DelegationRuleDto(
                r.Id, r.DelegatorUserId, r.DelegateUserId,
                r.ValidFrom, r.ValidUntil, r.IsActive,
                r.TenantId, r.CreatedAt, r.CreatedBy))
            .ToList();

        return Result.Ok<IReadOnlyList<DelegationRuleDto>>(result);
    }

    // DelegationRule is T1: Id (long). DelegatorUserId/DelegateUserId are Guid (FK exceptions).
    internal sealed class RuleRow
    {
        public long Id { get; set; }
        public Guid DelegatorUserId { get; set; }
        public Guid DelegateUserId { get; set; }
        public DateTimeOffset ValidFrom { get; set; }
        public DateTimeOffset ValidUntil { get; set; }
        public bool IsActive { get; set; }
        public Guid TenantId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
    }
}
