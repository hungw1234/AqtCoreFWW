using AqtCoreFW.Contracts.Domain.Entities;
using AqtCoreFW.Contracts.Domain.Repositories;
using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Errors;

namespace AqtCoreFW.Contracts.Application.Commands.CreateContract;

public sealed class CreateContractCommandHandler(IContractRepository repository)
    : IRequestHandler<CreateContractCommand, Result<long>>
{
    public async Task<Result<long>> Handle(
        CreateContractCommand request,
        CancellationToken cancellationToken)
    {
        if (request.EndDate < request.StartDate)
            return Result.Fail<long>(new ValidationError(
                "Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.",
                nameof(request.EndDate)));
        if (await repository.ContractNumberExistsAsync(
                request.ContractNumber, ct: cancellationToken))
            return Result.Fail<long>(new ConflictError("Số hợp đồng đã tồn tại."));

        var contract = Contract.Create(
            request.TenantId,
            request.ContractNumber,
            request.Title,
            request.CounterpartyName,
            request.StartDate,
            request.EndDate,
            request.Description,
            request.CreatedBy);

        await repository.AddAsync(contract, cancellationToken);
        return Result.Ok(contract.Id);
    }
}
