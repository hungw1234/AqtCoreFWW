using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.Integration.Application.DTOs;
using AqtCoreFW.Integration.Domain.Repositories;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Integration.Application.Queries.ListMessageLogs;

public sealed class ListMessageLogsQueryHandler(
    IReadDbConnection db,
    IPartnerRepository partnerRepository,
    IContractRepository contractRepository)
    : IRequestHandler<ListMessageLogsQuery, Result<PagedResult<MessageLogDto>>>
{
    public async Task<Result<PagedResult<MessageLogDto>>> Handle(
        ListMessageLogsQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);

        // Resolve optional T2 PartnerPublicId (Guid AK) → internal long PK.
        long? partnerInternalId = null;
        if (request.PartnerPublicId.HasValue)
        {
            var partner = await partnerRepository.GetByPublicIdAsync(
                request.PartnerPublicId.Value, request.TenantId, cancellationToken);
            if (partner is null)
                return Result.Ok(new PagedResult<MessageLogDto>([], 0,
                    new PaginationMeta(page, pageSize, 0, null, null, false)));
            partnerInternalId = partner.Id;
        }

        // Resolve optional T2 ContractPublicId (Guid AK) → internal long PK.
        long? contractInternalId = null;
        if (request.ContractPublicId.HasValue)
        {
            var contract = await contractRepository.GetByPublicIdAsync(
                request.ContractPublicId.Value, request.TenantId, cancellationToken);
            if (contract is null)
                return Result.Ok(new PagedResult<MessageLogDto>([], 0,
                    new PaginationMeta(page, pageSize, 0, null, null, false)));
            contractInternalId = contract.Id;
        }

        var p = new DynamicParameters();
        p.Add("TenantId", request.TenantId);
        p.Add("PartnerInternalId", partnerInternalId);   // long? — resolved from T2 AK
        p.Add("ContractInternalId", contractInternalId); // long? — resolved from T2 AK
        p.Add("Offset", (page - 1) * pageSize);
        p.Add("PageSize", pageSize);

        var clauses = new List<string>
        {
            "ml.TenantId = @TenantId",
            "(@PartnerInternalId IS NULL OR ml.PartnerId = @PartnerInternalId)",
            "(@ContractInternalId IS NULL OR ml.ContractId = @ContractInternalId)"
        };

        // T3 plain-class has no IsDeleted column — no soft-delete filter.

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var escaped = request.SearchTerm.EscapeSqlLike();
            p.Add("SearchPattern", $"%{escaped}%");
            clauses.Add("(ml.Direction LIKE @SearchPattern ESCAPE '\\' OR ml.MessageType LIKE @SearchPattern ESCAPE '\\')");
        }

        var where = string.Join(" AND ", clauses);

        var countSql = $"SELECT COUNT(*) FROM [integration].MessageLogs ml WHERE {where}";

        // T3: SELECT ml.MessageId AS Id (Guid AK → MessageLogDto.Id Guid).
        // PartnerId projected as Guid: JOIN Partners p ON p.Id = ml.PartnerId → p.PublicId AS PartnerId.
        // ContractId projected as Guid?: LEFT JOIN Contracts c ON c.Id = ml.ContractId → c.PublicId AS ContractId.
        // No UpdatedAt on T3 plain-class — project typed NULL (CAST AS datetime2 so Dapper infers DateTime, not INT).
        var dataSql = $"""
            SELECT ml.MessageId AS Id, ml.TenantId,
                   p.PublicId AS PartnerId, c.PublicId AS ContractId,
                   ml.Direction, ml.MessageType, ml.Payload, ml.Status,
                   ml.CorrelationId, ml.SentAt, ml.AcknowledgedAt,
                   ml.CreatedAt, CAST(NULL AS datetime2) AS UpdatedAt
            FROM [integration].MessageLogs ml
            JOIN [integration].Partners p ON p.Id = ml.PartnerId
            LEFT JOIN [integration].Contracts c ON c.Id = ml.ContractId
            WHERE {where}
            ORDER BY ml.SentAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var total = await db.QuerySingleAsync<int>(countSql, p, cancellationToken);
        var items = (await db.QueryAsync<MessageLogDto>(dataSql, p, cancellationToken)).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages,
            null, null, page < totalPages);

        return Result.Ok(new PagedResult<MessageLogDto>(items, total, meta));
    }
}
