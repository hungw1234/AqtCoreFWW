using AqtCoreFW.Audit.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Queries.GetClassificationInventory;

public sealed class GetClassificationInventoryQueryHandler(IDataClassificationService scanner)
    : IRequestHandler<GetClassificationInventoryQuery, Result<ClassificationInventoryDto>>
{
    public Task<Result<ClassificationInventoryDto>> Handle(
        GetClassificationInventoryQuery request,
        CancellationToken cancellationToken)
    {
        var dto = scanner.GetInventory();
        return Task.FromResult(Result.Ok(dto));
    }
}
