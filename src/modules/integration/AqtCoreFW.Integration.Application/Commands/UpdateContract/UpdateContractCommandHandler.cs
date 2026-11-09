using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Integration.Application.DTOs;
using AqtCoreFW.Integration.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Integration.Application.Commands.UpdateContract;

public sealed class UpdateContractCommandHandler(
    IContractRepository repository,
    IReadDbConnection db,
    ICurrentUser currentUser)
    : IRequestHandler<UpdateContractCommand, Result<ContractDto>>
{
    public async Task<Result<ContractDto>> Handle(
        UpdateContractCommand request, CancellationToken cancellationToken)
    {
        // T2 entity: request.Id is PublicId (Guid AK) — use GetByPublicIdAsync scoped to tenant.
        var contract = await repository.GetByPublicIdAsync(request.Id, request.TenantId, cancellationToken);
        if (contract is null)
            return Result.Fail(new NotFoundError($"Contract {request.Id} not found."));

        contract.Update(request.Title, request.Description,
            request.EffectiveDate, request.ExpiryDate,
            request.DataScopeJson, currentUser.UserId);

        await repository.UpdateAsync(contract, cancellationToken);

        // Post-update: project Guid AKs via JOIN (Contract stores long FK to Partner internally).
        // T2: Id = PublicId (Guid AK). PartnerId = Partner.PublicId (Guid AK).
        // Status = nvarchar(30) string conversion (ContractConfiguration:18-20) → direct map, no CAST.
        // NotFoundError on null result preserves PR-B null-Partner regression guard (`110a14b5`):
        // dangling FK or transactional anomaly returns NotFound rather than throwing.
        const string sql = """
            SELECT c.PublicId AS Id, c.TenantId,
                   p.PublicId AS PartnerId,
                   c.Title, c.Description,
                   c.EffectiveDate, c.ExpiryDate,
                   c.Status, c.DataScopeJson,
                   c.CreatedAt, c.UpdatedAt
            FROM [integration].Contracts c
            JOIN [integration].Partners p ON p.Id = c.PartnerId
            WHERE c.PublicId = @Id AND c.TenantId = @TenantId
            """;

        var dto = await db.QueryFirstOrDefaultAsync<ContractDto>(
            sql, new { request.Id, request.TenantId }, cancellationToken);

        return dto is null
            ? Result.Fail(new NotFoundError($"Contract {request.Id} not found after update."))
            : Result.Ok(dto);
    }
}
