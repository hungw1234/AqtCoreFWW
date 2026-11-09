using AqtCoreFW.Contracts.Domain.Repositories;
using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Errors;

namespace AqtCoreFW.Contracts.Application.Commands.UpdateContract;

public sealed class UpdateContractCommandHandler(IContractRepository repository)
    : IRequestHandler<UpdateContractCommand, Result<long>>
{
    public async Task<Result<long>> Handle(
        UpdateContractCommand request,
        CancellationToken cancellationToken)
    {
        if (request.EndDate < request.StartDate)
            return Result.Fail<long>(new ValidationError(
                "Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.",
                nameof(request.EndDate)));
        var result = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (result.IsFailed)
            return Result.Fail<long>(result.Errors);
        if (await repository.ContractNumberExistsAsync(
                request.ContractNumber, request.Id, cancellationToken))
            return Result.Fail<long>(new ConflictError("Số hợp đồng đã tồn tại."));

        var contract = result.Value;
        contract.Update(
            request.ContractNumber,
            request.Title,
            request.CounterpartyName,
            request.StartDate,
            request.EndDate,
            request.Status,
            request.Description,
            request.UpdatedBy);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Ok(contract.Id);
    }
}
